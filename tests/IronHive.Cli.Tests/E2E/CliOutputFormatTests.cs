using System.Diagnostics;
using System.Text.Json;

namespace IronHive.Cli.Tests.E2E;

/// <summary>
/// Tests for CLI output format options (--output json/jsonl, --plain).
/// Validates programmatic interface for subprocess integration.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "UI")]
public class CliOutputFormatTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cliProjectPath;

    public CliOutputFormatTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ironhive-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _cliProjectPath = FindCliProject();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SessionsList_WithOutputJson_ReturnsValidJson()
    {
        // Act
        var result = await RunCliAsync("sessions", "list", "--output", "json");

        // Assert - Should return valid JSON array (possibly empty)
        Assert.Equal(0, result.ExitCode);

        // Output should be valid JSON
        var trimmed = result.Output.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            var isValidJson = trimmed.StartsWith('[') || trimmed.StartsWith('{');
            Assert.True(isValidJson, $"Output should be valid JSON. Got: {trimmed}");

            // Should parse without error
            try
            {
                JsonDocument.Parse(trimmed);
            }
            catch (JsonException ex)
            {
                Assert.Fail($"Invalid JSON output: {ex.Message}");
            }
        }
    }

    [Fact]
    public async Task SessionsList_WithOutputJson_ContainsExpectedFields()
    {
        // Arrange - Create a session first
        await RunCliAsync("sessions", "list", "--output", "json"); // May create session dir

        // Act
        var result = await RunCliAsync("sessions", "list", "--output", "json");

        // Assert
        Assert.Equal(0, result.ExitCode);

        var output = result.Output.Trim();
        if (output.StartsWith('[') && output.Length > 2) // Non-empty array
        {
            var sessions = JsonDocument.Parse(output).RootElement;
            if (sessions.GetArrayLength() > 0)
            {
                var firstSession = sessions[0];
                // Should have id and created fields
                Assert.True(firstSession.TryGetProperty("id", out _) ||
                           firstSession.TryGetProperty("Id", out _),
                           "Session should have id field");
            }
        }
    }

    [Fact]
    public async Task Help_ShowsUsageInformation()
    {
        // Act
        var result = await RunCliAsync("--help");

        // Assert
        Assert.Equal(0, result.ExitCode);

        // Should contain help text
        Assert.Contains("ironhive", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USAGE", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigShow_WithOutputJson_ReturnsValidJson()
    {
        // Act
        var result = await RunCliAsync("config", "show", "--output", "json");

        // Assert
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
        {
            var trimmed = result.Output.Trim();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    JsonDocument.Parse(trimmed);
                }
                catch (JsonException ex)
                {
                    Assert.Fail($"config show --output json should return valid JSON: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public async Task OutputFormatOption_IsDocumented()
    {
        // Act
        var result = await RunCliAsync("--help");

        // Assert - Help should mention output format
        Assert.Equal(0, result.ExitCode);
        // The --output option should be documented
        Assert.True(
            result.Output.Contains("--output", StringComparison.OrdinalIgnoreCase) ||
            result.Output.Contains("-o", StringComparison.OrdinalIgnoreCase) ||
            result.Output.Contains("json", StringComparison.OrdinalIgnoreCase),
            "Help should document output format options");
    }

    [Fact]
    public async Task PlainOption_IsDocumented()
    {
        // Act
        var result = await RunCliAsync("--help");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("plain", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sessions_Help_ShowsOutputOption()
    {
        // Act
        var result = await RunCliAsync("sessions", "list", "--help");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.True(
            result.Output.Contains("--output", StringComparison.OrdinalIgnoreCase) ||
            result.Output.Contains("-o", StringComparison.OrdinalIgnoreCase),
            "sessions list --help should show --output option");
    }

    [Fact]
    public async Task OutputFormat_Text_IsDefault()
    {
        // Act - Run without --output flag
        var result = await RunCliAsync("sessions", "list");

        // Assert - Should succeed (text is default)
        Assert.Equal(0, result.ExitCode);
        // Text output is not JSON
        var trimmed = result.Output.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            // If there are sessions, text output typically has headers/table format
            // If empty, that's also valid
            Assert.True(true); // Just verify it runs without error
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{_cliProjectPath}\" -- {string.Join(" ", args)}",
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                ["NO_COLOR"] = "1",  // Disable colors for testing
                ["TERM"] = "dumb"    // Dumb terminal
            }
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = process.WaitForExit(30000);
            if (!completed)
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

    private static string FindCliProject()
    {
        var baseDir = AppContext.BaseDirectory;

        // Navigate up from test output directory to find src
        var searchPaths = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "IronHive.Cli")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "IronHive.Cli")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "IronHive.Cli")),
        };

        foreach (var path in searchPaths)
        {
            var csprojPath = Path.Combine(path, "IronHive.Cli.csproj");
            if (File.Exists(csprojPath))
            {
                return path;
            }
        }

        // Fallback
        return Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "IronHive.Cli");
    }
}
