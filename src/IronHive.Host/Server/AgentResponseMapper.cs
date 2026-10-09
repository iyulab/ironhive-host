using System.Runtime.CompilerServices;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;

using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// Extension methods that convert <see cref="AgentResponseChunk"/> streams
/// into <see cref="ServerEvent"/> streams, optionally recording to an <see cref="IExecutionLogger"/>.
/// </summary>
public static class AgentResponseMapper
{
    /// <summary>
    /// Transforms an async stream of agent response chunks into server events.
    /// If a logger is provided, tool calls are recorded automatically.
    /// </summary>
    public static async IAsyncEnumerable<ServerEvent> ToServerEvents(
        this IAsyncEnumerable<AgentResponseChunk> chunks,
        IExecutionLogger? logger = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        long cachedInputTokens = 0;
        var hasUsage = false;
        TurnStopReason? stopReason = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Outcomes already relayed as they arrived; the final turn record repeats them (built by the same rule).
        var relayed = new List<ToolCallResult>();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            if (logger is not null)
            {
                await logger.ProcessChunkAsync(chunk, cancellationToken: ct);
            }

            if (chunk.TextDelta is not null)
            {
                yield return new TextDeltaEvent(chunk.TextDelta);
            }

            // The loop streams extended-thinking content on its own field and the protocol has an
            // event for it; the two were never connected, so a client with a thinking pane got
            // nothing while the console path rendered it.
            if (chunk.ThinkingDelta is not null)
            {
                yield return new ThinkingDeltaEvent(chunk.ThinkingDelta);
            }

            // An observer's note rides on the final chunk as its own field; relay it as its own event
            // so the client can tell it from the model's text.
            if (chunk.Addendum is not null)
            {
                yield return new AddendumEvent(chunk.Addendum);
            }

            // One start per call: argument fragments of a call still being written (IsComplete false, when the loop
            // streams tool arguments) are progress, and the complete call that follows names the tool again.
            if (chunk.ToolCallDelta is { IsComplete: true, NameDelta: { } toolName } started)
            {
                yield return new ToolStartEvent(toolName, CallId: started.Id);
            }

            // A tool's outcome, the moment it arrives — a client's step timeline shows each result as the tool
            // finishes instead of all of them when the turn ends.
            if (chunk.ToolResult is { Success: { } arrivedSuccess } arrived)
            {
                relayed.Add(arrived);
                yield return new ToolEndEvent(arrived.ToolName, arrivedSuccess, arrived.Result, arrived.CallId)
                {
                    Refusal = arrived.RefusalKind is { } refused ? WireName(refused) : null,
                };
            }

            // The final chunk carries the turn's consolidated tool outcomes. Relay each one whose outcome is known
            // and that did not already arrive on its own chunk — including a permission gate's refusal
            // (Success = false) — so a client sees what every tool returned, not only that it started.
            if (chunk.Turn is not null)
            {
                foreach (var call in chunk.Turn.ToolCalls)
                {
                    if (relayed.Remove(call))
                    {
                        continue;
                    }
                    if (call.Success is { } success)
                    {
                        yield return new ToolEndEvent(call.ToolName, success, call.Result, call.CallId)
                        {
                            Refusal = call.RefusalKind is { } refused ? WireName(refused) : null,
                        };
                    }
                }
            }

            if (chunk.Usage is not null)
            {
                // Each round-trip's final chunk carries that round-trip's own usage — sum
                // across every round-trip in the turn (tool-calling turns make several).
                inputTokens += chunk.Usage.InputTokens;
                outputTokens += chunk.Usage.OutputTokens;
                cachedInputTokens += chunk.Usage.CachedInputTokens;
                hasUsage = true;
            }

            if (chunk.Turn is not null)
            {
                stopReason = chunk.Turn.StopReason;
            }
        }

        var end = hasUsage ? new TurnEndEvent(inputTokens, outputTokens) : new TurnEndEvent();
        yield return end with
        {
            CachedInputTokens = hasUsage && cachedInputTokens > 0 ? cachedInputTokens : null,
            StopReason = stopReason is { } reason ? WireName(reason) : null,
            DurationMs = clock.ElapsedMilliseconds,
        };
    }

    /// <summary>The protocol spelling of a stop reason (snake_case, stable across renames of the enum).</summary>
    internal static string WireName(TurnStopReason reason) => reason switch
    {
        TurnStopReason.Completed => StopReasons.Completed,
        TurnStopReason.OutputLimit => StopReasons.OutputLimit,
        TurnStopReason.ContentFilter => StopReasons.ContentFilter,
        TurnStopReason.ToolTerminated => StopReasons.ToolTerminated,
        TurnStopReason.AwaitingHostTools => StopReasons.AwaitingHostTools,
        TurnStopReason.StepLimit => StopReasons.StepLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A stop reason without a wire name."),
    };

    /// <summary>The protocol spelling of a tool refusal (<see cref="ToolRefusalCodes"/>).</summary>
    internal static string WireName(ToolCallRefusalKind kind) => kind switch
    {
        ToolCallRefusalKind.Denied => ToolRefusalCodes.Denied,
        ToolCallRefusalKind.ApprovalUnavailable => ToolRefusalCodes.ApprovalUnavailable,
        ToolCallRefusalKind.Rejected => ToolRefusalCodes.Rejected,
        ToolCallRefusalKind.ResultWithheld => ToolRefusalCodes.ResultWithheld,
        ToolCallRefusalKind.InvalidArguments => ToolRefusalCodes.InvalidArguments,
        ToolCallRefusalKind.RepeatedCall => ToolRefusalCodes.RepeatedCall,
        ToolCallRefusalKind.RepeatedError => ToolRefusalCodes.RepeatedError,
        ToolCallRefusalKind.RepeatedResult => ToolRefusalCodes.RepeatedResult,
        ToolCallRefusalKind.TimedOut => ToolRefusalCodes.TimedOut,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A tool refusal without a wire name."),
    };
}
