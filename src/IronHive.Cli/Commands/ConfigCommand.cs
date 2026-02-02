using System.ComponentModel;
using IronHive.Cli.Core.Config;
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

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
            .AddColumn("Section")
            .AddColumn("Key")
            .AddColumn("Value");

        // GpuStack configuration
        table.AddRow("GpuStack", "endpoint", _config.GpuStack.Endpoint ?? "[grey](not set)[/]");
        table.AddRow("GpuStack", "api_key", string.IsNullOrEmpty(_config.GpuStack.ApiKey) ? "[grey](not set)[/]" : "[green](set)[/]");
        table.AddRow("GpuStack", "model", _config.GpuStack.Model ?? "[grey](not set)[/]");
        table.AddRow("GpuStack", "embedding_model", _config.GpuStack.EmbeddingModel ?? "[grey](not set)[/]");
        table.AddRow("GpuStack", "rerank_model", _config.GpuStack.RerankModel ?? "[grey](not set)[/]");
        table.AddRow("GpuStack", "configured", _config.GpuStack.IsConfigured ? "[green]Yes[/]" : "[yellow]No[/]");

        // LMSupply configuration
        table.AddRow("LMSupply", "enabled", _config.LMSupply.Enabled ? "[green]Yes[/]" : "[grey]No[/]");
        table.AddRow("LMSupply", "embedder_model", _config.LMSupply.EmbedderModel);
        table.AddRow("LMSupply", "reranker_model", _config.LMSupply.RerankerModel);
        table.AddRow("LMSupply", "generator_model", _config.LMSupply.GeneratorModel);

        AnsiConsole.Write(table);

        // Show environment variable hints
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Environment Variables:[/]");
        AnsiConsole.MarkupLine("[grey]  GPUSTACK_ENDPOINT, GPUSTACK_API_KEY, GPUSTACK_MODEL[/]");
        AnsiConsole.MarkupLine("[grey]  LMSUPPLY_ENABLED, LMSUPPLY_EMBEDDER_MODEL, etc.[/]");

        return 0;
    }

    private static int ShowConfigPath()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive");

        var globalConfig = Path.Combine(configDir, "config.yaml");
        var envFile = Path.Combine(Environment.CurrentDirectory, ".env");

        AnsiConsole.MarkupLine("[bold]Configuration paths:[/]");
        AnsiConsole.MarkupLine($"  Global config:  [blue]{globalConfig}[/]");
        AnsiConsole.MarkupLine($"  Project .env:   [blue]{envFile}[/]");

        if (File.Exists(envFile))
        {
            AnsiConsole.MarkupLine($"  Status:         [green].env found[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  Status:         [yellow].env not found[/]");
        }

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
