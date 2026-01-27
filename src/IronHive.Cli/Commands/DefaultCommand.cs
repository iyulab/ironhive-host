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
    private readonly IUsageTracker _usageTracker;

    public DefaultCommand(IAgentLoopFactory factory, IUsageTracker usageTracker)
    {
        _factory = factory;
        _usageTracker = usageTracker;
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

        [CommandOption("--no-stream")]
        [Description("Disable streaming output (wait for complete response)")]
        public bool NoStream { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Create agent loop with optional model/provider override
        var agentLoop = _factory.Create(new AgentLoopFactoryOptions
        {
            Provider = settings.Provider,
            Model = settings.Model
        });

        try
        {
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

            // Set up Ctrl+C handler for graceful shutdown
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                AnsiConsole.MarkupLine("\n[yellow]Interrupting...[/]");
            };

            // Single prompt mode
            if (!string.IsNullOrWhiteSpace(settings.Prompt))
            {
                return await RunSinglePromptAsync(settings.Prompt, settings, agentLoop, cts.Token);
            }

            // Interactive mode
            return await RunInteractiveAsync(settings, agentLoop, _usageTracker, cts.Token);
        }
        finally
        {
            // Dispose agent loop if it implements IAsyncDisposable
            if (agentLoop is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    private static async Task<int> RunSinglePromptAsync(string prompt, Settings settings, IAgentLoop agentLoop, CancellationToken cancellationToken = default)
    {
        try
        {
            // Non-streaming mode
            if (settings.NoStream)
            {
                return await RunSinglePromptNonStreamingAsync(prompt, settings, agentLoop, cancellationToken);
            }

            // Streaming mode (default)
            return await RunSinglePromptStreamingAsync(prompt, settings, agentLoop, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 130; // Standard exit code for Ctrl+C
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> RunSinglePromptNonStreamingAsync(string prompt, Settings settings, IAgentLoop agentLoop, CancellationToken cancellationToken)
    {
        AgentResponse response;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Thinking...", async ctx =>
            {
                response = await agentLoop.RunAsync(prompt, cancellationToken);
            });

        response = await agentLoop.RunAsync(prompt, cancellationToken);

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

    private static async Task<int> RunSinglePromptStreamingAsync(string prompt, Settings settings, IAgentLoop agentLoop, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[blue]");

        var hasOutput = false;
        var toolCallsInProgress = new Dictionary<string, string>();

        await foreach (var chunk in agentLoop.RunStreamingAsync(prompt, cancellationToken))
        {
            // Handle text output
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                if (!hasOutput)
                {
                    hasOutput = true;
                }
                // Write text directly without markup escaping for real-time feel
                Console.Write(chunk.TextDelta);
            }

            // Handle tool calls
            if (chunk.ToolCallDelta is not null)
            {
                var toolCall = chunk.ToolCallDelta;
                if (!string.IsNullOrEmpty(toolCall.NameDelta))
                {
                    if (hasOutput)
                    {
                        AnsiConsole.WriteLine();
                    }
                    AnsiConsole.MarkupLine($"[/][grey]→ Calling tool: [cyan]{Markup.Escape(toolCall.NameDelta)}[/][/][blue]");
                    toolCallsInProgress[toolCall.Id] = toolCall.NameDelta;
                    hasOutput = true;
                }
            }
        }

        AnsiConsole.Markup("[/]"); // Close blue markup
        AnsiConsole.WriteLine();

        return 0;
    }

    private static async Task<int> RunInteractiveAsync(Settings settings, IAgentLoop agentLoop, IUsageTracker usageTracker, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new FigletText("IronHive")
            .Color(Color.Yellow));

        AnsiConsole.MarkupLine("[grey]Type 'exit' or press Ctrl+C to quit.[/]");
        if (settings.ShowThinking)
        {
            AnsiConsole.MarkupLine("[grey]Thinking mode: [yellow]enabled[/][/]");
        }
        if (!settings.NoStream)
        {
            AnsiConsole.MarkupLine("[grey]Streaming: [green]enabled[/] (use --no-stream to disable)[/]");
        }
        if (settings.ShowTokens)
        {
            AnsiConsole.MarkupLine("[grey]Token tracking: [green]enabled[/][/]");
        }
        AnsiConsole.WriteLine();

        // Create a linked token source for per-request cancellation
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            string prompt;
            try
            {
                prompt = AnsiConsole.Prompt(
                    new TextPrompt<string>("[green]>[/] ")
                        .AllowEmpty());
            }
            catch (OperationCanceledException)
            {
                break;
            }

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
                if (settings.NoStream)
                {
                    // Non-streaming mode
                    var response = await agentLoop.RunAsync(prompt, cancellationToken);
                    DisplayNonStreamingResponse(response, settings, usageTracker);
                }
                else
                {
                    // Streaming mode
                    await DisplayStreamingResponseAsync(agentLoop, prompt, settings, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Request cancelled. Ready for next input.[/]");
                AnsiConsole.WriteLine();
                // Continue the loop - don't exit on single request cancellation
                continue;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }
        }

        // Display session statistics
        DisplaySessionStatistics(usageTracker);

        AnsiConsole.MarkupLine("[grey]Goodbye![/]");
        return 0;
    }

    private static void DisplayNonStreamingResponse(AgentResponse response, Settings settings, IUsageTracker? usageTracker = null)
    {
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

        // Track and display usage
        if (response.Usage is not null)
        {
            usageTracker?.Record(response.Usage);

            if (settings.ShowTokens)
            {
                AnsiConsole.MarkupLine($"[grey]Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out / {response.Usage.TotalTokens} total[/]");
            }
        }

        AnsiConsole.WriteLine();
    }

    private static async Task DisplayStreamingResponseAsync(IAgentLoop agentLoop, string prompt, Settings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[blue]");

        var hasOutput = false;

        await foreach (var chunk in agentLoop.RunStreamingAsync(prompt, cancellationToken))
        {
            // Handle text output
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                if (!hasOutput)
                {
                    hasOutput = true;
                }
                // Write text directly for real-time streaming
                Console.Write(chunk.TextDelta);
            }

            // Handle tool calls
            if (chunk.ToolCallDelta is not null)
            {
                var toolCall = chunk.ToolCallDelta;
                if (!string.IsNullOrEmpty(toolCall.NameDelta))
                {
                    if (hasOutput)
                    {
                        AnsiConsole.WriteLine();
                    }
                    AnsiConsole.MarkupLine($"[/][grey]→ Calling tool: [cyan]{Markup.Escape(toolCall.NameDelta)}[/][/][blue]");
                    hasOutput = true;
                }
            }
        }

        AnsiConsole.Markup("[/]"); // Close blue markup
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    private static void DisplaySessionStatistics(IUsageTracker usageTracker)
    {
        var session = usageTracker.GetSessionUsage();

        if (session.RequestCount == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Session Statistics[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[grey]Value[/]").RightAligned());

        table.AddRow("Requests", session.RequestCount.ToString());
        table.AddRow("Input Tokens", session.TotalInputTokens.ToString("N0"));
        table.AddRow("Output Tokens", session.TotalOutputTokens.ToString("N0"));
        table.AddRow("Total Tokens", session.TotalTokens.ToString("N0"));
        table.AddRow("Avg per Request", session.AverageTokensPerRequest.ToString("N1"));
        table.AddRow("Est. Cost (USD)", $"${session.EstimatedCostUsd:F6}");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
