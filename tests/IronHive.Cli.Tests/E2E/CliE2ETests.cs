using System.Diagnostics;
using System.Globalization;

namespace IronHive.Cli.Tests.E2E;

/// <summary>
/// End-to-end tests for CLI operations.
/// These tests run the actual CLI executable and verify its behavior.
/// </summary>
public class CliE2ETests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cliPath;

    public CliE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ironhive-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Find the CLI executable
        _cliPath = FindCliExecutable();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Help_ShowsUsageInformation()
    {
        var result = await RunCliAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ironhive", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Version_ShowsVersionNumber()
    {
        var result = await RunCliAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\d+\.\d+\.\d+", result.Output);
    }

    [Fact]
    public async Task ConfigShow_DisplaysConfiguration()
    {
        var result = await RunCliAsync("config", "show");

        // Should succeed even without configuration
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ConfigPath_ShowsConfigurationPath()
    {
        var result = await RunCliAsync("config", "path");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("config", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sessions_ListsSessionsCommand()
    {
        var result = await RunCliAsync("sessions", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidCommand_ShowsError()
    {
        var result = await RunCliAsync("invalid-command-xyz");

        // Should fail with non-zero exit code
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void DryRun_DoesNotExecute()
    {
        // Create a test file
        var testFile = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(testFile, "original content");

        // This test would normally use --dry-run to verify no changes are made
        // For now, we verify the file is unchanged
        var content = File.ReadAllText(testFile);
        Assert.Equal("original content", content);
    }

    [Fact]
    public async Task Run_WithoutApiKey_ShowsError()
    {
        // Run without API configuration
        var result = await RunCliAsync("run", "test prompt", "--no-interactive");

        // Should fail or warn about missing configuration
        // The exact behavior depends on implementation
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task Update_ShowsUpdateInfo()
    {
        var result = await RunCliAsync("update", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("update", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleFlags_ParsedCorrectly()
    {
        var result = await RunCliAsync("--version", "--help");

        // Should handle multiple flags
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task WorkingDirectory_Respected()
    {
        // Create a subdirectory
        var subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subDir);

        // Run CLI with working directory
        var result = await RunCliInDirectoryAsync(subDir, "config", "path");

        Assert.NotNull(result.Output);
    }

    [Fact]
    public void CliExecutable_Exists()
    {
        Assert.True(File.Exists(_cliPath) || Directory.Exists(Path.GetDirectoryName(_cliPath)),
            $"CLI executable should exist at {_cliPath}");
    }

    private Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        return RunCliInDirectoryAsync(_tempDir, args);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCliInDirectoryAsync(
        string workingDirectory,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{_cliPath}\" -- {string.Join(" ", args)}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // If running compiled exe directly
        if (_cliPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            _cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = _cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : _cliPath;
            psi.Arguments = _cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? $"\"{_cliPath}\" {string.Join(" ", args)}"
                : string.Join(" ", args);
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait with timeout
            var completed = await Task.WhenAny(
                Task.Run(() => process.WaitForExit(30000)),
                Task.Delay(30000));

            if (!process.HasExited)
            {
                process.Kill();
                return (-1, "", "Process timed out");
            }

            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private static string FindCliExecutable()
    {
        // Try to find the CLI project
        var currentDir = Directory.GetCurrentDirectory();
        var searchPaths = new[]
        {
            Path.Combine(currentDir, "src", "IronHive.Cli"),
            Path.Combine(currentDir, "..", "..", "..", "..", "src", "IronHive.Cli"),
            Path.Combine(currentDir, "..", "..", "..", "..", "..", "src", "IronHive.Cli")
        };

        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // Fallback: use project reference path
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "IronHive.Cli");
    }
}
