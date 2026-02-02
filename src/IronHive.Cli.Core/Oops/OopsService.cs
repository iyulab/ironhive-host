using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace IronHive.Cli.Core.Oops;

/// <summary>
/// Implementation of oops file versioning integration.
/// Uses global storage (--global) to keep project directories clean.
/// </summary>
public class OopsService : IOopsService
{
    private const string OopsCommand = "oops";
    private const string GitHubRepo = "iyulab/oops";
    private const int AutoActivateThreshold = 2;

    private readonly ConcurrentDictionary<string, int> _editCounts = new();
    private readonly ConcurrentDictionary<string, bool> _gitRepoCache = new();
    private readonly HttpClient _httpClient;

    private bool? _isInstalled;
    private string? _version;
    private string? _oopsPath;

    public OopsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public bool IsInstalled => _isInstalled ??= CheckInstalled();

    /// <inheritdoc />
    public string? Version => IsInstalled ? (_version ??= GetVersion()) : null;

    /// <inheritdoc />
    public async Task<bool> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            return true;
        }

        try
        {
            await DownloadAndInstallAsync(cancellationToken);
            _isInstalled = CheckInstalled();
            return _isInstalled.Value;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsTracked(string filePath)
    {
        if (!IsInstalled)
        {
            return false;
        }

        var result = RunOops($"-g now \"{filePath}\"");
        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public Task<OopsResult> StartAsync(string filePath)
    {
        return Task.FromResult(RunOops($"-g start \"{filePath}\""));
    }

    /// <inheritdoc />
    public Task<OopsResult> SaveAsync(string filePath, string? message = null)
    {
        var args = string.IsNullOrEmpty(message)
            ? $"-g save \"{filePath}\""
            : $"-g save \"{filePath}\" \"{EscapeMessage(message)}\"";

        return Task.FromResult(RunOops(args));
    }

    /// <inheritdoc />
    public Task<OopsResult> UndoAsync(string filePath)
    {
        return Task.FromResult(RunOops($"-g oops! \"{filePath}\""));
    }

    /// <inheritdoc />
    public Task<OopsResult> BackAsync(string filePath, int snapshotNumber)
    {
        return Task.FromResult(RunOops($"-g back {snapshotNumber} \"{filePath}\""));
    }

    /// <inheritdoc />
    public Task<OopsResult> HistoryAsync(string filePath)
    {
        return Task.FromResult(RunOops($"-g history \"{filePath}\""));
    }

    /// <inheritdoc />
    public Task<OopsResult> ChangesAsync(string filePath, int? snapshotA = null, int? snapshotB = null)
    {
        string args;
        if (snapshotA.HasValue && snapshotB.HasValue)
        {
            args = $"-g changes {snapshotA} {snapshotB} \"{filePath}\"";
        }
        else if (snapshotA.HasValue)
        {
            args = $"-g changes {snapshotA} \"{filePath}\"";
        }
        else
        {
            args = $"-g changes \"{filePath}\"";
        }

        return Task.FromResult(RunOops(args));
    }

    /// <inheritdoc />
    public Task<OopsResult> StatusAsync(string filePath)
    {
        return Task.FromResult(RunOops($"-g now \"{filePath}\""));
    }

    /// <inheritdoc />
    public Task<OopsResult> StopAsync(string filePath)
    {
        return Task.FromResult(RunOops($"-g done \"{filePath}\""));
    }

    /// <inheritdoc />
    public Task<OopsResult> CleanupAsync(bool dryRun = false)
    {
        var args = dryRun ? "gc -g --dry-run" : "gc -g -y";
        return Task.FromResult(RunOops(args));
    }

    /// <inheritdoc />
    public bool ShouldAutoActivate(string filePath)
    {
        // Don't auto-activate if oops isn't installed
        if (!IsInstalled)
        {
            return false;
        }

        // Don't auto-activate if file is in a Git repository
        if (IsInGitRepository(filePath))
        {
            return false;
        }

        // Auto-activate if file has been edited multiple times
        var absPath = Path.GetFullPath(filePath);
        return _editCounts.TryGetValue(absPath, out var count) && count >= AutoActivateThreshold;
    }

    /// <inheritdoc />
    public void RecordEdit(string filePath)
    {
        var absPath = Path.GetFullPath(filePath);
        _editCounts.AddOrUpdate(absPath, 1, (_, count) => count + 1);
    }

    private bool CheckInstalled()
    {
        try
        {
            var result = RunOopsInternal("--version", skipCheck: true);
            if (result.ExitCode == 0)
            {
                _oopsPath = FindOopsPath();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetVersion()
    {
        var result = RunOopsInternal("--version", skipCheck: true);
        if (result.Success)
        {
            // Output format: "oops version 0.3.0" or just "0.3.0"
            var output = result.Output.Trim();
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[^1] : output;
        }
        return null;
    }

    private static string? FindOopsPath()
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = OopsCommand,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 ? output.Split('\n')[0].Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private OopsResult RunOops(string arguments)
    {
        if (!IsInstalled)
        {
            return new OopsResult
            {
                Success = false,
                Output = string.Empty,
                Error = "oops is not installed. Run EnsureInstalledAsync() first.",
                ExitCode = -1
            };
        }

        return RunOopsInternal(arguments, skipCheck: false);
    }

    private static OopsResult RunOopsInternal(string arguments, bool skipCheck)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OopsCommand,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new OopsResult
                {
                    Success = false,
                    Output = string.Empty,
                    Error = "Failed to start oops process.",
                    ExitCode = -1
                };
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new OopsResult
            {
                Success = process.ExitCode == 0,
                Output = output.Trim(),
                Error = string.IsNullOrEmpty(error) ? null : error.Trim(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new OopsResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    private bool IsInGitRepository(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        // Check cache first
        if (_gitRepoCache.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        // Walk up directory tree looking for .git
        var current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            var gitDir = Path.Combine(current, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir))
            {
                _gitRepoCache[directory] = true;
                return true;
            }

            var parent = Directory.GetParent(current);
            if (parent is null || parent.FullName == current)
            {
                break;
            }
            current = parent.FullName;
        }

        _gitRepoCache[directory] = false;
        return false;
    }

    private async Task DownloadAndInstallAsync(CancellationToken cancellationToken)
    {
        // Get latest release info
        var releaseUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, releaseUrl);
        request.Headers.Add("User-Agent", "ironhive-cli");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var release = JsonSerializer.Deserialize<GitHubRelease>(json);

        if (release?.Assets is null || release.Assets.Count == 0)
        {
            throw new InvalidOperationException("No release assets found.");
        }

        // Find appropriate asset for current platform
        var assetName = GetAssetNameForPlatform();
        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.Contains(assetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            throw new InvalidOperationException($"No asset found for platform: {assetName}");
        }

        // Download asset
        var downloadUrl = asset.BrowserDownloadUrl;
        var tempPath = Path.Combine(Path.GetTempPath(), asset.Name);

        using (var downloadResponse = await _httpClient.GetAsync(downloadUrl, cancellationToken))
        {
            downloadResponse.EnsureSuccessStatusCode();
            await using var fs = new FileStream(tempPath, FileMode.Create);
            await downloadResponse.Content.CopyToAsync(fs, cancellationToken);
        }

        // Extract and install
        var installDir = GetInstallDirectory();
        Directory.CreateDirectory(installDir);

        if (tempPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(tempPath, installDir, overwriteFiles: true);
        }
        else if (tempPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(tempPath, installDir, cancellationToken);
        }

        // Set execute permission on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var oopsPath = Path.Combine(installDir, "oops");
            if (File.Exists(oopsPath))
            {
                await SetExecutePermissionAsync(oopsPath, cancellationToken);
            }
        }

        // Cleanup
        try { File.Delete(tempPath); } catch { /* ignore */ }

        // Add to PATH hint
        if (!IsInPath(installDir))
        {
            Console.WriteLine($"oops installed to: {installDir}");
            Console.WriteLine($"Add to PATH: {installDir}");
        }
    }

    private static string GetAssetNameForPlatform()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"windows-{arch}";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return $"linux-{arch}";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"darwin-{arch}";
        }

        return $"linux-{arch}";
    }

    private static string GetInstallDirectory()
    {
        // Install to user's local bin directory
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Programs", "oops");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "bin");
    }

    private static bool IsInPath(string directory)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathVar.Split(Path.PathSeparator);
        return paths.Any(p => p.Equals(directory, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ExtractTarGzAsync(string archivePath, string extractPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{extractPath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is not null)
        {
            await process.WaitForExitAsync(cancellationToken);
        }
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

    private static string EscapeMessage(string message)
    {
        return message.Replace("\"", "\\\"");
    }

    // GitHub API models
    private sealed class GitHubRelease
    {
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string Name { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
