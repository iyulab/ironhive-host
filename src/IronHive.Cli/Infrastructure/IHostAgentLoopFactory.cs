using IronHive.Agent.Loop;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// The host's loop factory: an <see cref="IAgentLoopFactory"/> that also hands back the tool pipeline the loop's chat
/// client runs calls through. The registered tools are on the loop itself (<see cref="IAgentLoop.Tools"/>).
/// </summary>
public interface IHostAgentLoopFactory : IAgentLoopFactory
{
    /// <summary>
    /// Creates a loop and returns it together with its tool pipeline.
    /// </summary>
    Task<CreatedAgentLoop> CreateWithPipelineAsync(AgentLoopFactoryOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// A loop the host created. Its registered tools are <see cref="IAgentLoop.Tools"/> on <see cref="Loop"/>.
/// </summary>
public sealed record CreatedAgentLoop(IAgentLoop Loop)
{
    /// <summary>
    /// The tool pipeline the loop's chat client runs every call through, or null when it has none. A host that runs a
    /// call itself - a call a previous process left waiting for approval - runs it through this.
    /// </summary>
    public IronHive.Agent.Invocation.ToolInvocationPipeline? Pipeline { get; init; }
}
