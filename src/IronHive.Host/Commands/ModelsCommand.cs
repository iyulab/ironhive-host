using System.ComponentModel;
using System.Text.Json;
using IronHive.Agent.Providers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Host.Commands;

/// <summary>
/// Models command - lists available models from configured providers.
/// </summary>
public class ModelsCommand : AsyncCommand<ModelsCommand.Settings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IChatClientFactory _clientFactory;

    public ModelsCommand(IChatClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-p|--provider <PROVIDER>")]
        [Description("Filter by provider (e.g., openai, anthropic, gpustack)")]
        public string? Provider { get; init; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<AvailableModelInfo> models;

        if (!string.IsNullOrEmpty(settings.Provider))
        {
            models = await _clientFactory.GetAvailableModelsAsync(settings.Provider, cancellationToken);
        }
        else
        {
            models = await _clientFactory.GetAllAvailableModelsAsync(cancellationToken);
        }

        if (settings.Json)
        {
            return OutputJson(models);
        }

        return OutputTable(models, settings.Provider);
    }

    private static int OutputJson(IReadOnlyList<AvailableModelInfo> models)
    {
        var json = JsonSerializer.Serialize(models, JsonOptions);
        Console.WriteLine(json);
        return 0;
    }

    private static int OutputTable(IReadOnlyList<AvailableModelInfo> models, string? providerFilter)
    {
        if (models.Count == 0)
        {
            if (!string.IsNullOrEmpty(providerFilter))
            {
                AnsiConsole.MarkupLine($"[yellow]No models found for provider: {Markup.Escape(providerFilter)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]No models available.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]Configure a provider in .env file:[/]");
                AnsiConsole.MarkupLine("[grey]  OPENAI_API_KEY, ANTHROPIC_API_KEY, GOOGLE_API_KEY, etc.[/]");
                AnsiConsole.MarkupLine("[grey]Or enable local inference:[/]");
                AnsiConsole.MarkupLine("[grey]  OLLAMA_ENABLED=true, LMSTUDIO_ENABLED=true, LMSUPPLY_ENABLED=true[/]");
            }
            return 0;
        }

        // Group by provider
        var grouped = models
            .GroupBy(m => m.Provider)
            .OrderBy(g => GetProviderSortOrder(g.Key));

        foreach (var group in grouped)
        {
            var providerName = group.Key;
            var providerModels = group.ToList();

            AnsiConsole.MarkupLine($"[bold cyan]{Markup.Escape(providerName.ToUpperInvariant())}[/] ({providerModels.Count} models)");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Model ID")
                .AddColumn("Display Name")
                .AddColumn("Context")
                .AddColumn("Input $/M")
                .AddColumn("Output $/M")
                .AddColumn("Source")
                .AddColumn("Default");

            foreach (var model in providerModels.OrderBy(m => m.ModelId))
            {
                var contextStr = model.ContextWindow.HasValue
                    ? FormatNumber(model.ContextWindow.Value)
                    : "[grey]-[/]";

                var inputPrice = model.InputPricePerMillion.HasValue
                    ? $"${model.InputPricePerMillion:F2}"
                    : "[grey]-[/]";

                var outputPrice = model.OutputPricePerMillion.HasValue
                    ? $"${model.OutputPricePerMillion:F2}"
                    : "[grey]-[/]";

                var sourceStr = model.Source switch
                {
                    ModelSource.Api => "[blue]API[/]",
                    ModelSource.Cached => "[green]Local[/]",
                    ModelSource.Static => "[grey]Static[/]",
                    _ => "[grey]?[/]"
                };

                var defaultStr = model.IsDefault
                    ? "[green]*[/]"
                    : string.Empty;

                table.AddRow(
                    Markup.Escape(model.ModelId),
                    Markup.Escape(model.DisplayName ?? model.ModelId),
                    contextStr,
                    inputPrice,
                    outputPrice,
                    sourceStr,
                    defaultStr
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        // Summary
        AnsiConsole.MarkupLine($"[bold]Total:[/] {models.Count} models from {grouped.Count()} providers");
        AnsiConsole.MarkupLine("[grey]* = default model for provider[/]");

        return 0;
    }

    private static int GetProviderSortOrder(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "gpustack" => 0,
            "openai" => 1,
            "anthropic" or "claude" => 2,
            "google" or "gemini" => 3,
            "xai" or "grok" => 4,
            "azure" or "azure-openai" => 5,
            "ollama" => 6,
            "lmstudio" => 7,
            "lmsupply" or "local" => 8,
            _ => 99
        };
    }

    private static string FormatNumber(int number)
    {
        return number switch
        {
            >= 1_000_000 => $"{number / 1_000_000.0:F1}M",
            >= 1_000 => $"{number / 1_000.0:F0}K",
            _ => number.ToString()
        };
    }
}
