using IronHive.Agent.Context;
using IronHive.Agent.Invocation;
using IronHive.Agent.Mode;
using IronHive.Agent.Tracking;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Tools;

/// <summary>
/// The runtime composition the CLI and <c>run --server</c> run every model call and tool call through. A host that embeds
/// the agent runtime calls the same two methods to get the same behaviour — loop guards, approval, argument recovery, text
/// tool-call recovery, context-window guard — instead of copying the list.
/// </summary>
public static class HostChatPipeline
{
    /// <summary>
    /// Wraps a provider's client (outer → inner): function invocation through <paramref name="toolInvocationPipeline"/>
    /// (see <see cref="CreateToolInvocationPipeline"/>) with text tool-call recovery directly under it, an unbound
    /// <see cref="ToolRoundContextChatClient"/> the agent loop binds to its own <see cref="ContextManager"/>, an unbound
    /// <see cref="UsageLimitChatClient"/> the loop binds to its own usage limiter (when a budget is set), then
    /// <see cref="TokenBudgetChatClient"/>.
    /// </summary>
    /// <param name="inner">The provider's client.</param>
    /// <param name="behavior">The iteration and consecutive-error limits of the function-invoking client.</param>
    /// <param name="toolInvocationPipeline">The steps every tool call goes through.</param>
    /// <param name="telemetrySourceName">
    /// When set, one OpenTelemetry span per model call under this source name (message content is not recorded); with no
    /// listener it costs nothing. Null (the default) adds no telemetry decorator.
    /// </param>
    public static IChatClient Decorate(
        IChatClient inner,
        ChatBehaviorConfig behavior,
        ToolInvocationPipeline toolInvocationPipeline,
        string? telemetrySourceName = null) =>
        Decorate(inner, behavior, toolInvocationPipeline, telemetrySourceName, declaredContextWindow: 0);

    /// <summary>
    /// <see cref="Decorate(IChatClient, ChatBehaviorConfig, ToolInvocationPipeline, string?)"/>, with the context window the
    /// user declared for the model (<c>compaction.maxContextTokens</c> in the CLI): the token budget guard uses it when the
    /// provider reports no window of its own (a remote OpenAI-compatible server or gateway). 0 = none declared — then the
    /// guard has no budget and the server enforces its window.
    /// </summary>
    public static IChatClient Decorate(
        IChatClient inner,
        ChatBehaviorConfig behavior,
        ToolInvocationPipeline toolInvocationPipeline,
        string? telemetrySourceName,
        int declaredContextWindow)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(toolInvocationPipeline);
        ArgumentOutOfRangeException.ThrowIfNegative(declaredContextWindow);

        // Innermost, around the provider: one model call that fails for a transient reason (a 502 from the gateway, a refused
        // or reset connection) before any of it reached the caller is tried again, so a blip on one call of a long turn does
        // not end the turn and lose its tool work.
        var builder = new ToolRoundContextChatClient(new UsageLimitChatClient(new TokenBudgetChatClient(new TransientRetryChatClient(inner), declaredContextWindow)))
            .AsBuilder()
            .UseToolInvocationPipeline(toolInvocationPipeline, client =>
            {
                client.MaximumIterationsPerRequest = behavior.MaximumIterationsPerRequest;
                client.MaximumConsecutiveErrorsPerRequest = behavior.MaximumConsecutiveErrorsPerRequest;
            })
            // Directly under the function-invoking client: a call a local model wrote as text (its server's parser
            // missed it) becomes the call that client runs, instead of a reply that ends the turn as completed.
            .UseTextToolCallRecovery();
        if (telemetrySourceName is not null)
        {
            builder = builder.UseOpenTelemetry(sourceName: telemetrySourceName);
        }

        return builder.Build();
    }

    /// <summary>
    /// The steps every tool call goes through, outermost first:
    /// <list type="number">
    /// <item><see cref="InvalidArgumentsMiddleware"/> — a call whose arguments could not be parsed, or that leaves out a required one, is not run.</item>
    /// <item><see cref="RepeatedCallGuardMiddleware"/> — the same call after three successful runs in a row is not run.</item>
    /// <item><see cref="RepeatedResultGuardMiddleware"/> — the same result on a third separate visit ends the turn with the cause.</item>
    /// <item><see cref="RepeatedErrorGuardMiddleware"/> — the same error three times in a row ends the turn with a result.</item>
    /// <item><see cref="ApprovalGateMiddleware"/> — Planning mode, then the permission rules and the approval prompt (Allow / Deny / Ask).</item>
    /// <item><see cref="ResilientArgumentsMiddleware"/> — arguments that do not bind become a recovery directive.</item>
    /// </list>
    /// The loop guards sit in front of the gate so the user is never asked to approve a call that would be refused
    /// anyway; the resilient step sits directly around the tool, so the error guard only counts errors it lets through.
    /// </summary>
    /// <param name="policy">
    /// The permission rules the approval gate applies. Null leaves the gate out — for an unattended host that runs every
    /// call (an evaluation harness); the other steps stay.
    /// </param>
    /// <param name="approvalService">Asks the user when the policy answers Ask; null refuses those calls.</param>
    /// <param name="loggerFactory">Logs each step's refusals; null logs nothing.</param>
    /// <param name="modeManager">When given, the gate enforces Planning mode (a tool the mode does not permit is denied).</param>
    /// <param name="modeToolFilter">Which tools a mode permits; required with <paramref name="modeManager"/>.</param>
    /// <param name="options">Step limits and the per-call time limit; null uses the defaults.</param>
    public static ToolInvocationPipeline CreateToolInvocationPipeline(
        IToolCallPolicy? policy,
        IHumanApprovalService? approvalService = null,
        ILoggerFactory? loggerFactory = null,
        IModeManager? modeManager = null,
        IModeToolFilter? modeToolFilter = null,
        ToolInvocationOptions? options = null)
    {
        options ??= new ToolInvocationOptions();
        List<IToolInvocationMiddleware> steps =
        [
            new InvalidArgumentsMiddleware(options, loggerFactory?.CreateLogger<InvalidArgumentsMiddleware>()),
            new RepeatedCallGuardMiddleware(options, loggerFactory?.CreateLogger<RepeatedCallGuardMiddleware>()),
            new RepeatedResultGuardMiddleware(options, loggerFactory?.CreateLogger<RepeatedResultGuardMiddleware>()),
            new RepeatedErrorGuardMiddleware(options, loggerFactory?.CreateLogger<RepeatedErrorGuardMiddleware>()),
        ];
        if (policy is not null)
        {
            steps.Add(new ApprovalGateMiddleware(policy, approvalService, loggerFactory?.CreateLogger<ApprovalGateMiddleware>(), modeManager, modeToolFilter));
        }

        steps.Add(new ResilientArgumentsMiddleware());
        return new ToolInvocationPipeline(steps, resultMiddleware: null, options);
    }
}
