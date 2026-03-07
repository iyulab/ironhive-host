using System.ComponentModel;
using IronHive.Cli.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Get command - gets a configuration value.
/// Usage: ironhive get openai.apiKey
/// </summary>
public class GetCommand : Command<GetCommand.Settings>
{
    private readonly SettingsManager _settings;

    public GetCommand(SettingsManager settings)
    {
        _settings = settings;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[KEY]")]
        [Description("Configuration key (e.g., openai.apiKey). If omitted, shows all settings.")]
        public string? Key { get; init; }

        [CommandOption("--raw")]
        [Description("Output raw value without formatting (for scripts)")]
        public bool Raw { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // If no key, show all settings
        if (string.IsNullOrWhiteSpace(settings.Key))
        {
            return ShowAllSettings(settings.Raw);
        }

        var value = _settings.GetValue(settings.Key);

        if (value is null)
        {
            if (settings.Raw)
            {
                // Silent exit for scripts
                return 1;
            }

            AnsiConsole.MarkupLine($"[yellow]Key not found:[/] {Markup.Escape(settings.Key)}");
            return 1;
        }

        if (settings.Raw)
        {
            Console.WriteLine(value);
        }
        else
        {
            var displayValue = IsSecretKey(settings.Key)
                ? MaskValue(value)
                : value;

            AnsiConsole.MarkupLine($"[blue]{Markup.Escape(settings.Key)}[/] = [cyan]{Markup.Escape(displayValue)}[/]");
        }

        return 0;
    }

    private int ShowAllSettings(bool raw)
    {
        var allSettings = _settings.ListAll();

        if (allSettings.Count == 0)
        {
            if (!raw)
            {
                AnsiConsole.MarkupLine("[yellow]No settings configured.[/]");
                AnsiConsole.MarkupLine($"[grey]Settings file: {Markup.Escape(_settings.SettingsFilePath)}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]Use 'ironhive set <key> <value>' to configure.[/]");
            }
            return 0;
        }

        if (raw)
        {
            foreach (var (key, value) in allSettings.OrderBy(s => s.Key))
            {
                Console.WriteLine($"{key}={value}");
            }
        }
        else
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Key")
                .AddColumn("Value");

            foreach (var (key, value) in allSettings.OrderBy(s => s.Key))
            {
                table.AddRow(
                    Markup.Escape(key),
                    Markup.Escape(value)
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]Settings file: {Markup.Escape(_settings.SettingsFilePath)}[/]");
        }

        return 0;
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
}
