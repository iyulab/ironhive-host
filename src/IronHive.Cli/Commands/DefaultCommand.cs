using System.ComponentModel;
using IronHive.Cli.Core.Agent;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Default command - enters interactive chat mode.
/// </summary>
public class DefaultCommand : AsyncCommand<DefaultCommand.Settings>
{
    private readonly IAgentLoopFactory _factory;

    public DefaultCommand(IAgentLoopFactory factory)
    {
        _factory = factory;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-p|--prompt <PROMPT>")]
        [Description("Run a single prompt and exit")]
        public string? Prompt { get; init; }

        [CommandOption("-m|--model <MODEL>")]
        [Description("Model to use (e.g., gpt-4o-mini, llama3.2)")]
        public string? Model { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Provider (openai, ollama, gpustack)")]
        public string? Provider { get; init; }

        [CommandOption("--show-tokens")]
        [Description("Show token usage statistics")]
        public bool ShowTokens { get; init; }

        [CommandOption("--show-thinking")]
        [Description("Show thinking/reasoning content from the model")]
        public bool ShowThinking { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Create agent loop with optional model/provider override
        var agentLoop = _factory.Create(new AgentLoopFactoryOptions
        {
            Provider = settings.Provider,
            Model = settings.Model
        });

        // Show configuration info
        if (settings.Model is not null || settings.Provider is not null)
        {
            var info = new List<string>();
            if (settings.Provider is not null)
            {
                info.Add($"provider: [cyan]{settings.Provider}[/]");
            }
            if (settings.Model is not null)
            {
                info.Add($"model: [cyan]{settings.Model}[/]");
            }
            AnsiConsole.MarkupLine($"[grey]Using {string.Join(", ", info)}[/]");
        }

        // Single prompt mode
        if (!string.IsNullOrWhiteSpace(settings.Prompt))
        {
            return await RunSinglePromptAsync(settings.Prompt, settings, agentLoop);
        }

        // Interactive mode
        return await RunInteractiveAsync(settings, agentLoop);
    }

    private static async Task<int> RunSinglePromptAsync(string prompt, Settings settings, IAgentLoop agentLoop)
    {
        try
        {
            AnsiConsole.MarkupLine("[grey]Running prompt...[/]");

            var response = await agentLoop.RunAsync(prompt);

            // Show thinking content if available and requested
            if (settings.ShowThinking && response.ThinkingContent?.Content is not null)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel(Markup.Escape(response.ThinkingContent.Content))
                    .Header("[yellow]Thinking[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Yellow));

                if (response.ThinkingContent.TokenCount.HasValue)
                {
                    AnsiConsole.MarkupLine($"[grey]Thinking tokens: {response.ThinkingContent.TokenCount.Value}[/]");
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(response.Content)
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Blue));

            if (settings.ShowTokens && response.Usage is not null)
            {
                AnsiConsole.MarkupLine($"[grey]Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out / {response.Usage.TotalTokens} total[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> RunInteractiveAsync(Settings settings, IAgentLoop agentLoop)
    {
        AnsiConsole.Write(new FigletText("IronHive")
            .Color(Color.Yellow));

        AnsiConsole.MarkupLine("[grey]Type 'exit' or press Ctrl+C to quit.[/]");
        if (settings.ShowThinking)
        {
            AnsiConsole.MarkupLine("[grey]Thinking mode: [yellow]enabled[/][/]");
        }
        AnsiConsole.WriteLine();

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.Token.IsCancellationRequested)
        {
            var prompt = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]>[/] ")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                prompt.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                var response = await agentLoop.RunAsync(prompt, cts.Token);

                // Show thinking content if available and requested
                if (settings.ShowThinking && response.ThinkingContent?.Content is not null)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Panel(Markup.Escape(response.ThinkingContent.Content))
                        .Header("[yellow]Thinking[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Color.Yellow)
                        .Collapse());

                    if (response.ThinkingContent.TokenCount.HasValue)
                    {
                        AnsiConsole.MarkupLine($"[grey]Thinking tokens: {response.ThinkingContent.TokenCount.Value}[/]");
                    }
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[blue]{Markup.Escape(response.Content)}[/]");
                AnsiConsole.WriteLine();

                if (settings.ShowTokens && response.Usage is not null)
                {
                    AnsiConsole.MarkupLine($"[grey]Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out / {response.Usage.TotalTokens} total[/]");
                }

                AnsiConsole.WriteLine();
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }
        }

        AnsiConsole.MarkupLine("[grey]Goodbye![/]");
        return 0;
    }
}
