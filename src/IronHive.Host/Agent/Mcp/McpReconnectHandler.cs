using Cronex;
using Cronex.Hosting;
using Microsoft.Extensions.Logging;

namespace IronHive.Agent.Mcp;

/// <summary>
/// Cronex trigger handler that gives the plugins which are wanted but not connected another try
/// (<see cref="IMcpPluginManager.ReconnectPendingAsync"/>, which keeps its own backoff, so a tick that finds nothing due costs
/// nothing). The tools of a plugin that connects reach the running loops through <see cref="McpToolSynchronizer"/>.
/// </summary>
internal sealed partial class McpReconnectHandler : ICronexHandler
{
    private readonly IMcpPluginManager _pluginManager;
    private readonly ILogger? _logger;

    public McpReconnectHandler(IMcpPluginManager pluginManager, ILogger? logger = null)
    {
        _pluginManager = pluginManager;
        _logger = logger;
    }

    public async Task HandleAsync(TriggerContext context, CancellationToken cancellationToken)
    {
        var connected = await _pluginManager.ReconnectPendingAsync(cancellationToken);
        foreach (var name in connected)
        {
            LogConnectedLater(_logger, name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP plugin '{PluginName}' connected on a later attempt")]
    private static partial void LogConnectedLater(ILogger? logger, string pluginName);
}
