using System.ComponentModel;
using IronHive.Cli.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Set command - sets a configuration value.
/// Usage: ironhive set openai.apiKey sk-xxx
/// </summary>
public class SetCommand : Command<SetCommand.Settings>
{
    private readonly SettingsManager _settings;

    public SetCommand(SettingsManager settings)
    {
        _settings = settings;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("Configuration key (e.g., openai.apiKey, anthropic.model)")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<VALUE>")]
        [Description("Value to set")]
        public string Value { get; init; } = string.Empty;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Key))
        {
            AnsiConsole.MarkupLine("[red]Error: Key is required.[/]");
            ShowValidKeys();
            return 1;
        }

        try
        {
            _settings.SetValue(settings.Key, settings.Value);

            var displayValue = IsSecretKey(settings.Key)
                ? MaskValue(settings.Value)
                : settings.Value;

            AnsiConsole.MarkupLine($"[green]Set[/] [blue]{Markup.Escape(settings.Key)}[/] = [cyan]{Markup.Escape(displayValue)}[/]");
            AnsiConsole.MarkupLine($"[grey]Saved to: {Markup.Escape(_settings.SettingsFilePath)}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static bool IsSecretKey(string key)
    {
        return key.Contains("apiKey", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 8)
        {
            return "***";
        }

        return value[..4] + "..." + value[^4..];
    }

    private static void ShowValidKeys()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Valid configuration keys:[/]");
        AnsiConsole.WriteLine();

        var keys = new Dictionary<string, string[]>
        {
            ["openai"] = ["apiKey", "model", "endpoint"],
            ["anthropic"] = ["apiKey", "model"],
            ["google"] = ["apiKey", "model"],
            ["xai"] = ["apiKey", "model", "endpoint"],
            ["azure"] = ["endpoint", "apiKey", "deploymentName"],
            ["gpustack"] = ["endpoint", "apiKey", "model", "embeddingModel", "rerankModel"],
            ["ollama"] = ["enabled", "endpoint", "model"],
            ["lmstudio"] = ["enabled", "endpoint", "model"],
            ["lmsupply"] = ["enabled", "embedderModel", "rerankerModel", "generatorModel", "maxContextLength"]
        };

        foreach (var (section, props) in keys)
        {
            AnsiConsole.MarkupLine($"  [yellow]{section}[/]");
            foreach (var prop in props)
            {
                AnsiConsole.MarkupLine($"    [grey]{section}.{prop}[/]");
            }
        }
    }
}
