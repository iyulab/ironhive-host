using System.ComponentModel;
using IronHive.Agent.Providers;
using IronHive.Host.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Doctor command - diagnoses configuration and connectivity issues.
/// </summary>
public class DoctorCommand : AsyncCommand<DoctorCommand.Settings>
{
    private readonly IronHiveConfig _config;
    private readonly ConfigurationManager _configManager;
    private readonly IChatClientFactory _clientFactory;

    public DoctorCommand(IronHiveConfig config, ConfigurationManager configManager, IChatClientFactory clientFactory)
    {
        _config = config;
        _configManager = configManager;
        _clientFactory = clientFactory;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--fix")]
        [Description("Attempt to fix common issues automatically")]
        public bool Fix { get; init; }

        [CommandOption("--verbose")]
        [Description("Show detailed diagnostic information")]
        public bool Verbose { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var hasErrors = false;
        var hasWarnings = false;
        var recommendations = new List<string>();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]IronHive CLI Diagnostics[/]");
        AnsiConsole.WriteLine();

        // 1. Check settings file
        AnsiConsole.MarkupLine("[bold]Configuration[/]");
        hasErrors |= !CheckSettingsFile(recommendations, settings.Verbose);

        // 2. Check providers
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Providers[/]");
        var (providerErrors, providerWarnings) = CheckProviders(recommendations, settings.Verbose);
        hasErrors |= providerErrors;
        hasWarnings |= providerWarnings;

        // 3. Check connectivity
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Connectivity[/]");
        hasErrors |= !await CheckConnectivityAsync(recommendations, settings.Verbose, cancellationToken);

        // 4. Check environment
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Environment[/]");
        hasWarnings |= !CheckEnvironment(recommendations, settings.Verbose);

        // Summary
        AnsiConsole.WriteLine();
        PrintSummary(hasErrors, hasWarnings, recommendations);

        return hasErrors ? 1 : 0;
    }

    private bool CheckSettingsFile(List<string> recommendations, bool verbose)
    {
        var configPath = _configManager.GlobalConfigPath;
        var exists = File.Exists(configPath);

        if (exists)
        {
            PrintCheck(true, "Config file exists");
            if (verbose)
            {
                AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(configPath)}[/]");
            }
        }
        else
        {
            PrintCheck(false, "Config file not found", isWarning: true);
            recommendations.Add("Create settings with: ironhive set <key> <value>");
        }

