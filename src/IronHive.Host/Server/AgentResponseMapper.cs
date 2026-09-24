using System.Runtime.CompilerServices;
using IronHive.Agent.Loop;

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
        var hasUsage = false;
        // Outcomes already relayed as they arrived; the final turn record repeats them (built by the same rule).
        var relayed = new List<ToolCallResult>();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            if (logger is not null)
            {
                await logger.ProcessChunkAsync(chunk);
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

            if (chunk.ToolCallDelta?.NameDelta is not null)
            {
                yield return new ToolStartEvent(chunk.ToolCallDelta.NameDelta, CallId: chunk.ToolCallDelta.Id);
            }

            // A tool's outcome, the moment it arrives — a client's step timeline shows each result as the tool
            // finishes instead of all of them when the turn ends.
            if (chunk.ToolResult is { Success: { } arrivedSuccess } arrived)
            {
                relayed.Add(arrived);
                yield return new ToolEndEvent(arrived.ToolName, arrivedSuccess, arrived.Result, arrived.CallId);
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
                        yield return new ToolEndEvent(call.ToolName, success, call.Result, call.CallId);
                    }
                }
            }

            if (chunk.Usage is not null)
            {
                // Each round-trip's final chunk carries that round-trip's own usage — sum
                // across every round-trip in the turn (tool-calling turns make several).
                inputTokens += chunk.Usage.InputTokens;
                outputTokens += chunk.Usage.OutputTokens;
                hasUsage = true;
            }
        }

        yield return hasUsage
            ? new TurnEndEvent(inputTokens, outputTokens)
            : new TurnEndEvent();
    }
}
