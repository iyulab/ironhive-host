using System.Collections.Concurrent;
using Ironbees.Core;
using IronHive.Agent.Delegation;
using IronHive.Agent.Invocation;
using IronHive.Agent.Ironbees;
using IronHive.Agent.Providers;
using IronHive.Agent.Tracking;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Infrastructure.Delegation;

/// <summary>
/// Builds the delegation tools of one session (<c>delegation:</c> in <c>config.yaml</c>): an Ironbees orchestrator over
/// the configured agents, whose runs call their tools through the session's own tool pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Approval path: the host builds one <see cref="ToolInvocationPipeline"/> (permission rules, planning mode, the human
/// approval prompt) and installs it on every session client; it is read back from the session client here. A delegated
/// run gets that same instance, so its tool calls are judged exactly like the session's. There is no fallback to a
/// default pipeline: one without the permission gate would let a delegated agent run anything.
/// </para>
/// <para>
/// Clients: the delegated agents run their own tool loop, so they need plain provider clients
/// (the <c>delegationClients</c> argument of <see cref="CreateToolsAsync"/>). A session client already runs tools itself; the agent runtime would run every
/// call a second time and ask for approval twice. The plain clients also carry no usage limiter — the delegation tools
/// charge the session budget for each run, and a second limiter would count the same tokens twice.
/// </para>
/// </remarks>
internal static class DelegationWiring
{
    /// <summary>The tools for the configured agents, or none when the section lists no agent.</summary>
    /// <param name="config">The <c>delegation</c> section.</param>
    /// <param name="workingDirectory">The session's working directory; a relative agents directory is taken from it.</param>
    /// <param name="sessionClient">The session's chat client, which carries the session's tool pipeline.</param>
    /// <param name="delegationClients">A factory of plain provider clients (no tool pipeline, no usage limiter).</param>
    /// <param name="toolPool">The tools a delegated agent may use when its <c>agent.yaml</c> lists none: the session's.</param>
    /// <param name="sessionModelId">The model of an agent whose configuration and <c>agent.yaml</c> name none.</param>
    /// <param name="usageLimiter">The session budget, or null.</param>
    /// <param name="cancellationToken">Cancels loading the agents.</param>
    public static async Task<IReadOnlyList<AITool>> CreateToolsAsync(
        DelegationHostConfig? config,
        string workingDirectory,
        IChatClient sessionClient,
        IChatClientFactory delegationClients,
        Func<IList<AITool>> toolPool,
        string? sessionModelId,
        IUsageLimiter? usageLimiter,
        CancellationToken cancellationToken)
    {
        if (config is not { Agents.Count: > 0 })
        {
            return [];
        }

        ArgumentNullException.ThrowIfNull(sessionClient);
        ArgumentNullException.ThrowIfNull(delegationClients);
        ArgumentNullException.ThrowIfNull(toolPool);

        var pipeline = sessionClient.GetService<ToolInvocationPipeline>()
            ?? throw new InvalidOperationException(
                "Delegation is configured, but the session's chat client carries no tool pipeline. A delegated run must " +
                "call its tools through the session's approval path; it is not run without it.");

        var agentsDirectory = Path.GetFullPath(Path.Combine(
            workingDirectory, string.IsNullOrWhiteSpace(config.AgentsDirectory) ? "agents" : config.AgentsDirectory));

        // One client per (provider, model) for the session: the runtime asks for a client per agent and per run.
        var clients = new ConcurrentDictionary<(string? Provider, string Model), DeferredChatClient>();
        IChatClient ClientFor(ModelConfig model)
        {
            var modelId = !string.IsNullOrWhiteSpace(model.Deployment) ? model.Deployment : sessionModelId;
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new InvalidOperationException(
                    "A delegated agent names no model, and the session's model is unknown. Set delegation.agents[].model.");
            }

            var provider = config.Agents.FirstOrDefault(a =>
                string.Equals(a.Model, modelId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(a.Provider))?.Provider;
            return clients.GetOrAdd((provider, modelId), key => new DeferredChatClient(
                ct => key.Provider is null
                    ? delegationClients.CreateAsync(key.Model, ct)
                    : delegationClients.CreateAsync(key.Provider, key.Model, ct),
                key.Model));
        }

        var adapter = new ChatClientFrameworkAdapter(ClientFor, toolPool, pipeline);
        var orchestrator = new AgentOrchestrator(
            new SelectedAgentsLoader([.. config.Agents.Select(a => a.Name)]),
            new AgentRegistry(),
            adapter,
            new KeywordAgentSelector(),
            agentsDirectory,
            conversationStore: null,
            defaultModelDeployment: sessionModelId);
        await orchestrator.LoadAgentsAsync(cancellationToken).ConfigureAwait(false);

        var defaults = new DelegationOptions();
        return [.. DelegationTools.Create(
            orchestrator,
            config.Agents.Select(a => new DelegatedAgent
            {
                AgentName = a.Name,
                Model = string.IsNullOrWhiteSpace(a.Model) ? null : a.Model,
                Description = string.IsNullOrWhiteSpace(a.Description) ? null : a.Description,
                MaxToolTurns = a.MaxToolTurns,
            }),
            new DelegationOptions
            {
                MaxDepth = config.MaxDepth > 0 ? config.MaxDepth : defaults.MaxDepth,
                MaxConcurrent = config.MaxConcurrent > 0 ? config.MaxConcurrent : defaults.MaxConcurrent,
                UsageLimiter = usageLimiter,
            })];
    }
}

/// <summary>The plain provider-client factory for delegated agents (see <see cref="DelegationWiring"/>).</summary>
internal sealed record DelegationClients(IChatClientFactory Factory);
