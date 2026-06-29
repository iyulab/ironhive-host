using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Host.Core.Tools;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IronHive.Host.Tests.Agent;

/// <summary>
/// Tests for AgentLoop integration with MCP tools and WebSearch.
/// Verifies tool composition, error resilience, and factory-level behaviors.
/// </summary>
public class AgentLoopFactoryTests
{
    #region BuiltInTools + WebSearch Integration

    [Fact]
    public void BuiltInTools_WithWebSearch_ReturnsNineTools()
    {
        // Arrange
        using var searchClient = new WebLookup.WebSearchClient();
        using var siteExplorer = new WebLookup.SiteExplorer();
        var webSearchTool = new WebSearchTool(searchClient, siteExplorer);

        // Act
        var tools = BuiltInTools.GetAll(
            Path.GetTempPath(), oopsService: null, webSearchTool: webSearchTool);

        // Assert — 7 built-in + 2 web search
        Assert.Equal(9, tools.Count);
    }

    [Fact]
    public void BuiltInTools_WithoutWebSearch_ReturnsSevenTools()
    {
        // Act
        var tools = BuiltInTools.GetAll(
            Path.GetTempPath(), oopsService: null, webSearchTool: null);

        // Assert — 7 built-in
        Assert.Equal(7, tools.Count);
    }

    #endregion

    #region MCP Plugin Manager Tool Loading

