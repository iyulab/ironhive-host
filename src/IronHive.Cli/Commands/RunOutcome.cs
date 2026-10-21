using System.Net;
using System.Text.Json;
using IronHive.Abstractions.Exceptions;
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

    /// <summary>
    /// The <c>error_kind</c> of an error the model provider caused: no response (connection, TLS, a body cut short), a server
    /// error status (5xx, 408), a rate limit or billing refusal, or an error the server sent inside the stream. Running the
    /// same prompt again later can succeed — a harness counts such a run as the environment's failure, not the agent's.
    /// </summary>
    public const string ProviderErrorKind = "provider";

    /// <summary>
    /// The <c>error_kind</c> of every other error: the run itself failed (configuration, a request the provider refused for
    /// what it asked — other 4xx — a context overflow, a defect).
    /// </summary>
    public const string RunErrorKind = "run";

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
    public static string Json(RunReport report, long durationMs, bool showThinking)
        => Document(error: null, errorKind: null, ReasonOf(report.StopReason ?? TurnStopReason.Completed), report, durationMs, showThinking);

    /// <summary>
    /// <see cref="ProviderErrorKind"/> when <paramref name="error"/> (or an exception it wraps) is the provider's failure,
    /// otherwise <see cref="RunErrorKind"/>.
    /// </summary>
    public static string ErrorKindOf(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AggregateException aggregate:
                    return aggregate.InnerExceptions.Any(e => ErrorKindOf(e) == ProviderErrorKind) ? ProviderErrorKind : RunErrorKind;
                case ContextOverflowException:
                    return RunErrorKind;
                case RateLimitException or BillingException or ProviderResponseException or HttpIOException:
                    return ProviderErrorKind;
                case HttpRequestException http:
                    // No status = no response (connection, TLS); a 4xx other than 408/429 refused what the run asked.
                    return http.StatusCode is not { } status || (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                        ? ProviderErrorKind
                        : RunErrorKind;
            }
        }

        return RunErrorKind;
    }

    /// <summary>
    /// The <c>--json</c> document of a turn cut short — by <c>--timeout</c>, the caller or an error — with the work it did
    /// before it was cut: the text streamed so far and every call that started, a call that never reported back as
    /// <c>unfinished</c>. Without it a timed-out run is only «it did not finish», and nothing it did can be read.
    /// </summary>
    public static string CutShortJson(string stopReason, string error, RunReport soFar, long durationMs, bool showThinking)
        => Document(error, errorKind: null, stopReason, soFar, durationMs, showThinking);

    /// <summary>The <c>--json</c> document of a turn an error cut short, with its <c>error_kind</c>.</summary>
    public static string ErrorJson(Exception error, RunReport soFar, long durationMs, bool showThinking)
        => Document(error.Message, ErrorKindOf(error), ErrorReason, soFar, durationMs, showThinking);

    private static string Document(string? error, string? errorKind, string stopReason, RunReport report, long durationMs, bool showThinking)
    {
        var unfinished = report.Unfinished ?? [];
        var calls = report.ToolCalls;
        return JsonSerializer.Serialize(new
        {
            error,
            error_kind = errorKind,
            content = report.Content,
            stop_reason = stopReason,
            duration_ms = durationMs,
            tool_calls = calls.Count,
            refused_tool_calls = calls.Count(c => c.RefusalKind is not null),
            failed_tool_calls = calls.Count(c => c.Success == false && c.RefusalKind is null),
            // In call order, so a reader can see which call a guard refused and what it said — a `tool_terminated` run is
            // otherwise only a count. Arguments are cut to CallTextLimit (a write's content can be the whole file); a tool's
            // own result is left out, a refusal's message kept.
            calls = calls.Select(c => new
            {
                tool = c.ToolName,
                outcome = c.CallId is { } id && unfinished.Contains(id) ? "unfinished" : OutcomeOf(c),
                refusal_kind = c.RefusalKind is { } kind ? JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString()) : null,
                arguments = Cut(c.Arguments),
                refusal = c.RefusalKind is not null ? Cut(c.Result) : null,
            }),
            thinking = showThinking && report.ThinkingContent is not null ? new
            {
                content = report.ThinkingContent.Content,
                token_count = report.ThinkingContent.TokenCount
            } : null,
            usage = report.Usage is not null ? new
            {
                input_tokens = report.Usage.InputTokens,
                output_tokens = report.Usage.OutputTokens,
                total_tokens = report.Usage.TotalTokens
            } : null
        }, JsonOptions);
    }

    /// <summary><c>ok</c>, <c>failed</c>, <c>refused</c>, or <c>unknown</c> when the invoker did not report one.</summary>
    private static string OutcomeOf(ToolCallResult call) =>
        call.RefusalKind is not null ? "refused" : call.Success switch { true => "ok", false => "failed", null => "unknown" };

    private static string Cut(string text) => text.Length <= CallTextLimit ? text : string.Concat(text.AsSpan(0, CallTextLimit), "…");

    /// <summary>The <c>--json</c> document of a run that did not produce a turn (an error, or the timeout).</summary>
    public static string FailureJson(string stopReason, string error, long durationMs) => JsonSerializer.Serialize(new
    {
        error,
        error_kind = stopReason == ErrorReason ? RunErrorKind : null,
        stop_reason = stopReason,
        duration_ms = durationMs,
    }, JsonOptions);
}
