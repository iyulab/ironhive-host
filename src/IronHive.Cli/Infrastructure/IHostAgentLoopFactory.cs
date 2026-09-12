using IronHive.Agent.Loop;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// The host's loop factory: an <see cref="IAgentLoopFactory"/> that also hands back the tools it
/// registered on the loop, so the host can resolve a request's per-turn tool names against them
/// (the loop itself does not expose its tool list).
/// </summary>
public interface IHostAgentLoopFactory : IAgentLoopFactory
{
    /// <summary>
    /// Creates a loop and returns it together with the tools registered on it.
    /// </summary>
    Task<CreatedAgentLoop> CreateWithToolsAsync(AgentLoopFactoryOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// A loop the host created, with the tools it registered on it.
/// </summary>
public sealed record CreatedAgentLoop(IAgentLoop Loop, IReadOnlyList<AITool> Tools);
