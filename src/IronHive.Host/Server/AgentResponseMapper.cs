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

            if (chunk.ToolCallDelta?.NameDelta is not null)
            {
                yield return new ToolStartEvent(chunk.ToolCallDelta.NameDelta, CallId: chunk.ToolCallDelta.Id);
            }
        }
    }
}
