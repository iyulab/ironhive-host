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
