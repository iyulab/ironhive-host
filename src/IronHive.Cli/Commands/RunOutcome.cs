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
/// limit, or a guard that ended the turn) · <see cref="Filtered"/> 3 (the provider's content filter).
/// </remarks>
internal static class RunOutcome
{
    public const int Completed = 0;
    public const int Error = 1;
    public const int Incomplete = 2;
    public const int Filtered = 3;

    /// <summary>The <c>stop_reason</c> value of a run that hit <c>--timeout</c>.</summary>
    public const string Timeout = "timeout";

    /// <summary>The <c>stop_reason</c> value of a run that failed with an error.</summary>
    public const string ErrorReason = "error";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

    /// <summary>The <c>--json</c> document of a run that did not produce a turn (an error, or the timeout).</summary>
    public static string FailureJson(string stopReason, string error, long durationMs) => JsonSerializer.Serialize(new
    {
        error,
        stop_reason = stopReason,
        duration_ms = durationMs,
    }, JsonOptions);
}
