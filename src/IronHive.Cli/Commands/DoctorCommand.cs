using System.ComponentModel;
using IronHive.Agent.Providers;
using IronHive.Host.Core.Config;
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
        var providers = new (string name, bool configured, string? model, string? hint)[]
        {
            ("gpustack", _config.GpuStack.IsConfigured, _config.GpuStack.Model, "set gpustack.endpoint, gpustack.apiKey, gpustack.model"),
            ("openai", _config.OpenAI.IsConfigured, _config.OpenAI.Model, "set openai.apiKey, openai.model"),
            ("anthropic", _config.Anthropic.IsConfigured, _config.Anthropic.Model, "set anthropic.apiKey, anthropic.model"),
            ("google", _config.GoogleAI.IsConfigured, _config.GoogleAI.Model, "set google.apiKey, google.model"),
            ("xai", _config.Xai.IsConfigured, _config.Xai.Model, "set xai.apiKey, xai.model"),
            ("azure", _config.AzureOpenAI.IsConfigured, _config.AzureOpenAI.DeploymentName, "set azure.endpoint, azure.apiKey, azure.deploymentName"),
            ("ollama", _config.Ollama.IsConfigured, _config.Ollama.Model, "set ollama.enabled true, ollama.model"),
            ("lmstudio", _config.LMStudio.IsConfigured, _config.LMStudio.Model, "set lmstudio.enabled true, lmstudio.model"),
            ("lmsupply", _config.LMSupply.Enabled, _config.LMSupply.GeneratorModel, null)
        };

        foreach (var (name, configured, model, hint) in providers)
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
            hasWarnings = true;
            PrintCheck(true, "Using local inference only (lmsupply)", isWarning: true);
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

    private bool HasAnyRemoteProvider()
    {
        return _config.GpuStack.IsConfigured ||
               _config.OpenAI.IsConfigured ||
               _config.Anthropic.IsConfigured ||
               _config.GoogleAI.IsConfigured ||
               _config.Xai.IsConfigured ||
               _config.AzureOpenAI.IsConfigured ||
               _config.Ollama.IsConfigured ||
               _config.LMStudio.IsConfigured;
    }

    private bool HasModelSpecified()
    {
        if (_config.GpuStack.IsConfigured && string.IsNullOrEmpty(_config.GpuStack.Model))
        {
            return false;
        }

        if (_config.OpenAI.IsConfigured && string.IsNullOrEmpty(_config.OpenAI.Model))
        {
            return false;
        }

        if (_config.Anthropic.IsConfigured && string.IsNullOrEmpty(_config.Anthropic.Model))
        {
            return false;
        }

        if (_config.GoogleAI.IsConfigured && string.IsNullOrEmpty(_config.GoogleAI.Model))
        {
            return false;
        }

        if (_config.Xai.IsConfigured && string.IsNullOrEmpty(_config.Xai.Model))
        {
            return false;
        }

        if (_config.AzureOpenAI.IsConfigured && string.IsNullOrEmpty(_config.AzureOpenAI.DeploymentName))
        {
            return false;
        }

        if (_config.Ollama.IsConfigured && string.IsNullOrEmpty(_config.Ollama.Model))
        {
            return false;
        }

        if (_config.LMStudio.IsConfigured && string.IsNullOrEmpty(_config.LMStudio.Model))
        {
            return false;
        }

        return true;
    }

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
                if (models.Count > 0)
                {
                    PrintCheck(true, $"{providerName} - reachable ({models.Count} models)");
                }
                else
                {
                    PrintCheck(true, $"{providerName} - reachable", isWarning: true);
                }
            }
            catch (Exception ex)
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
