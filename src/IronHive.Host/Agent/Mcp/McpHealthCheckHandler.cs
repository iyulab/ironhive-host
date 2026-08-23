using Cronex;
using Cronex.Hosting;
using Microsoft.Extensions.Logging;

namespace IronHive.Agent.Mcp;

/// <summary>
/// Cronex trigger handler that checks connected MCP plugin health and reports failures
/// back to the owning <see cref="McpHealthCheckService"/>.
/// </summary>
internal sealed class McpHealthCheckHandler : ICronexHandler
{
    private readonly McpHealthCheckService _owner;
    private readonly IMcpPluginManager _pluginManager;
    private readonly ILogger? _logger;

    public McpHealthCheckHandler(
        McpHealthCheckService owner,
        IMcpPluginManager pluginManager,
        ILogger? logger = null)
    {
        _owner = owner;
        _pluginManager = pluginManager;
        _logger = logger;
    }

    public async Task HandleAsync(TriggerContext context, CancellationToken cancellationToken)
    {
        foreach (var pluginName in _pluginManager.ConnectedPlugins)
        {
            var healthy = await _pluginManager.IsHealthyAsync(pluginName, cancellationToken);
            if (!healthy)
            {
#pragma warning disable CA1848
                _logger?.LogWarning("MCP plugin '{PluginName}' health check failed", pluginName);
#pragma warning restore CA1848
                _owner.RaisePluginUnhealthy(pluginName);
            }
        }
    }
}
