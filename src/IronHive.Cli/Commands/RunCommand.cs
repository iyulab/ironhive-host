using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Cli.Core.Server;
using IronHive.Cli.Core.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Run command - executes a single prompt and exits.
/// </summary>
public class RunCommand : AsyncCommand<RunCommand.Settings>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAgentLoopFactory _factory;
    private readonly IMcpPluginManager? _pluginManager;

    public RunCommand(IAgentLoopFactory factory, IMcpPluginManager? pluginManager = null)
    {
        _factory = factory;
        _pluginManager = pluginManager;
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
        [Description("Output response as JSON")]
        public bool Json { get; init; }

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
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Server)
        {
            return await RunServerModeAsync(settings, cancellationToken);
        }

        var prompt = settings.GetPrompt();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            AnsiConsole.MarkupLine("[red]Error: Prompt is required.[/]");
            AnsiConsole.MarkupLine("[grey]Usage: ironhive run -p \"your prompt here\"[/]");
            return 1;
        }

        // Capture initial Git state if auto-commit is enabled
        GitStatus? initialStatus = null;
        if (settings.AutoCommit && GitHelper.IsGitRepository())
        {
            initialStatus = GitHelper.GetStatus();
        }

        // Create agent loop with optional model/provider override
        var agentLoop = await _factory.CreateAsync(new AgentLoopFactoryOptions
        {
            Provider = settings.Provider,
            Model = settings.Model
        }, cancellationToken);

        try
        {
            var response = await agentLoop.RunAsync(prompt, cancellationToken);

            if (settings.Json)
            {
                var json = JsonSerializer.Serialize(new
                {
                    content = response.Content,
                    thinking = settings.ShowThinking && response.ThinkingContent is not null ? new
                    {
                        content = response.ThinkingContent.Content,
                        token_count = response.ThinkingContent.TokenCount
                    } : null,
                    usage = response.Usage is not null ? new
                    {
                        input_tokens = response.Usage.InputTokens,
                        output_tokens = response.Usage.OutputTokens,
                        total_tokens = response.Usage.TotalTokens
                    } : null
                }, JsonOptions);

                Console.WriteLine(json);
            }
            else
            {
                // Show thinking content if available and requested
                if (settings.ShowThinking && response.ThinkingContent?.Content is not null)
                {
                    Console.WriteLine("=== Thinking ===");
                    Console.WriteLine(response.ThinkingContent.Content);
                    if (response.ThinkingContent.TokenCount.HasValue)
                    {
                        Console.WriteLine($"(Thinking tokens: {response.ThinkingContent.TokenCount.Value})");
                    }
                    Console.WriteLine("================");
                    Console.WriteLine();
                }

                Console.WriteLine(response.Content);

                if (settings.ShowTokens && response.Usage is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out / {response.Usage.TotalTokens} total");
                }
            }

            // Handle auto-commit if enabled
            if (settings.AutoCommit && initialStatus is not null)
            {
                await HandleAutoCommitAsync(settings, prompt);
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    error = ex.Message
                }, JsonOptions));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }

            return 1;
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
        var agentLoop = await _factory.CreateAsync(new AgentLoopFactoryOptions
        {
            Provider = settings.Provider,
            Model = settings.Model
        }, ct);

        McpHealthCheckService? healthCheck = null;
        try
        {
            var sessionId = settings.SessionId ?? Guid.NewGuid().ToString("N");

            await AgentServerRunner.WriteEventAsync(
                Console.Out,
                new SessionStartedEvent(sessionId));

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
                healthCheck.Start();
            }

            async IAsyncEnumerable<ServerEvent> ProcessMessage(
                UserMessageRequest msg,
                [EnumeratorCancellation] CancellationToken token)
            {
                await foreach (var evt in agentLoop.RunStreamingAsync(msg.Content, token)
                    .ToServerEvents(executionLog, token))
                {
                    yield return evt;
                }
            }

            var logger = NullLogger<AgentServerRunner>.Instance;
            var runner = new AgentServerRunner(ProcessMessage, logger);
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
