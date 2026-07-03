using System.ComponentModel;
using IronHive.Host.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Host.Commands;

/// <summary>
/// Config command - manages configuration.
/// </summary>
public class ConfigCommand : Command<ConfigCommand.Settings>
{
    private readonly IronHiveConfig _config;
    private readonly ConfigurationManager _configManager;

    public ConfigCommand(IronHiveConfig config, ConfigurationManager configManager)
    {
        _config = config;
        _configManager = configManager;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[ACTION]")]
        [Description("Action to perform (show, path)")]
        public string? Action { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
        AddConfigRow(table, "gpuStack", "endpoint", _config.GpuStack.Endpoint);
        AddSecretRow(table, "gpuStack", "apiKey", _config.GpuStack.ApiKey);
        AddConfigRow(table, "gpuStack", "model", _config.GpuStack.Model);
        AddConfigRow(table, "gpuStack", "embeddingModel", _config.GpuStack.EmbeddingModel);
        AddConfigRow(table, "gpuStack", "rerankModel", _config.GpuStack.RerankModel);
        AddStatusRow(table, "gpuStack", "configured", _config.GpuStack.IsConfigured);

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
        AddSecretRow(table, "googleai", "apiKey", _config.GoogleAI.ApiKey);
        AddConfigRow(table, "googleai", "model", _config.GoogleAI.Model);
        AddStatusRow(table, "googleai", "configured", _config.GoogleAI.IsConfigured);

        // Xai configuration
        AddConfigRow(table, "xai", "endpoint", _config.Xai.Endpoint);
        AddSecretRow(table, "xai", "apiKey", _config.Xai.ApiKey);
        AddConfigRow(table, "xai", "model", _config.Xai.Model);
        AddStatusRow(table, "xai", "configured", _config.Xai.IsConfigured);

        // Azure OpenAI configuration
        AddConfigRow(table, "azureopenai", "endpoint", _config.AzureOpenAI.Endpoint);
        AddSecretRow(table, "azureopenai", "apiKey", _config.AzureOpenAI.ApiKey);
        AddConfigRow(table, "azureopenai", "deploymentName", _config.AzureOpenAI.DeploymentName);
        AddStatusRow(table, "azureopenai", "configured", _config.AzureOpenAI.IsConfigured);

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
        AnsiConsole.MarkupLine($"[grey]Config file: {Markup.Escape(_configManager.GlobalConfigPath)}[/]");

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
        AnsiConsole.MarkupLine($"  Config file:   [blue]{Markup.Escape(_configManager.GlobalConfigPath)}[/]");
        AnsiConsole.MarkupLine($"  Directory:     [blue]{Markup.Escape(Path.GetDirectoryName(_configManager.GlobalConfigPath)!)}[/]");

        if (File.Exists(_configManager.GlobalConfigPath))
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
