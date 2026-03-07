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
    private readonly SettingsManager _settings;

    public ConfigCommand(IronHiveConfig config, SettingsManager settings)
    {
        _config = config;
        _settings = settings;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[ACTION]")]
        [Description("Action to perform (show, path)")]
        public string? Action { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var action = settings.Action?.ToLowerInvariant() ?? "show";

        return action switch
        {
            "show" => ShowConfig(),
            "path" => ShowConfigPath(),
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
        AddConfigRow(table, "gpustack", "endpoint", _config.GpuStack.Endpoint);
        AddSecretRow(table, "gpustack", "apiKey", _config.GpuStack.ApiKey);
        AddConfigRow(table, "gpustack", "model", _config.GpuStack.Model);
        AddConfigRow(table, "gpustack", "embeddingModel", _config.GpuStack.EmbeddingModel);
        AddConfigRow(table, "gpustack", "rerankModel", _config.GpuStack.RerankModel);
        AddStatusRow(table, "gpustack", "configured", _config.GpuStack.IsConfigured);

        // OpenAI configuration
        AddSecretRow(table, "openai", "apiKey", _config.OpenAI.ApiKey);
        AddConfigRow(table, "openai", "model", _config.OpenAI.Model);
        AddConfigRow(table, "openai", "endpoint", _config.OpenAI.Endpoint);
        AddStatusRow(table, "openai", "configured", _config.OpenAI.IsConfigured);

        // Anthropic configuration
        AddSecretRow(table, "anthropic", "apiKey", _config.Anthropic.ApiKey);
        AddConfigRow(table, "anthropic", "model", _config.Anthropic.Model);
        AddStatusRow(table, "anthropic", "configured", _config.Anthropic.IsConfigured);

        // Google AI configuration
        AddSecretRow(table, "google", "apiKey", _config.GoogleAI.ApiKey);
        AddConfigRow(table, "google", "model", _config.GoogleAI.Model);
        AddStatusRow(table, "google", "configured", _config.GoogleAI.IsConfigured);

        // Xai configuration
        AddConfigRow(table, "xai", "endpoint", _config.Xai.Endpoint);
        AddSecretRow(table, "xai", "apiKey", _config.Xai.ApiKey);
        AddConfigRow(table, "xai", "model", _config.Xai.Model);
        AddStatusRow(table, "xai", "configured", _config.Xai.IsConfigured);

        // Azure OpenAI configuration
        AddConfigRow(table, "azure", "endpoint", _config.AzureOpenAI.Endpoint);
        AddSecretRow(table, "azure", "apiKey", _config.AzureOpenAI.ApiKey);
        AddConfigRow(table, "azure", "deploymentName", _config.AzureOpenAI.DeploymentName);
        AddStatusRow(table, "azure", "configured", _config.AzureOpenAI.IsConfigured);

        // Ollama configuration
        AddBoolRow(table, "ollama", "enabled", _config.Ollama.Enabled);
        AddConfigRow(table, "ollama", "endpoint", _config.Ollama.Endpoint);
        AddConfigRow(table, "ollama", "model", _config.Ollama.Model);

        // LMStudio configuration
        AddBoolRow(table, "lmstudio", "enabled", _config.LMStudio.Enabled);
        AddConfigRow(table, "lmstudio", "endpoint", _config.LMStudio.Endpoint);
        AddConfigRow(table, "lmstudio", "model", _config.LMStudio.Model);

        // LMSupply configuration
        AddBoolRow(table, "lmsupply", "enabled", _config.LMSupply.Enabled);
        AddConfigRow(table, "lmsupply", "embedderModel", _config.LMSupply.EmbedderModel);
        AddConfigRow(table, "lmsupply", "rerankerModel", _config.LMSupply.RerankerModel);
        AddConfigRow(table, "lmsupply", "generatorModel", _config.LMSupply.GeneratorModel);
        table.AddRow("lmsupply", "maxContextLength",
            _config.LMSupply.MaxContextLength.HasValue
                ? $"{_config.LMSupply.MaxContextLength.Value:N0}"
                : "[cyan](auto)[/]");

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Settings file: {Markup.Escape(_settings.SettingsFilePath)}[/]");

        return 0;
    }

    private static void AddConfigRow(Table table, string section, string key, string? value)
    {
        table.AddRow(section, key, value ?? "[grey](not set)[/]");
    }

    private static void AddSecretRow(Table table, string section, string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            table.AddRow(section, key, "[grey](not set)[/]");
        }
        else
        {
            table.AddRow(section, key, "[green](set)[/]");
        }
    }

    private static void AddStatusRow(Table table, string section, string key, bool value)
    {
        table.AddRow(section, key, value ? "[green]Yes[/]" : "[yellow]No[/]");
    }

    private static void AddBoolRow(Table table, string section, string key, bool value)
    {
        table.AddRow(section, key, value ? "[green]true[/]" : "[grey]false[/]");
    }

    private int ShowConfigPath()
    {
        AnsiConsole.MarkupLine("[bold]Configuration:[/]");
        AnsiConsole.MarkupLine($"  Settings file: [blue]{Markup.Escape(_settings.SettingsFilePath)}[/]");
        AnsiConsole.MarkupLine($"  Directory:     [blue]{Markup.Escape(_settings.SettingsDirectory)}[/]");

        if (File.Exists(_settings.SettingsFilePath))
        {
            AnsiConsole.MarkupLine("  Status:        [green]exists[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  Status:        [yellow]not created[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Use 'ironhive set <key> <value>' to create settings.[/]");
        }

        return 0;
    }

    private static int ShowHelp(string action)
    {
        AnsiConsole.MarkupLine($"[red]Unknown action: {Markup.Escape(action)}[/]");
        AnsiConsole.MarkupLine("[grey]Available actions: show, path[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]To set values, use: ironhive set <key> <value>[/]");
        AnsiConsole.MarkupLine("[grey]To get values, use: ironhive get <key>[/]");
        return 1;
    }
}
