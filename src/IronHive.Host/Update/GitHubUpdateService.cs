using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronHive.Host.Update;

/// <summary>
/// Update service that checks GitHub Releases for updates (standalone binary)
/// or NuGet for updates (dotnet tool installation).
/// </summary>
public class GitHubUpdateService : IUpdateService
{
    private const string NuGetPackageId = "IronHive.Host";
    private const string NuGetPackageIdLower = "ironhive.cli";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly Version _currentVersion;
    private readonly bool _isDotnetTool;

    public GitHubUpdateService(HttpClient httpClient, string owner = "iyulab", string repo = "ironhive-cli-releases")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _owner = owner;
        _repo = repo;
        _currentVersion = GetCurrentVersion();
        _isDotnetTool = DetectDotnetToolInstallation();
    }

    /// <inheritdoc />
    public Version CurrentVersion => _currentVersion;

    /// <summary>
    /// Gets whether the CLI was installed as a dotnet tool.
    /// </summary>
    public bool IsDotnetToolInstallation => _isDotnetTool;

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isDotnetTool)
            {
                return await CheckForNuGetUpdateAsync(cancellationToken);
            }

            return await CheckForGitHubUpdateAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<UpdateInfo?> CheckForGitHubUpdateAsync(CancellationToken cancellationToken)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        if (release is null)
        {
            return null;
        }

        var latestVersion = ParseVersion(release.TagName);
        if (latestVersion is null)
        {
            return null;
        }

        var downloadUrl = GetDownloadUrlForCurrentPlatform(release);

        return new UpdateInfo
        {
            LatestVersion = latestVersion,
            CurrentVersion = _currentVersion,
            IsPrerelease = release.Prerelease,
            ReleaseNotes = release.Body,
            ReleaseUrl = release.HtmlUrl,
            DownloadUrl = downloadUrl
        };
    }

    private async Task<UpdateInfo?> CheckForNuGetUpdateAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{NuGetPackageIdLower}/index.json";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", $"ironhive-cli/{_currentVersion}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadFromJsonAsync<NuGetVersionIndex>(JsonOptions, cancellationToken);
        if (content?.Versions is null || content.Versions.Count == 0)
        {
            return null;
        }

        // Get the latest stable version (not prerelease)
        var latestVersionString = content.Versions
            .Where(v => !v.Contains('-')) // Exclude prerelease versions
            .LastOrDefault();

        if (latestVersionString is null || !Version.TryParse(latestVersionString, out var latestVersion))
        {
            return null;
        }

        return new UpdateInfo
        {
            LatestVersion = latestVersion,
            CurrentVersion = _currentVersion,
            IsPrerelease = false,
            ReleaseNotes = null,
            ReleaseUrl = $"https://www.nuget.org/packages/{NuGetPackageId}/{latestVersionString}",
            DownloadUrl = null // Not needed for dotnet tool update
        };
    }

    /// <inheritdoc />
    public async Task<UpdateResult> UpdateAsync(IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check for update
            progress?.Report(new UpdateProgress { Operation = "Checking for updates..." });
            var updateInfo = await CheckForUpdateAsync(cancellationToken);

            if (updateInfo is null)
            {
                return new UpdateResult
                {
                    Success = false,
                    Error = "Failed to check for updates. Please check your network connection."
                };
            }

            if (!updateInfo.IsUpdateAvailable)
            {
                return new UpdateResult
                {
                    Success = true,
                    UpdatedVersion = _currentVersion,
                    Error = "Already up to date."
                };
            }

            // Use different update strategy based on installation type
            if (_isDotnetTool)
            {
                return await UpdateViaDotnetToolAsync(updateInfo.LatestVersion, progress, cancellationToken);
            }

            return await UpdateViaGitHubReleaseAsync(updateInfo, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            return new UpdateResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<UpdateResult> UpdateViaDotnetToolAsync(
        Version latestVersion,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress { Operation = "Updating via dotnet tool...", PercentComplete = 10 });

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"tool update -g {NuGetPackageId}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new UpdateResult
            {
                Success = false,
                Error = "Failed to start dotnet tool update process."
            };
        }

        progress?.Report(new UpdateProgress { Operation = "Installing update...", PercentComplete = 50 });

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return new UpdateResult
            {
                Success = false,
                Error = $"dotnet tool update failed: {error}".Trim()
            };
        }

        progress?.Report(new UpdateProgress { Operation = "Update complete!", PercentComplete = 100 });

        return new UpdateResult
        {
            Success = true,
            UpdatedVersion = latestVersion,
            RestartRequired = true
        };
    }

    private async Task<UpdateResult> UpdateViaGitHubReleaseAsync(
        UpdateInfo updateInfo,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            return new UpdateResult
            {
                Success = false,
                Error = $"No download available for your platform ({GetRuntimeIdentifier()})."
            };
        }

        // Download update
        progress?.Report(new UpdateProgress { Operation = "Downloading update...", PercentComplete = 0 });
        var downloadPath = await DownloadUpdateAsync(updateInfo.DownloadUrl, progress, cancellationToken);

        // Extract update
        progress?.Report(new UpdateProgress { Operation = "Extracting update..." });
        var extractPath = await ExtractUpdateAsync(downloadPath, cancellationToken);

        // Install update
        progress?.Report(new UpdateProgress { Operation = "Installing update..." });
        var newExecutablePath = await InstallUpdateAsync(extractPath, cancellationToken);

        // Cleanup
        progress?.Report(new UpdateProgress { Operation = "Cleaning up..." });
        CleanupTempFiles(downloadPath, extractPath);

        progress?.Report(new UpdateProgress { Operation = "Update complete!", PercentComplete = 100 });

        return new UpdateResult
        {
            Success = true,
            UpdatedVersion = updateInfo.LatestVersion,
            RestartRequired = true,
            NewExecutablePath = newExecutablePath
        };
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", $"ironhive-cli/{_currentVersion}");
        request.Headers.Add("Accept", "application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Try to get releases list if "latest" endpoint fails (might be all prereleases)
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return await GetLatestReleaseFromListAsync(cancellationToken);
            }
            return null;
        }

        return await response.Content.ReadFromJsonAsync<GitHubRelease>(JsonOptions, cancellationToken);
    }

    private async Task<GitHubRelease?> GetLatestReleaseFromListAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases?per_page=10";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", $"ironhive-cli/{_currentVersion}");
        request.Headers.Add("Accept", "application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(JsonOptions, cancellationToken);
        return releases?.FirstOrDefault();
    }

    private static string? GetDownloadUrlForCurrentPlatform(GitHubRelease release)
    {
        var rid = GetRuntimeIdentifier();
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".zip" : ".tar.gz";

        // Find matching asset
        foreach (var asset in release.Assets)
        {
            if (asset.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return asset.BrowserDownloadUrl;
            }
        }

        return null;
    }

    private static string GetRuntimeIdentifier()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"win-{arch}";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return $"linux-{arch}";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"osx-{arch}";
        }

        return $"linux-{arch}";
    }

    private async Task<string> DownloadUpdateAsync(string url, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ironhive-update-{Guid.NewGuid()}{GetArchiveExtension()}");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var bytesDownloaded = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesDownloaded += bytesRead;

            if (totalBytes > 0)
            {
                var percent = (int)((bytesDownloaded * 100) / totalBytes);
                progress?.Report(new UpdateProgress
                {
                    Operation = "Downloading update...",
                    PercentComplete = percent,
                    BytesDownloaded = bytesDownloaded,
                    TotalBytes = totalBytes
                });
            }
        }

        return tempPath;
    }

    private static async Task<string> ExtractUpdateAsync(string archivePath, CancellationToken cancellationToken)
    {
        var extractPath = Path.Combine(Path.GetTempPath(), $"ironhive-extract-{Guid.NewGuid()}");
        Directory.CreateDirectory(extractPath);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractPath), cancellationToken);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archivePath, extractPath, cancellationToken);
        }

        return extractPath;
    }

    private static async Task ExtractTarGzAsync(string archivePath, string extractPath, CancellationToken cancellationToken)
    {
        // Use tar command on Unix systems
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{extractPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is not null)
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Failed to extract archive: {error}");
            }
        }
    }

    private static async Task<string> InstallUpdateAsync(string extractPath, CancellationToken cancellationToken)
    {
        // Find the executable in the extracted files
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ironhive.exe" : "ironhive";
        var newExecutable = Directory.GetFiles(extractPath, executableName, SearchOption.AllDirectories).FirstOrDefault();

        if (newExecutable is null)
        {
            throw new FileNotFoundException($"Could not find {executableName} in the update package.");
        }

        // Get current executable path
        var currentExecutable = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine current executable path.");

        // On Windows, we can't replace a running executable, so we rename it first
        var backupPath = currentExecutable + ".old";

        // Remove old backup if exists
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        // Rename current executable
        File.Move(currentExecutable, backupPath);

        try
        {
            // Copy new executable
            File.Copy(newExecutable, currentExecutable, overwrite: true);

            // Set execute permission on Unix
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await SetExecutePermissionAsync(currentExecutable, cancellationToken);
            }

            // Remove backup after successful copy
            await Task.Delay(100, cancellationToken); // Small delay to ensure file handles are released
            File.Delete(backupPath);
        }
        catch
        {
            // Restore backup on failure
            if (File.Exists(backupPath))
            {
                if (File.Exists(currentExecutable))
                {
                    File.Delete(currentExecutable);
                }
                File.Move(backupPath, currentExecutable);
            }
            throw;
        }

        return currentExecutable;
    }

    private static async Task SetExecutePermissionAsync(string path, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is not null)
        {
            await process.WaitForExitAsync(cancellationToken);
        }
    }

    private static void CleanupTempFiles(string downloadPath, string extractPath)
    {
        try
        {
            if (File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }
            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static string GetArchiveExtension()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".zip" : ".tar.gz";
    }

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(0, 1, 0);
    }

    private static Version? ParseVersion(string tagName)
    {
        // Remove 'v' prefix if present
        var versionString = tagName.TrimStart('v', 'V');

        // Handle prerelease suffixes (e.g., "0.1.0-alpha")
        var dashIndex = versionString.IndexOf('-');
        if (dashIndex > 0)
        {
            versionString = versionString[..dashIndex];
        }

        return Version.TryParse(versionString, out var version) ? version : null;
    }

    /// <summary>
    /// Detects if the CLI was installed as a dotnet global tool.
    /// </summary>
    private static bool DetectDotnetToolInstallation()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            // Normalize path separators for cross-platform comparison
            var normalizedPath = exePath.Replace('\\', '/').ToLowerInvariant();

            // dotnet global tools are installed in:
            // - Windows: %USERPROFILE%\.dotnet\tools\ironhive.exe
            // - Linux/macOS: ~/.dotnet/tools/ironhive
            return normalizedPath.Contains(".dotnet/tools");
        }
        catch
        {
            return false;
        }
    }

    // NuGet API models
    private sealed record NuGetVersionIndex
    {
        [JsonPropertyName("versions")]
        public List<string> Versions { get; init; } = [];
    }

    // GitHub API models
    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
