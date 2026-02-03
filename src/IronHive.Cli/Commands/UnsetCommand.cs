using System.ComponentModel;
using IronHive.Cli.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Unset command - removes a configuration value.
/// Usage: ironhive unset openai.apiKey
/// </summary>
public class UnsetCommand : Command<UnsetCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("Configuration key to remove (e.g., openai.apiKey)")]
        public string Key { get; init; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Key))
        {
            AnsiConsole.MarkupLine("[red]Error: Key is required.[/]");
            return 1;
        }

        try
        {
            var removed = SettingsManager.UnsetValue(settings.Key);

            if (removed)
            {
                AnsiConsole.MarkupLine($"[green]Removed[/] [blue]{Markup.Escape(settings.Key)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Key not found:[/] {Markup.Escape(settings.Key)}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
