using System.ComponentModel;
using System.Globalization;
using IronHive.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Config command - manages configuration.
/// </summary>
public class ConfigCommand : Command<ConfigCommand.Settings>
{
    private readonly IronHiveConfig _config;

    public ConfigCommand(IronHiveConfig config)
    {
        _config = config;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[ACTION]")]
        [Description("Action to perform (show, set, path)")]
        public string? Action { get; init; }

        [CommandArgument(1, "[KEY]")]
        [Description("Configuration key")]
        public string? Key { get; init; }

        [CommandArgument(2, "[VALUE]")]
        [Description("Configuration value")]
        public string? Value { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var action = settings.Action?.ToLowerInvariant() ?? "show";

        return action switch
        {
            "show" => ShowConfig(),
            "path" => ShowConfigPath(),
            "set" => SetConfig(settings.Key, settings.Value),
            _ => ShowHelp(action)
        };
    }

    private int ShowConfig()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Key")
            .AddColumn("Value");

        table.AddRow("provider", _config.Provider);
        table.AddRow("model", _config.Model);
        table.AddRow("api_key", string.IsNullOrEmpty(_config.ApiKey) ? "[grey](not set)[/]" : "[green](set)[/]");
        table.AddRow("base_url", _config.BaseUrl ?? "[grey](default)[/]");
        table.AddRow("temperature", _config.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "[grey](default)[/]");
        table.AddRow("max_tokens", _config.MaxTokens?.ToString(CultureInfo.InvariantCulture) ?? "[grey](default)[/]");

        AnsiConsole.Write(table);
        return 0;
    }

    private static int ShowConfigPath()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive");

        var globalConfig = Path.Combine(configDir, "config.yaml");
        var projectConfig = Path.Combine(Environment.CurrentDirectory, ".ironhive", "config.yaml");

        AnsiConsole.MarkupLine("[bold]Configuration paths:[/]");
        AnsiConsole.MarkupLine($"  Global:  [blue]{globalConfig}[/]");
        AnsiConsole.MarkupLine($"  Project: [blue]{projectConfig}[/]");

        return 0;
    }

    private static int SetConfig(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            AnsiConsole.MarkupLine("[red]Error: Key is required.[/]");
            return 1;
        }

        // TODO: Implement config file writing
        AnsiConsole.MarkupLine("[yellow]Config file writing not yet implemented.[/]");
        AnsiConsole.MarkupLine($"Would set: [blue]{key}[/] = [green]{value ?? "(empty)"}[/]");

        return 0;
    }

    private static int ShowHelp(string action)
    {
        AnsiConsole.MarkupLine($"[red]Unknown action: {Markup.Escape(action)}[/]");
        AnsiConsole.MarkupLine("[grey]Available actions: show, set, path[/]");
        return 1;
    }
}
