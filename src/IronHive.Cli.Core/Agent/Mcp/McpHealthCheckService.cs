using Chronex;
using Microsoft.Extensions.Logging;

namespace IronHive.Agent.Mcp;

/// <summary>
/// Periodically checks the health of connected MCP plugins using a Chronex scheduler.
/// Raises <see cref="PluginUnhealthy"/> when a plugin fails to respond.
/// </summary>
public sealed class McpHealthCheckService : IAsyncDisposable
{
    private readonly IMcpPluginManager _pluginManager;
    private readonly ChronexScheduler _scheduler;
    private readonly ILogger? _logger;

    /// <summary>
    /// Raised when a plugin health check fails.
    /// </summary>
    public event EventHandler<McpPluginEventArgs>? PluginUnhealthy;

    public McpHealthCheckService(
        IMcpPluginManager pluginManager,
        string expression = "@every 5m",
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        _pluginManager = pluginManager;
        _scheduler = new ChronexScheduler(timeProvider);
        _logger = logger;
        _scheduler.Register("mcp:health-check", expression, CheckHealthAsync);
    }

    /// <summary>
    /// Starts the periodic health check scheduler.
    /// </summary>
    public void Start() => _scheduler.Start();

    private async Task CheckHealthAsync(TriggerContext context, CancellationToken ct)
    {
        foreach (var pluginName in _pluginManager.ConnectedPlugins)
        {
            try
            {
                // Health check by attempting to list tools — timeout indicates unhealthy
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                await _pluginManager.GetToolsAsync(pluginName, cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException)
            {
#pragma warning disable CA1848
                _logger?.LogWarning("MCP plugin '{PluginName}' health check failed: {Message}", pluginName, ex.Message);
#pragma warning restore CA1848
                PluginUnhealthy?.Invoke(this, new McpPluginEventArgs { PluginName = pluginName });
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync();
    }
}
