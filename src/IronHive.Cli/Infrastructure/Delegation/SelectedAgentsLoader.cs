using Ironbees.Core;

namespace IronHive.Cli.Infrastructure.Delegation;

/// <summary>
/// Loads only the agents the configuration names, each from its own directory. The orchestrator fails as a whole when
/// any agent it loads fails, so an unrelated broken agent in the same directory must not take delegation down.
/// </summary>
internal sealed class SelectedAgentsLoader : IAgentLoader
{
    private readonly IAgentLoader _inner;
    private readonly IReadOnlyList<string> _names;

    public SelectedAgentsLoader(IReadOnlyList<string> names, IAgentLoader? inner = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        _names = names;
        _inner = inner ?? new FileSystemAgentLoader();
    }

    public Task<AgentConfig> LoadConfigAsync(string agentPath, CancellationToken cancellationToken = default) =>
        _inner.LoadConfigAsync(agentPath, cancellationToken);

    public async Task<IReadOnlyList<AgentConfig>> LoadAllConfigsAsync(
        string? agentsDirectory = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentsDirectory);
        var configs = new List<AgentConfig>(_names.Count);
        foreach (var name in _names)
        {
            var path = Path.Combine(agentsDirectory, name);
            if (!Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Delegation lists the agent '{name}', but '{path}' does not exist. Add {name}/agent.yaml under the " +
                    "agents directory, or remove it from delegation.agents.");
            }

            configs.Add(await _inner.LoadConfigAsync(path, cancellationToken).ConfigureAwait(false));
        }

        return configs;
    }

    public Task<bool> ValidateAgentDirectoryAsync(string agentPath, CancellationToken cancellationToken = default) =>
        _inner.ValidateAgentDirectoryAsync(agentPath, cancellationToken);
}