        return true; // Not a fatal error
    }

    private (bool hasErrors, bool hasWarnings) CheckProviders(List<string> recommendations, bool verbose)
    {
        var hasErrors = false;
        var hasWarnings = false;
        var configuredCount = 0;

        // Check each provider
        var providers = ProviderBlocks.All
            .Select(p => (name: p.Name, configured: p.IsConfigured(_config), model: p.Model(_config)))
            .Append(("lmsupply", _config.LMSupply.Enabled, _config.LMSupply.GeneratorModel))
            .ToArray();

        foreach (var (name, configured, model) in providers)
        {
            if (configured)
            {
                configuredCount++;
                var modelInfo = !string.IsNullOrEmpty(model) ? $" ({model})" : "";
                PrintCheck(true, $"{name}{modelInfo}");
            }
            else if (verbose)
            {
                PrintCheck(false, $"{name} - not configured", isWarning: true);
            }
        }

        if (configuredCount == 0)
        {
            hasErrors = true;
            PrintCheck(false, "No provider configured");
            recommendations.Add("Configure a provider: ironhive set openai.apiKey <your-key>");
        }
        else if (configuredCount == 1 && _config.LMSupply.Enabled && !HasAnyRemoteProvider())
        {
            // Local-only is a supported configuration, not a problem to fix: reported as a passing check, with the
            // remote-provider suggestion kept as a recommendation.
            PrintCheck(true, "Using local inference only (lmsupply)");
            recommendations.Add("For better performance, configure a remote provider");
        }

        // Check for model specification
        if (HasAnyRemoteProvider() && !HasModelSpecified())
        {
            hasWarnings = true;
            PrintCheck(false, "Model not specified for configured provider", isWarning: true);
            recommendations.Add("Specify a model: ironhive set <provider>.model <model-name>");
        }

        return (hasErrors, hasWarnings);
    }

    private bool HasAnyRemoteProvider() => ProviderBlocks.Configured(_config).Any();

    private bool HasModelSpecified() => ProviderBlocks.Configured(_config).All(p => !string.IsNullOrEmpty(p.Model(_config)));

    private async Task<bool> CheckConnectivityAsync(List<string> recommendations, bool verbose, CancellationToken cancellationToken)
    {
        var allOk = true;
        var availableProviders = _clientFactory.AvailableProviders;

        if (availableProviders.Count == 0)
        {
            PrintCheck(false, "No providers available for connectivity test", isWarning: true);
            return true; // Not a fatal error if no providers
        }

        foreach (var providerName in availableProviders.Take(3)) // Test first 3
        {
            try
            {
                var models = await _clientFactory.GetAvailableModelsAsync(providerName, cancellationToken);
                // A provider whose model-list request fails falls back to its static catalog (or just the configured model),
                // so a list made only of Static entries means the endpoint did not answer — not that it is reachable.
                if (models.Count > 0 && models.All(m => m.Source == IronHive.Agent.Providers.ModelSource.Static))
                {
                    PrintCheck(false, $"{providerName} - model list unavailable (endpoint unreachable, or it serves no /models)", isWarning: true);
                    recommendations.Add($"Check {providerName} endpoint and API key");
                }
                else if (models.Count > 0)
                {
                    PrintCheck(true, $"{providerName} - reachable ({models.Count} models)");
                }
                else
                {
                    PrintCheck(true, $"{providerName} - reachable", isWarning: true);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                allOk = false;
                PrintCheck(false, $"{providerName} - unreachable");
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(ex.Message)}[/]");
                }
                recommendations.Add($"Check {providerName} endpoint and API key");
            }
        }

        return allOk;
    }

    private bool CheckEnvironment(List<string> recommendations, bool verbose)
    {
        var allOk = true;

        // Check Git
        var gitAvailable = IsCommandAvailable("git", "--version");
        if (gitAvailable)
        {
            PrintCheck(true, "Git available");
        }
        else
        {
            PrintCheck(false, "Git not found", isWarning: true);
            recommendations.Add("Install Git for version control features");
            allOk = false;
        }

        // Check working directory
        var isGitRepo = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), ".git"));
        if (isGitRepo)
        {
            PrintCheck(true, "Current directory is a Git repository");
        }
        else if (verbose)
        {
            PrintCheck(false, "Current directory is not a Git repository", isWarning: true);
        }

        // Check disk space (settings directory)
        try
        {
            var settingsDir = Path.GetDirectoryName(_configManager.GlobalConfigPath)!;
            if (Directory.Exists(settingsDir))
            {
                var drive = new DriveInfo(Path.GetPathRoot(settingsDir) ?? "C:");
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                if (freeGb < 1)
                {
                    PrintCheck(false, $"Low disk space: {freeGb:F1} GB free", isWarning: true);
                    allOk = false;
                }
                else if (verbose)
                {
                    PrintCheck(true, $"Disk space: {freeGb:F1} GB free");
                }
            }
        }
        catch
        {
            // Ignore disk check errors
        }

        return allOk;
    }

    private static bool IsCommandAvailable(string command, string args)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void PrintCheck(bool success, string message, bool isWarning = false)
    {
        if (success)
        {
            var icon = isWarning ? "[yellow]![/]" : "[green]✓[/]";
            var color = isWarning ? "yellow" : "white";
            AnsiConsole.MarkupLine($"  {icon} [{color}]{Markup.Escape(message)}[/]");
        }
        else
        {
            var icon = isWarning ? "[yellow]![/]" : "[red]✗[/]";
            var color = isWarning ? "yellow" : "red";
            AnsiConsole.MarkupLine($"  {icon} [{color}]{Markup.Escape(message)}[/]");
        }
    }

    private static void PrintSummary(bool hasErrors, bool hasWarnings, List<string> recommendations)
    {
        if (!hasErrors && !hasWarnings)
        {
            AnsiConsole.MarkupLine("[green]All checks passed![/]");
        }
        else if (hasErrors)
        {
            AnsiConsole.MarkupLine("[red]Some checks failed.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Some warnings found.[/]");
        }

        if (recommendations.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Recommendations:[/]");
            foreach (var rec in recommendations.Distinct())
            {
                AnsiConsole.MarkupLine($"  [grey]•[/] {Markup.Escape(rec)}");
            }
        }
    }
}
