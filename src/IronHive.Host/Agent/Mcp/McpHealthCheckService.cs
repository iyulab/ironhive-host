using Cronex.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IronHive.Agent.Mcp;

/// <summary>
/// Keeps MCP plugins connected, on Cronex-hosted triggers (<see cref="Cronex.Hosting.ICronexHandler"/>/<c>CronexBackgroundService</c>):
/// one checks the health of the connected plugins - a plugin that fails to respond raises <see cref="PluginUnhealthy"/> and is
/// connected again - and a quicker one tries the plugins that could not be connected (start-up failures, lost connections).
/// </summary>
public sealed class McpHealthCheckService : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHostedService _hostedService;

    /// <summary>
    /// Raised when a plugin health check fails.
    /// </summary>
    public event EventHandler<McpPluginEventArgs>? PluginUnhealthy;

    public McpHealthCheckService(
        IMcpPluginManager pluginManager,
        string expression = "@every 5m",
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        string reconnectExpression = "@every 30s")
    {
        var services = new ServiceCollection();
        services.AddSingleton(pluginManager);
        services.AddSingleton(this);
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }
        if (logger is not null)
        {
            services.AddSingleton(logger);
        }
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        services.AddCronex(b => b
            .AddTrigger<McpHealthCheckHandler>("mcp:health-check", expression)
            .AddTrigger<McpReconnectHandler>("mcp:reconnect-pending", reconnectExpression));

        _provider = services.BuildServiceProvider();
        _hostedService = _provider.GetServices<IHostedService>().Single();
    }

    /// <summary>
    /// Whether there is anything to keep connected: a connected plugin to check, or a plugin that could not be connected
    /// and is waiting for another try (<see cref="IMcpPluginManager.PendingPlugins"/>).
    /// </summary>
    public static bool IsNeeded(IMcpPluginManager? pluginManager) =>
        pluginManager is not null && (pluginManager.ConnectedPlugins.Count > 0 || pluginManager.PendingPlugins.Count > 0);

    /// <summary>
    /// Starts the periodic health check scheduler.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _hostedService.StartAsync(cancellationToken);

    /// <summary>
    /// Invoked by <see cref="McpHealthCheckHandler"/> when a connected plugin fails its health check.
    /// </summary>
    internal void RaisePluginUnhealthy(string pluginName) =>
        PluginUnhealthy?.Invoke(this, new McpPluginEventArgs { PluginName = pluginName });

    public async ValueTask DisposeAsync()
    {
        await _hostedService.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }
}
