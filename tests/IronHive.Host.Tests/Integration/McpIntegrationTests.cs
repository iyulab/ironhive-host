using System.Text.Json;
using IronHive.Agent.Mcp;
using IronHive.Host.Integration;
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
        await File.WriteAllTextAsync(yamlPath, yamlContent);

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

        await reloader.ReloadAsync(newConfig);

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
        await reloader.ExcludePluginAsync("plugin-a");
        Assert.Contains("plugin-a", reloader.ExcludedPlugins);

        // Act & Assert: Include plugin back
        await reloader.IncludePluginAsync("plugin-a");
        Assert.DoesNotContain("plugin-a", reloader.ExcludedPlugins);
    }

    #endregion

    #region Scenario 3: Memory Tools Integration

    [Fact]
    public async Task Scenario3_MemoryToolsWorkflow()
    {
        // Arrange: Create memory provider
        var provider = new InMemoryToolsProvider();

        // Act: Store memories with different importance levels
        var storeResult1 = await provider.StoreAsync(
            "test-user",
            "User prefers dark mode UI",
            0.9f,
            "preferences");

        var storeResult2 = await provider.StoreAsync(
            "test-user",
            "Temporary note about meeting",
            0.2f,
            "notes");

        // Assert: Verify tier assignment
        Assert.True(storeResult1.Success);
        Assert.Equal("Archive", storeResult1.Tier); // High importance

        Assert.True(storeResult2.Success);
        Assert.Equal("Short", storeResult2.Tier); // Low importance

        // Act: Recall memories
        var recallResult = await provider.RecallAsync(
            "test-user",
            "dark mode",
            5);

        // Assert: Verify recall finds relevant memory
        Assert.True(recallResult.Success);
        Assert.Equal(1, recallResult.Count);
        Assert.Contains("dark mode", recallResult.Memories![0].Content);

        // Act: Search with category filter
        var searchResult = await provider.SearchAsync(
            "test-user",
            new MemorySearchOptions { Category = "preferences" });

        // Assert: Verify search filters correctly
        Assert.True(searchResult.Success);
        Assert.Equal(1, searchResult.Count);
        Assert.Equal("preferences", searchResult.Memories![0].Category);
    }

    [Fact]
    public async Task Scenario3_MemoryTierProgression()
    {
        // Arrange
        var provider = new InMemoryToolsProvider();

        // Act: Store memories with various importance levels
        var buffer = await provider.StoreAsync("user1", "Buffer tier", 0.1f, null);
        var shortTerm = await provider.StoreAsync("user1", "Short tier", 0.3f, null);
        var longTerm = await provider.StoreAsync("user1", "Long tier", 0.6f, null);
        var archive = await provider.StoreAsync("user1", "Archive tier", 0.9f, null);

        // Assert: Verify tier assignments
        Assert.Equal("Buffer", buffer.Tier);
        Assert.Equal("Short", shortTerm.Tier);
        Assert.Equal("Long", longTerm.Tier);
        Assert.Equal("Archive", archive.Tier);

        // Verify search by tier
        var archiveSearch = await provider.SearchAsync("user1",
            new MemorySearchOptions { Tier = "Archive" });
        Assert.Equal(1, archiveSearch.Count);

        var allSearch = await provider.SearchAsync("user1",
            new MemorySearchOptions { Limit = 10 });
        Assert.Equal(4, allSearch.Count);
    }

    [Fact]
    public async Task Scenario3_MemoryForgetWorkflow()
    {
        // Arrange
        var provider = new InMemoryToolsProvider();

        var storeResult = await provider.StoreAsync(
            "user1",
            "Sensitive information to forget",
            0.5f,
            null);
        var memoryId = storeResult.MemoryId!;

        // Act: Forget the memory
        var forgetResult = await provider.ForgetAsync("user1", memoryId);

        // Assert: Verify forget succeeded
        Assert.True(forgetResult.Success);
        Assert.Equal("Memory forgotten", forgetResult.Message);

        // Verify memory is gone
        var searchResult = await provider.SearchAsync("user1", new MemorySearchOptions());
        Assert.Empty(searchResult.Memories!);
    }

    #endregion

    #region Scenario 4: Code Execution Integration

    [Fact]
    public async Task Scenario4_CodeExecutionWorkflow()
    {
        // Arrange
        var provider = new InMemoryCodeExecutionProvider();

        // Act: Create a session
        var createResult = await provider.CreateSessionAsync("javascript");
        Assert.True(createResult.Success);
        Assert.NotNull(createResult.SessionId);
        var sessionId = createResult.SessionId;

        // Act: Execute code in session
        var executeResult = await provider.ExecuteAsync(
            "console.log('Hello, World!')",
            "javascript",
            sessionId,
            30);

        // Assert: Verify execution
        Assert.True(executeResult.Success);
        Assert.Equal("Hello, World!", executeResult.Stdout);

        // Act: List sessions
        var listResult = await provider.ListSessionsAsync();
        Assert.True(listResult.Success);
        Assert.Single(listResult.Sessions!);

        // Act: Destroy session
        var destroyResult = await provider.DestroySessionAsync(sessionId);
        Assert.True(destroyResult.Success);

        // Verify session is gone
        var listAfter = await provider.ListSessionsAsync();
        Assert.Empty(listAfter.Sessions!);
    }

    [Fact]
    public async Task Scenario4_MultiLanguageExecution()
    {
        // Arrange
        var provider = new InMemoryCodeExecutionProvider();

        // Act & Assert: JavaScript
        var jsResult = await provider.ExecuteAsync(
            "console.log('JavaScript output')",
            "javascript",
            null,
            30);
        Assert.Equal("JavaScript output", jsResult.Stdout);

        // Act & Assert: Python
        var pyResult = await provider.ExecuteAsync(
            "print('Python output')",
            "python",
            null,
            30);
        Assert.Equal("Python output", pyResult.Stdout);

        // Act & Assert: Unsupported language
        var unsupportedResult = await provider.ExecuteAsync(
            "code",
            "ruby",
            null,
            30);
        Assert.Contains("Unsupported language", unsupportedResult.Stdout);
    }

    [Fact]
    public async Task Scenario4_PackageInstallation()
    {
        // Arrange
        var provider = new InMemoryCodeExecutionProvider();
        var session = await provider.CreateSessionAsync("javascript");

        // Act: Install packages
        var installResult = await provider.InstallPackagesAsync(
            session.SessionId!,
            ["lodash", "axios", "moment"]);

        // Assert
        Assert.True(installResult.Success);
        Assert.Equal(3, installResult.InstalledPackages!.Count);
        Assert.Contains("lodash", installResult.InstalledPackages);
    }

    #endregion

    #region Scenario 5: Combined Agent Workflow Simulation

    [Fact]
    public async Task Scenario5_CombinedAgentWorkflow()
    {
        // Simulate an agent using both memory and code tools together

        // Setup providers
        var memoryProvider = new InMemoryToolsProvider();
        var codeProvider = new InMemoryCodeExecutionProvider();

        // Step 1: Execute code
        var codeResult = await codeProvider.ExecuteAsync(
            "console.log('Calculation result: 42')",
            "javascript",
            null,
            30);
        var codeOutput = codeResult.Stdout;

        // Step 2: Store the result in memory
        var storeResult = await memoryProvider.StoreAsync(
            "agent-1",
            $"Code execution result: {codeOutput}",
            0.7f,
            "code-results");
        Assert.True(storeResult.Success);

        // Step 3: Later, recall the result
        var recallResult = await memoryProvider.RecallAsync(
            "agent-1",
            "calculation",
            5);

        // Assert: Memory contains the code result
        Assert.Equal(1, recallResult.Count);
        Assert.Contains("42", recallResult.Memories![0].Content);
    }

    [Fact]
    public async Task Scenario5_MultiStepCodeExecution()
    {
        // Simulate multi-step code execution with memory
        var memoryProvider = new InMemoryToolsProvider();
        var codeProvider = new InMemoryCodeExecutionProvider();

        // Create a session
        var session = await codeProvider.CreateSessionAsync("javascript");

        // Step 1: Define a variable
        await codeProvider.ExecuteAsync(
            "console.log('Step 1: Initialized')",
            "javascript",
            session.SessionId,
            30);

        // Step 2: Store step completion in memory
        await memoryProvider.StoreAsync(
            "workflow-1",
            "Step 1 completed: Initialized",
            0.8f,
            "workflow-steps");

        // Step 3: Execute next step
        var step2Result = await codeProvider.ExecuteAsync(
            "console.log('Step 2: Processing complete')",
            "javascript",
            session.SessionId,
            30);

        // Store step 2
        await memoryProvider.StoreAsync(
            "workflow-1",
            $"Step 2 completed: {step2Result.Stdout}",
            0.8f,
            "workflow-steps");

        // Verify workflow history
        var workflowMemories = await memoryProvider.SearchAsync(
            "workflow-1",
            new MemorySearchOptions { Category = "workflow-steps", Limit = 10 });

        Assert.Equal(2, workflowMemories.Count);
    }

    #endregion

    #region Scenario 6: Error Handling

    [Fact]
    public async Task Scenario6_ErrorHandling_InvalidSession()
    {
        // Arrange
        var provider = new InMemoryCodeExecutionProvider();

        // Act: Try to install packages on non-existent session
        var result = await provider.InstallPackagesAsync(
            "nonexistent-session",
            ["lodash"]);

        // Assert: Should return error gracefully
        Assert.False(result.Success);
        Assert.Equal("Session not found", result.Error);
    }

    [Fact]
    public async Task Scenario6_ErrorHandling_InvalidMemoryId()
    {
        // Arrange
        var provider = new InMemoryToolsProvider();

        // Act: Try to forget non-existent memory
        var result = await provider.ForgetAsync("user1", "nonexistent-memory");

        // Assert: Should return failure gracefully
        Assert.False(result.Success);
        Assert.Equal("Memory not found", result.Message);
    }

    [Fact]
    public async Task Scenario6_ErrorHandling_EmptyRecall()
    {
        // Arrange
        var provider = new InMemoryToolsProvider();

        // Act: Recall from empty store
        var result = await provider.RecallAsync("user1", "anything", 10);

        // Assert: Should return empty, not error
        Assert.True(result.Success);
        Assert.Empty(result.Memories!);
    }

    #endregion

    #region Scenario 7: Concurrent Operations

    [Fact]
    public async Task Scenario7_ConcurrentMemoryOperations()
    {
        // Arrange
        var provider = new InMemoryToolsProvider();

        // Act: Concurrent store operations
        var tasks = Enumerable.Range(0, 10).Select(i =>
            provider.StoreAsync(
                "user1",
                $"Concurrent memory {i}",
                0.5f,
                "concurrent"));

        var results = await Task.WhenAll(tasks);

        // Assert: All operations should succeed
        foreach (var result in results)
        {
            Assert.True(result.Success);
        }

        // Verify all memories were stored
        var searchResult = await provider.SearchAsync(
            "user1",
            new MemorySearchOptions { Category = "concurrent", Limit = 20 });
        Assert.Equal(10, searchResult.Count);
    }

    [Fact]
    public async Task Scenario7_ConcurrentSessionOperations()
    {
        // Arrange
        var provider = new InMemoryCodeExecutionProvider();

        // Act: Create multiple sessions concurrently
        var createTasks = Enumerable.Range(0, 5).Select(_ =>
            provider.CreateSessionAsync("javascript"));

        var sessions = await Task.WhenAll(createTasks);

        // Assert: All sessions should be unique
        var sessionIds = sessions.Select(s => s.SessionId).Distinct().ToList();
        Assert.Equal(5, sessionIds.Count);

        // Act: Execute code in all sessions concurrently
        var executeTasks = sessions.Select(s =>
            provider.ExecuteAsync(
                $"console.log('Session {s.SessionId}')",
                "javascript",
                s.SessionId,
                30));

        var executeResults = await Task.WhenAll(executeTasks);

        // Assert: All executions should succeed
        foreach (var result in executeResults)
        {
            Assert.True(result.Success);
        }
    }

    #endregion

    #region Scenario 8: Tool Registration

    [Fact]
    public async Task Scenario8_ToolsHaveCorrectMetadata()
    {
        // Arrange
        var memoryProvider = new InMemoryToolsProvider();
        using var memoryTools = new MemoryIndexerTools(memoryProvider);

        var codeProvider = new InMemoryCodeExecutionProvider();
        await using var codeTools = new CodeBeakerTools(codeProvider);

        // Act: Get all tools
        var allTools = new List<AITool>();
        allTools.AddRange(memoryTools.GetTools());
        allTools.AddRange(codeTools.GetTools());

        // Assert: Verify tool count
        Assert.Equal(7, allTools.Count); // 4 memory + 3 code

        // Assert: All tools have names and descriptions
        foreach (var tool in allTools)
        {
            Assert.NotNull(tool.Name);
            Assert.NotEmpty(tool.Name);
            Assert.NotNull(tool.Description);
            Assert.NotEmpty(tool.Description);
        }

        // Assert: Tool names are unique
        var names = allTools.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Scenario8_MemoryToolsDescriptions()
    {
        // Arrange
        var provider = new InMemoryToolsProvider();
        using var tools = new MemoryIndexerTools(provider);

        // Act
        var toolList = tools.GetTools();

        // Assert: Verify descriptions are helpful
        var storeTool = toolList.First(t => t.Name == "memory_store");
        Assert.Contains("importance", storeTool.Description);

        var recallTool = toolList.First(t => t.Name == "memory_recall");
        Assert.Contains("semantic", recallTool.Description);

        var searchTool = toolList.First(t => t.Name == "memory_search");
        Assert.Contains("filter", searchTool.Description);

        var forgetTool = toolList.First(t => t.Name == "memory_forget");
        Assert.Contains("Remove", forgetTool.Description);
    }

    #endregion

    #region Scenario 9: Plugin Manager Lifecycle

    [Fact]
    public async Task Scenario9_PluginManagerDisposal()
    {
        // Arrange
        var manager = new McpPluginManager();

        // Act: Dispose manager
        await manager.DisposeAsync();

        // Assert: Operations should throw after disposal
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.GetToolsAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.ConnectAsync("test", new McpPluginConfig { Command = "test" }));
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
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reloader.InitializeAsync());

        // Cleanup
        await manager.DisposeAsync();
    }

    #endregion
}
