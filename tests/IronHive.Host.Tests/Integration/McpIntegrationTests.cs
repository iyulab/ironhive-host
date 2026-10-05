using System.Text.Json;
using IronHive.Agent.Mcp;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tests.Integration;

/// <summary>
/// Integration tests for the MCP Plugin System.
/// Tests the complete workflow without requiring actual MCP servers.
/// </summary>
public class McpIntegrationTests
{
    #region Scenario 1: Plugin Configuration and Discovery

    [Fact]
    public async Task Scenario1_PluginConfigurationAndDiscovery()
    {
        // Arrange: Create configuration with multiple plugins
        var config = new McpPluginsConfig
        {
            Plugins = new Dictionary<string, McpPluginConfig>
            {
                ["memory"] = new McpPluginConfig
                {
                    Command = "memory-indexer",
                    Arguments = ["--port", "8080"],
                    AutoReconnect = true,
                    TimeoutMs = 30000
                },
                ["code"] = new McpPluginConfig
                {
                    Command = "code-beaker",
                    Arguments = ["--sandbox"],
                    AutoReconnect = true
                },
                ["disabled"] = new McpPluginConfig
                {
                    Command = "disabled-plugin"
                }
            },
            ExcludePlugins = ["disabled"],
            AutoConnect = true,
            DefaultTimeoutMs = 45000
        };

        await using var manager = new McpPluginManager();
        using var discovery = new McpToolDiscovery(manager, config);

        // Act: Get available plugins
        var availablePlugins = discovery.GetAvailablePlugins();

        // Assert: Verify plugin discovery
        Assert.Equal(3, availablePlugins.Count);

        var memoryPlugin = availablePlugins.First(p => p.Name == "memory");
        Assert.False(memoryPlugin.IsConnected);
        Assert.False(memoryPlugin.IsExcluded);
        Assert.Equal(McpTransportType.Stdio, memoryPlugin.Transport);

        var disabledPlugin = availablePlugins.First(p => p.Name == "disabled");
        Assert.True(disabledPlugin.IsExcluded);
    }

    [Fact]
    public async Task Scenario1_ConfigurationFileLoading()
    {
        // Arrange: Create temp config files
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var ironhiveDir = Path.Combine(tempDir, ".ironhive");
        Directory.CreateDirectory(ironhiveDir);

        var yamlPath = Path.Combine(ironhiveDir, "plugins.yaml");
        var yamlContent = """
            plugins:
              memory:
                command: memory-indexer
                arguments:
                  - --port
                  - "8080"
              code:
                command: code-beaker
            autoConnect: true
            defaultTimeoutMs: 30000
            """;
        await File.WriteAllTextAsync(yamlPath, yamlContent, TestContext.Current.CancellationToken);

        try
        {
            // Act: Load configuration
            var config = McpPluginsConfigLoader.LoadFromDefault(tempDir);

            // Assert: Verify loaded configuration
            Assert.Equal(2, config.Plugins.Count);
            Assert.True(config.Plugins.ContainsKey("memory"));
            Assert.True(config.Plugins.ContainsKey("code"));
            Assert.Equal("memory-indexer", config.Plugins["memory"].Command);
            Assert.True(config.AutoConnect);
            Assert.Equal(30000, config.DefaultTimeoutMs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Scenario 2: Hot Reload Workflow

    [Fact]
    public async Task Scenario2_HotReloadWorkflow()
    {
        // Arrange: Initial configuration
        var initialConfig = new McpPluginsConfig
        {
            Plugins = new Dictionary<string, McpPluginConfig>
            {
                ["plugin-a"] = new McpPluginConfig { Command = "plugin-a" }
            },
            AutoConnect = false
        };

        await using var manager = new McpPluginManager();
        await using var reloader = new McpPluginHotReloader(
            manager, initialConfig, enableFileWatcher: false);

        var reloadEvents = new List<PluginReloadEventArgs>();
        reloader.PluginsReloaded += (_, args) => reloadEvents.Add(args);

        // Act: Reload with new configuration
        var newConfig = new McpPluginsConfig
        {
            Plugins = new Dictionary<string, McpPluginConfig>
            {
                ["plugin-a"] = new McpPluginConfig { Command = "plugin-a-v2" },
                ["plugin-b"] = new McpPluginConfig { Command = "plugin-b" }
            },
            AutoConnect = false
        };

        await reloader.ReloadAsync(newConfig, TestContext.Current.CancellationToken);

        // Assert: Verify reload event
        Assert.Single(reloadEvents);
        Assert.Same(newConfig, reloader.CurrentConfig);
    }

    [Fact]
    public async Task Scenario2_ExcludeIncludeAtRuntime()
    {
        // Arrange
        var config = new McpPluginsConfig
        {
            Plugins = new Dictionary<string, McpPluginConfig>
            {
                ["plugin-a"] = new McpPluginConfig { Command = "a" },
                ["plugin-b"] = new McpPluginConfig { Command = "b" }
            },
            AutoConnect = false
        };

        await using var manager = new McpPluginManager();
        await using var reloader = new McpPluginHotReloader(
            manager, config, enableFileWatcher: false);

        // Act & Assert: Exclude plugin
        await reloader.ExcludePluginAsync("plugin-a", TestContext.Current.CancellationToken);
        Assert.Contains("plugin-a", reloader.ExcludedPlugins);

        // Act & Assert: Include plugin back
        await reloader.IncludePluginAsync("plugin-a", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("plugin-a", reloader.ExcludedPlugins);
    }

    #endregion

    #region Scenario 3: Memory Tools Integration

    [Fact]
    public async Task Scenario9_PluginManagerDisposal()
    {
        // Arrange
        var manager = new McpPluginManager();

        // Act: Dispose manager
        await manager.DisposeAsync();

        // Assert: Operations should throw after disposal
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.GetToolsAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.ConnectAsync("test", new McpPluginConfig { Command = "test" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Scenario9_HotReloaderDisposal()
    {
        // Arrange
        var manager = new McpPluginManager();
        var config = new McpPluginsConfig();
        var reloader = new McpPluginHotReloader(manager, config, enableFileWatcher: false);

        // Act: Dispose reloader
        await reloader.DisposeAsync();

        // Assert: Operations should throw after disposal
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reloader.InitializeAsync(TestContext.Current.CancellationToken));

        // Cleanup
        await manager.DisposeAsync();
    }

    #endregion
}
