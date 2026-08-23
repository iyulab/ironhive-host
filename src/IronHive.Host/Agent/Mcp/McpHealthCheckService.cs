using Cronex.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IronHive.Agent.Mcp;

/// <summary>
/// Periodically checks the health of connected MCP plugins via a Cronex-hosted trigger
/// (<see cref="Cronex.Hosting.ICronexHandler"/>/<c>CronexBackgroundService</c>).
/// Raises <see cref="PluginUnhealthy"/> when a plugin fails to respond.
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
        ILogger? logger = null)
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

        services.AddCronex(b => b.AddTrigger<McpHealthCheckHandler>("mcp:health-check", expression));

        _provider = services.BuildServiceProvider();
        _hostedService = _provider.GetServices<IHostedService>().Single();
    }

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
