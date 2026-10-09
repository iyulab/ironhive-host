using System.Text.Json;
using IronHive.Agent.Loop;

namespace IronHive.Cli.Commands;

/// <summary>
/// What <c>ironhive run</c> reports about one run, for a caller that is not a person: the <c>--json</c> document and the
/// exit code. Both say whether the turn completed and, if not, why — so a script, a scheduler or a benchmark harness
/// never has to read the model's prose to tell "done" from "gave up at the step limit".
/// </summary>
/// <remarks>
/// Exit codes: <see cref="Completed"/> 0 · <see cref="Error"/> 1 · <see cref="Incomplete"/> 2 (a step, output or time
/// limit, or a guard that ended the turn) · <see cref="Filtered"/> 3 (the provider's content filter) ·
/// <see cref="Cancelled"/> 130 (the caller cancelled the run).
/// </remarks>
internal static class RunOutcome
{
    public const int Completed = 0;
    public const int Error = 1;
    public const int Incomplete = 2;
    public const int Filtered = 3;

    /// <summary>The exit code of a run the caller cancelled — the conventional code for an interrupt.</summary>
    public const int Cancelled = 130;

    /// <summary>The <c>stop_reason</c> value of a run that hit <c>--timeout</c>.</summary>
    public const string Timeout = "timeout";

    /// <summary>The <c>stop_reason</c> value of a run the caller cancelled.</summary>
    public const string CancelledReason = "cancelled";

    /// <summary>The <c>stop_reason</c> value of a run that failed with an error.</summary>
    public const string ErrorReason = "error";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>How much of a call's arguments, and of a refusal's message, the <c>calls</c> list keeps.</summary>
    internal const int CallTextLimit = 300;

    /// <summary>The <c>stop_reason</c> text of a turn: the loop's reason in snake case (<c>step_limit</c>).</summary>
    public static string ReasonOf(TurnStopReason reason) => JsonNamingPolicy.SnakeCaseLower.ConvertName(reason.ToString());

    /// <summary>The exit code for a turn that ended for <paramref name="reason"/>.</summary>
    public static int ExitCodeFor(TurnStopReason reason) => reason switch
    {
        TurnStopReason.Completed => Completed,
        TurnStopReason.ContentFilter => Filtered,
        _ => Incomplete,
    };

    /// <summary>The <c>--json</c> document of a turn that ended.</summary>
    public static string Json(AgentResponse response, long durationMs, bool showThinking) => JsonSerializer.Serialize(new
    {
        content = response.Content,
        stop_reason = ReasonOf(response.StopReason),
        duration_ms = durationMs,
        tool_calls = response.ToolCalls.Count,
        refused_tool_calls = response.ToolCalls.Count(c => c.RefusalKind is not null),
        failed_tool_calls = response.ToolCalls.Count(c => c.Success == false && c.RefusalKind is null),
        // In call order, so a reader can see which call a guard refused and what it said — a `tool_terminated` run is
        // otherwise only a count. Arguments are cut to CallTextLimit (a write's content can be the whole file); a tool's
        // own result is left out, a refusal's message kept.
        calls = response.ToolCalls.Select(c => new
        {
            tool = c.ToolName,
            outcome = OutcomeOf(c),
            refusal_kind = c.RefusalKind is { } kind ? JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString()) : null,
            arguments = Cut(c.Arguments),
            refusal = c.RefusalKind is not null ? Cut(c.Result) : null,
        }),
        thinking = showThinking && response.ThinkingContent is not null ? new
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

    /// <summary><c>ok</c>, <c>failed</c>, <c>refused</c>, or <c>unknown</c> when the invoker did not report one.</summary>
    private static string OutcomeOf(ToolCallResult call) =>
        call.RefusalKind is not null ? "refused" : call.Success switch { true => "ok", false => "failed", null => "unknown" };

    private static string Cut(string text) => text.Length <= CallTextLimit ? text : string.Concat(text.AsSpan(0, CallTextLimit), "…");

    /// <summary>The <c>--json</c> document of a run that did not produce a turn (an error, or the timeout).</summary>
    public static string FailureJson(string stopReason, string error, long durationMs) => JsonSerializer.Serialize(new
    {
        error,
        stop_reason = stopReason,
        duration_ms = durationMs,
    }, JsonOptions);
}
