using System.ComponentModel;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Agent.Mode;
using IronHive.Cli.Core.Update;
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
    private readonly IModeManager _modeManager;
    private readonly IUpdateService _updateService;

    public DefaultCommand(IAgentLoopFactory factory, IUsageTracker usageTracker, IModeManager modeManager, IUpdateService updateService)
    {
        _factory = factory;
        _usageTracker = usageTracker;
        _modeManager = modeManager;
        _updateService = updateService;
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

        [CommandOption("--plan")]
        [Description("Enter planning mode (read-only exploration)")]
        public bool PlanMode { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show what would be done without executing")]
        public bool DryRun { get; init; }

        [CommandOption("--update")]
        [Description("Check for updates after execution")]
        public bool CheckUpdate { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Start background update check (unless --update flag is used, which will check after execution)
        if (!settings.CheckUpdate)
        {
            UpdateChecker.StartBackgroundCheck(_updateService);
        }

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

            // Initialize mode based on flags
            if (settings.PlanMode || settings.DryRun)
            {
                _modeManager.Fire(ModeTrigger.StartPlanning);
                AnsiConsole.MarkupLine("[grey]Mode: [yellow]Planning[/] (read-only)[/]");
                if (settings.DryRun)
                {
                    AnsiConsole.MarkupLine("[grey]Dry-run: [yellow]enabled[/] (no execution)[/]");
                }
            }
            else
            {
                _modeManager.Fire(ModeTrigger.StartWorking);
            }

            // Set up Ctrl+C handler for graceful shutdown
            using var cts = new CancellationTokenSource();
            var cancelCount = 0;
            Console.CancelKeyPress += (_, e) =>
            {
                cancelCount++;
                if (cancelCount >= 2)
                {
                    // Force exit on second Ctrl+C
                    AnsiConsole.MarkupLine("\n[red]Force exit.[/]");
                    Environment.Exit(130);
                    return;
                }

                e.Cancel = true;
                cts.Cancel();
                AnsiConsole.MarkupLine("\n[yellow]Interrupting... (press Ctrl+C again to force exit)[/]");
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

            // Handle update check
            if (settings.CheckUpdate)
            {
                // --update flag: check for updates after execution
                await CheckAndDisplayUpdateAsync();
            }
            else
            {
                // Normal mode: display notification if background check found an update
                UpdateChecker.DisplayUpdateNotificationIfAvailable();
            }
        }
    }

    private async Task CheckAndDisplayUpdateAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[blue]Update Check[/]").RuleStyle("grey"));

        UpdateInfo? updateInfo = null;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Checking for updates...", async _ =>
            {
                updateInfo = await _updateService.CheckForUpdateAsync();
            });

        if (updateInfo is null)
        {
            AnsiConsole.MarkupLine("[grey]Could not check for updates.[/]");
            return;
        }

        if (updateInfo.IsUpdateAvailable)
        {
            AnsiConsole.MarkupLine($"[green]New version available: v{updateInfo.LatestVersion}[/]");
            AnsiConsole.MarkupLine($"[grey]Current version: v{updateInfo.CurrentVersion}[/]");
            AnsiConsole.MarkupLine("[grey]Run [cyan]ironhive update[/] to update.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]You are running the latest version (v{updateInfo.CurrentVersion}).[/]");
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
                // Write text directly for real-time streaming (no markup processing)
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
                        Console.WriteLine();
                    }
                    AnsiConsole.MarkupLine($"[grey]→ Calling tool: [cyan]{Markup.Escape(toolCall.NameDelta)}[/][/]");
                    toolCallsInProgress[toolCall.Id] = toolCall.NameDelta;
                    hasOutput = true;
                }
            }
        }

        Console.WriteLine();

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

            // Handle /model command
            if (prompt.StartsWith("/model", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[yellow]To change provider, restart with --provider option:[/]");
                AnsiConsole.MarkupLine("  [cyan]ironhive --provider gpustack[/]  (remote API)");
                AnsiConsole.MarkupLine("  [cyan]ironhive --provider local[/]     (local LMSupply)");
                AnsiConsole.MarkupLine("  [cyan]ironhive --provider lmsupply[/]  (local LMSupply)");
                AnsiConsole.WriteLine();
                continue;
            }

            // Handle /help command
            if (prompt.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[yellow]Available commands:[/]");
                AnsiConsole.MarkupLine("  [cyan]/model[/]  - Show provider options");
                AnsiConsole.MarkupLine("  [cyan]/help[/]   - Show this help");
                AnsiConsole.MarkupLine("  [cyan]exit[/]    - Exit the program");
                AnsiConsole.WriteLine();
                continue;
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
                // Write text directly for real-time streaming (no markup processing)
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
                        Console.WriteLine();
                    }
                    AnsiConsole.MarkupLine($"[grey]→ Calling tool: [cyan]{Markup.Escape(toolCall.NameDelta)}[/][/]");
                    hasOutput = true;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine();
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