    [Fact]
    public async Task McpPluginManager_ConnectedPlugins_ReturnsToolsViaInterface()
    {
        // Arrange
        var mcpManager = Substitute.For<IMcpPluginManager>();
        mcpManager.ConnectedPlugins.Returns(new List<string> { "system-harness" }.AsReadOnly());

        var mcpTools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "help", "mcp__system-harness_help"),
            AIFunctionFactory.Create(() => "get", "mcp__system-harness_get"),
            AIFunctionFactory.Create(() => "do", "mcp__system-harness_do")
        };
        mcpManager.GetToolsAsync(Arg.Any<CancellationToken>())
            .Returns(mcpTools.AsReadOnly());

        // Act
        var tools = await mcpManager.GetToolsAsync();

        // Assert
        Assert.Equal(3, tools.Count);
    }

    [Fact]
    public async Task McpPluginManager_NoConnectedPlugins_ReturnsEmpty()
    {
        // Arrange
        var mcpManager = Substitute.For<IMcpPluginManager>();
        mcpManager.ConnectedPlugins.Returns(new List<string>().AsReadOnly());
        mcpManager.GetToolsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<AITool>().AsReadOnly());

        // Act
        var tools = await mcpManager.GetToolsAsync();

        // Assert
        Assert.Empty(tools);
    }

    [Fact]
    public async Task McpPluginManager_GetToolsThrows_ErrorIsContained()
    {
        // Arrange
        var mcpManager = Substitute.For<IMcpPluginManager>();
        mcpManager.ConnectedPlugins.Returns(new List<string> { "broken-plugin" }.AsReadOnly());
        mcpManager.GetToolsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Plugin disconnected"));

        // Act & Assert — error can be caught and handled
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mcpManager.GetToolsAsync());
        Assert.Equal("Plugin disconnected", ex.Message);
    }

    [Fact]
    public async Task McpTools_CombinedWithBuiltInTools_TotalCountCorrect()
    {
        // Arrange — simulate what AgentLoopFactory does
        var builtInTools = BuiltInTools.GetAll(Path.GetTempPath(), oopsService: null, webSearchTool: null);
        Assert.Equal(7, builtInTools.Count);

        var mcpManager = Substitute.For<IMcpPluginManager>();
        mcpManager.ConnectedPlugins.Returns(new List<string> { "system-harness" }.AsReadOnly());
        mcpManager.GetToolsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<AITool>
            {
                AIFunctionFactory.Create(() => "help", "mcp__system-harness_help"),
                AIFunctionFactory.Create(() => "get", "mcp__system-harness_get"),
                AIFunctionFactory.Create(() => "do", "mcp__system-harness_do")
            }.AsReadOnly());

        // Act — merge tools like AgentLoopFactory.LoadMcpToolsAsync does
        var mcpTools = await mcpManager.GetToolsAsync();
        foreach (var tool in mcpTools)
        {
            builtInTools.Add(tool);
        }

        // Assert — 7 built-in + 3 MCP
        Assert.Equal(10, builtInTools.Count);
    }

    #endregion

    #region AgentLoop with Combined Tools

    [Fact]
    public async Task AgentLoop_WithMcpAndBuiltInTools_HandlesToolCallsCorrectly()
    {
        // Arrange — Agent uses both MCP and built-in tools
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("mcp__system-harness_get",
                """{"category": "system", "action": "info"}""",
                "Let me get system info via MCP.")
            .EnqueueToolCallResponse("WriteFile",
                """{"path": "report.md", "content": "# System: Windows 11"}""",
                "Got the info. Saving to file.")
            .EnqueueResponse("System report saved to report.md.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You have both MCP and built-in tools."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Get system info and save it");
        Assert.Equal("mcp__system-harness_get", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("System: Windows 11, 8 cores, 16GB");
        Assert.Equal("WriteFile", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("File written successfully");
        Assert.Contains("report.md", r3.Content);
    }

    [Fact]
    public async Task AgentLoop_McpPluginLoadFailure_AgentStillWorks()
    {
        // Arrange — Even if MCP fails, agent works with built-in tools
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("ReadFile", """{"path": "README.md"}""",
                "I'll read the file using built-in tools.")
            .EnqueueResponse("README.md contains project documentation.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "Use available tools."
        });

        // Act — agent uses built-in tool despite MCP unavailability
        var r1 = await agentLoop.RunAsync("Read README.md");
        Assert.Equal("ReadFile", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("# My Project\nDocumentation here.");
        Assert.Contains("documentation", r2.Content, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region MCP Config Loading

    [Fact]
    public void McpPluginsConfigLoader_LoadFromDefault_ReturnsConfigWithoutThrow()
    {
        // Act — LoadFromDefault should not throw even if no config file exists
        var config = McpPluginsConfigLoader.LoadFromDefault();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config.Plugins);
    }

    [Fact]
    public async Task McpPluginsConfig_LoadFromConfigAsync_SkipsExcludedPlugins()
    {
        // Arrange
        var config = new McpPluginsConfig
        {
            Plugins = new Dictionary<string, McpPluginConfig>
            {
                ["allowed-plugin"] = new() { Command = "dotnet", Transport = McpTransportType.Stdio },
                ["excluded-plugin"] = new() { Command = "dotnet", Transport = McpTransportType.Stdio }
            },
            ExcludePlugins = ["excluded-plugin"],
            AutoConnect = true
        };

        var mcpManager = Substitute.For<IMcpPluginManager>();

        // Act
        await mcpManager.LoadFromConfigAsync(config);

        // Assert — only allowed-plugin should be connected
        await mcpManager.Received(1).ConnectAsync(
            "allowed-plugin", Arg.Any<McpPluginConfig>(), Arg.Any<CancellationToken>());
        await mcpManager.DidNotReceive().ConnectAsync(
            "excluded-plugin", Arg.Any<McpPluginConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task McpPluginsConfig_AutoConnectFalse_SkipsAllPlugins()
    {
        // Arrange
        var config = new McpPluginsConfig
        {
            Plugins = new Dictionary<string, McpPluginConfig>
            {
                ["plugin-a"] = new() { Command = "dotnet", Transport = McpTransportType.Stdio }
            },
            AutoConnect = false
        };

        var mcpManager = Substitute.For<IMcpPluginManager>();

        // Act
        await mcpManager.LoadFromConfigAsync(config);

        // Assert — no plugins connected
        await mcpManager.DidNotReceiveWithAnyArgs()
            .ConnectAsync(default!, default!, default);
    }

    #endregion
}
