using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using IronHive.Agent.ErrorRecovery;
using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using IronHive.Host.Session;
using IronHive.Host.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Run command - executes a single prompt and exits.
/// </summary>
public class RunCommand : AsyncCommand<RunCommand.Settings>
{
    private readonly IHostAgentLoopFactory _factory;
    private readonly IMcpPluginManager? _pluginManager;
    private readonly HitlBridge? _hitlBridge;
    private readonly IronHiveConfig? _config;
    private readonly ISessionManager? _sessionManager;
    private readonly IErrorRecoveryService? _errorRecovery;

    public RunCommand(
        IHostAgentLoopFactory factory,
        IMcpPluginManager? pluginManager = null,
        HitlBridge? hitlBridge = null,
        IronHiveConfig? config = null,
        ISessionManager? sessionManager = null,
        IErrorRecoveryService? errorRecovery = null)
    {
        _factory = factory;
        _pluginManager = pluginManager;
        _hitlBridge = hitlBridge;
        _config = config;
        _sessionManager = sessionManager;
        _errorRecovery = errorRecovery;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[PROMPT]")]
        [Description("The prompt to execute")]
        public string? PromptArg { get; init; }

        [CommandOption("-p|--prompt <PROMPT>")]
        [Description("The prompt to execute (alternative to argument)")]
        public string? PromptOption { get; init; }

        [CommandOption("-m|--model <MODEL>")]
        [Description("Model to use")]
        public string? Model { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Provider (gpustack, lmsupply)")]
        public string? Provider { get; init; }

        [CommandOption("--json")]
        [Description("Output the result as JSON: content, stop_reason, duration_ms, tool call counts, usage")]
        public bool Json { get; init; }

        [CommandOption("--max-iterations <N>")]
        [Description("Model-call rounds allowed in this turn (overrides chatBehavior.maximumIterationsPerRequest)")]
        public int? MaxIterations { get; init; }

        [CommandOption("--max-output-tokens <N>")]
        [Description("Output tokens per model call (overrides chatBehavior.maxOutputTokens)")]
        public int? MaxOutputTokens { get; init; }

        [CommandOption("--reasoning-effort <LEVEL>")]
        [Description("How much the model reasons per call: none, low, medium, high, extra_high (overrides chatBehavior.reasoningEffort)")]
        public string? ReasoningEffort { get; init; }

        [CommandOption("--tool-timeout <SECONDS>")]
        [Description("Longest one tool call may run before it is stopped (overrides chatBehavior.toolCallTimeoutSeconds)")]
        public int? ToolTimeoutSeconds { get; init; }

        [CommandOption("--timeout <SECONDS>")]
        [Description("Stop the run after this many seconds (stop_reason \"timeout\", exit code 2)")]
        public int? TimeoutSeconds { get; init; }

        [CommandOption("--show-tokens")]
        [Description("Show token usage statistics")]
        public bool ShowTokens { get; init; }

        [CommandOption("--show-thinking")]
        [Description("Show thinking/reasoning content from the model")]
        public bool ShowThinking { get; init; }

        [CommandOption("--server")]
        [Description("Run in server mode (JSON Lines stdin/stdout)")]
        public bool Server { get; init; }

        [CommandOption("--session-id <SESSION_ID>")]
        [Description("Session ID for server mode")]
        public string? SessionId { get; init; }

        [CommandOption("--auto-commit")]
        [Description("Automatically commit changes after successful execution")]
        public bool AutoCommit { get; init; }

        [CommandOption("--commit-message <MESSAGE>")]
        [Description("Custom commit message (default: auto-generated from prompt)")]
        public string? CommitMessage { get; init; }

        public string? GetPrompt() => PromptArg ?? PromptOption;

        public override ValidationResult Validate()
        {
            if (MaxIterations is <= 0)
            {
                return ValidationResult.Error("--max-iterations must be a positive number.");
            }

            if (MaxOutputTokens is <= 0)
            {
                return ValidationResult.Error("--max-output-tokens must be a positive number.");
            }

            if (ReasoningEffort is not null && !ReasoningEffortName.TryParse(ReasoningEffort, out _))
            {
                return ValidationResult.Error(
                    $"--reasoning-effort must be one of {string.Join(", ", ReasoningEffortName.Values)}.");
            }

            if (TimeoutSeconds is <= 0)
            {
                return ValidationResult.Error("--timeout must be a positive number of seconds.");
            }

            if (ToolTimeoutSeconds is <= 0)
            {
                return ValidationResult.Error("--tool-timeout must be a positive number of seconds.");
            }

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Server)
        {
            return await RunServerModeAsync(settings, cancellationToken);
        }

        return await RunOnceAsync(settings, Console.Out, cancellationToken);
    }

    /// <summary>
    /// One prompt, one turn, then exit — the code a script reads (<see cref="RunOutcome"/>): 0 completed, 1 error,
    /// 2 stopped short (a step, output or time limit, or a guard), 3 content filter. Under <c>--json</c>
    /// <paramref name="stdout"/> carries only the result document.
    /// </summary>
    internal async Task<int> RunOnceAsync(Settings settings, TextWriter stdout, CancellationToken cancellationToken)
    {
        var prompt = settings.GetPrompt();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            if (settings.Json)
            {
                await stdout.WriteLineAsync(RunOutcome.FailureJson(RunOutcome.ErrorReason, "A prompt is required.", 0));
                return RunOutcome.Error;
            }

            AnsiConsole.MarkupLine("[red]Error: Prompt is required.[/]");
            AnsiConsole.MarkupLine("[grey]Usage: ironhive run -p \"your prompt here\"[/]");
            return RunOutcome.Error;
        }

        // This process runs one turn, so the flag is the turn's setting: the chat client reads the cap when the loop is
        // created below.
        if (settings.MaxOutputTokens is { } maxOutputTokens && _config is not null)
        {
            _config.ChatBehavior.MaxOutputTokens = maxOutputTokens;
        }

        if (settings.ReasoningEffort is { } reasoningEffort && _config is not null)
        {
            _config.ChatBehavior.ReasoningEffort = reasoningEffort;
        }

        if (settings.MaxIterations is { } maxIterations && _config is not null)
        {
            _config.ChatBehavior.MaximumIterationsPerRequest = maxIterations;
        }

        if (settings.ToolTimeoutSeconds is { } toolTimeout && _config is not null)
        {
            _config.ChatBehavior.ToolCallTimeoutSeconds = toolTimeout;
        }

        // Capture initial Git state if auto-commit is enabled
        GitStatus? initialStatus = null;
        if (settings.AutoCommit && GitHelper.IsGitRepository())
        {
            initialStatus = GitHelper.GetStatus();
        }

        using var timeout = settings.TimeoutSeconds is { } seconds ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : null;
        using var linked = timeout is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var clock = Stopwatch.StartNew();
        IAgentLoop? agentLoop = null;

        try
        {
            // Inside the error boundary: a provider that cannot be created is reported like any other failure (JSON
            // under --json), not as unstructured text from the host.
            agentLoop = await _factory.CreateAsync(new AgentLoopFactoryOptions
            {
                Provider = settings.Provider,
                Model = settings.Model
            }, linked.Token);

            var response = await agentLoop.RunAsync(prompt, linked.Token);
            clock.Stop();

            if (settings.Json)
            {
                await stdout.WriteLineAsync(RunOutcome.Json(response, clock.ElapsedMilliseconds, settings.ShowThinking));
            }
            else
            {
                // Show thinking content if available and requested
                if (settings.ShowThinking && response.ThinkingContent?.Content is not null)
                {
                    await stdout.WriteLineAsync("=== Thinking ===");
                    await stdout.WriteLineAsync(response.ThinkingContent.Content);
                    if (response.ThinkingContent.TokenCount.HasValue)
                    {
                        await stdout.WriteLineAsync($"(Thinking tokens: {response.ThinkingContent.TokenCount.Value})");
                    }
                    await stdout.WriteLineAsync("================");
                    await stdout.WriteLineAsync();
                }

                await stdout.WriteLineAsync(response.Content);

                if (settings.ShowTokens && response.Usage is not null)
                {
                    await stdout.WriteLineAsync();
                    await stdout.WriteLineAsync($"Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out / {response.Usage.TotalTokens} total");
                }

                if (response.StopReason != TurnStopReason.Completed)
                {
                    await Console.Error.WriteLineAsync($"Stopped: {RunOutcome.ReasonOf(response.StopReason)}");
                }
            }

            // Handle auto-commit if enabled
            if (settings.AutoCommit && initialStatus is not null)
            {
                await HandleAutoCommitAsync(settings, prompt);
            }

            return RunOutcome.ExitCodeFor(response.StopReason);
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            var message = $"The run did not finish within {settings.TimeoutSeconds} s.";
            if (settings.Json)
            {
                await stdout.WriteLineAsync(RunOutcome.FailureJson(RunOutcome.Timeout, message, clock.ElapsedMilliseconds));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
            }

            return RunOutcome.Incomplete;
        }
        catch (Exception ex)
        {
            if (settings.Json)
            {
                await stdout.WriteLineAsync(RunOutcome.FailureJson(RunOutcome.ErrorReason, ex.Message, clock.ElapsedMilliseconds));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }

            return RunOutcome.Error;
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

    private async Task<int> RunServerModeAsync(Settings settings, CancellationToken ct)
    {
        var created = await _factory.CreateWithToolsAsync(new AgentLoopFactoryOptions
        {
            Provider = settings.Provider,
            Model = settings.Model
        }, ct);
        var agentLoop = created.Loop;

        McpHealthCheckService? healthCheck = null;
        try
        {
            // The session is the resume key: started again with the same --session-id, the server continues the
            // conversation it had (each turn, and each tool call as it completes, is written as it happens).
            SessionTurnRecorder? recorder = null;
            IReadOnlyList<ApprovalWaitEntry> pending = [];
            var sessionId = settings.SessionId ?? Guid.NewGuid().ToString("N");
            if (_sessionManager is not null)
            {
                var session = await _sessionManager.OpenSessionAsync(
                    sessionId, Directory.GetCurrentDirectory(), settings.Model ?? "default", ct);
                var history = await _sessionManager.RestoreContextAsync(session, ct);
                if (history.Count > 0)
                {
                    await agentLoop.InitializeHistoryAsync(history, ct);
                }

                recorder = new SessionTurnRecorder(_sessionManager, session);
                if (_hitlBridge is not null)
                {
                    // Approval waits are written to the session, so a restart can offer them again and finish the turn
                    _hitlBridge.WaitLog = recorder;
                    pending = await _sessionManager.GetPendingApprovalsAsync(session, ct);
                }
            }

            await AgentServerRunner.WriteEventAsync(
                Console.Out,
                new SessionStartedEvent(sessionId), cancellationToken: ct);

            await using var executionLog = new ExecutionLogService();
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ironhive", "logs");
            Directory.CreateDirectory(logDir);
            executionLog.Initialize(Path.Combine(logDir, $"{sessionId}.execlog.jsonl"));

            // Start MCP health check if plugins are connected
            if (_pluginManager is not null && _pluginManager.ConnectedPlugins.Count > 0)
            {
                healthCheck = new McpHealthCheckService(_pluginManager);
                await healthCheck.StartAsync(ct);
            }

            async IAsyncEnumerable<ServerEvent> ProcessMessage(
                UserMessageRequest msg,
                [EnumeratorCancellation] CancellationToken token)
            {
                // Per-turn options become the loop's override for this turn only; a request without
                // them runs on the loop's configuration. Unknown tool names throw here and reach the
                // client as an ErrorEvent (the runner turns exceptions into one) — never a silent drop.
                var overrideOptions = TurnOptionsMapper.ToChatOptions(msg.Options, created.Tools);

                var turn = agentLoop.RunStreamingAsync(msg.Content, overrideOptions, token);
                if (recorder is not null)
                {
                    turn = recorder.RecordAsync(msg.Content, turn, token);
                }

                await foreach (var evt in turn.ToServerEvents(executionLog, token))
                {
                    yield return evt;
                }
            }

            var logger = NullLogger<AgentServerRunner>.Instance;
            var runner = new AgentServerRunner(ProcessMessage, logger, hitlBridge: _hitlBridge);
            if (_errorRecovery is not null)
            {
                // The same classifier error recovery uses (with the gateways' failure readers), so ErrorEvent.Code agrees with it.
                runner.ErrorClassifier = _errorRecovery;
            }
            if (pending.Count > 0 && recorder is not null && _hitlBridge is not null)
            {
                var bridge = _hitlBridge;
                runner.ResumeTurn = token => SuspendedTurnResumer
                    .ResumeAsync(agentLoop, pending, bridge, recorder, created.Tools, created.Pipeline, token)
                    .ToServerEvents(executionLog, token);
            }
            await runner.RunAsync(ct);

            return 0;
        }
        finally
        {
            if (healthCheck is not null)
            {
                await healthCheck.DisposeAsync();
            }

            if (agentLoop is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    private static Task HandleAutoCommitAsync(Settings settings, string prompt)
    {
        if (!GitHelper.IsGitRepository())
        {
            AnsiConsole.MarkupLine("[yellow]Auto-commit skipped: Not a Git repository[/]");
            return Task.CompletedTask;
        }

        var status = GitHelper.GetStatus();
        if (!status.HasChanges)
        {
            AnsiConsole.MarkupLine("[grey]Auto-commit skipped: No changes detected[/]");
            return Task.CompletedTask;
        }

        // Stage all changes
        if (!GitHelper.StageAll())
        {
            AnsiConsole.MarkupLine("[red]Auto-commit failed: Could not stage changes[/]");
            return Task.CompletedTask;
        }

        // Generate commit message
        var commitMessage = settings.CommitMessage ?? GenerateCommitMessage(prompt, status);

        // Create commit
        if (GitHelper.Commit(commitMessage))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓[/] Changes committed: {Markup.Escape(commitMessage)}");
            AnsiConsole.MarkupLine($"[grey]  {status.TotalChangedFiles} file(s) changed[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Auto-commit failed: Could not create commit[/]");
        }

        return Task.CompletedTask;
    }

    private static string GenerateCommitMessage(string prompt, GitStatus status)
    {
        // Create a concise commit message based on the prompt
        var truncatedPrompt = prompt.Length > 50
            ? string.Concat(prompt.AsSpan(0, 47), "...")
            : prompt;

        // Clean up the prompt for use in commit message
        truncatedPrompt = truncatedPrompt
            .Replace("\n", " ")
            .Replace("\r", "")
            .Trim();

        return $"ironhive: {truncatedPrompt}";
    }
}
