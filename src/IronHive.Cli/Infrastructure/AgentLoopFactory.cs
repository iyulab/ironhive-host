using IndexThinking.Agents;
using IndexThinking.Client;
using IronHive.Agent.Context;
using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Agent.Providers;
using IronHive.Host.Core.Context;
using IronHive.Host.Core.Oops;
using IronHive.Host.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// Factory for creating IAgentLoop instances with runtime configuration.
/// </summary>
public sealed partial class AgentLoopFactory : IAgentLoopFactory
{
    private readonly IChatClientFactory _clientFactory;
    private readonly IThinkingTurnManager _turnManager;
    private readonly IOopsService? _oopsService;
    private readonly WebSearchTool? _webSearchTool;
    private readonly DeepResearchTool? _deepResearchTool;
    private readonly IMcpPluginManager? _mcpPluginManager;
    private readonly ILogger<AgentLoopFactory>? _logger;
    private readonly CompactionConfig? _compactionConfig;
    private int _mcpPluginsLoaded;

    private const string DefaultSystemPrompt = """
        You are a helpful AI assistant with access to tools for file, system, and web operations.

        ## Tool Usage Guidelines
        - Use tools only when necessary to complete the user's request.
        - When a tool returns a success message, trust it and DO NOT verify with additional tool calls.
        - After completing a task (e.g., writing a file), immediately report the result to the user.
        - Avoid redundant operations: do not read a file you just wrote, or list a directory just to confirm.
        - If a tool fails, explain the error and ask for clarification if needed.

        ## Web Search
        - Use WebSearch to find up-to-date information from the web.
        - Use ExploreSite to analyze a website's structure via robots.txt and sitemap.

        ## Deep Research
        - Use DeepResearch for complex queries requiring multi-step investigation.
        - It autonomously searches, analyzes sources, and generates a comprehensive report.
        - Choose depth: 'quick' (1-2 min), 'standard' (3-5 min), or 'comprehensive' (10-15 min).

        ## MCP Plugins
        - Additional tools may be available via MCP plugins (e.g., desktop automation, screen capture).
        - MCP tools are prefixed with the plugin name (e.g., system-harness).
        - Use the help tool to discover available commands when working with MCP plugins.

        ## Response Format
        - Be concise and direct in your responses.
        - After using tools, summarize what was done without repeating tool output verbatim.
        """;
    private const float DefaultTemperature = 0.7f;
    private const int DefaultMaxTokens = 4096;

    public AgentLoopFactory(
        IChatClientFactory clientFactory,
        IThinkingTurnManager turnManager,
        IOopsService? oopsService = null,
        WebSearchTool? webSearchTool = null,
        DeepResearchTool? deepResearchTool = null,
        IMcpPluginManager? mcpPluginManager = null,
        ILogger<AgentLoopFactory>? logger = null,
        CompactionConfig? compactionConfig = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _turnManager = turnManager ?? throw new ArgumentNullException(nameof(turnManager));
        _oopsService = oopsService;
        _webSearchTool = webSearchTool;
        _deepResearchTool = deepResearchTool;
        _mcpPluginManager = mcpPluginManager;
        _logger = logger;
        _compactionConfig = compactionConfig;
    }

    /// <inheritdoc />
    public Task<IAgentLoop> CreateAsync(CancellationToken cancellationToken = default)
        => CreateAsync(new AgentLoopFactoryOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<IAgentLoop> CreateAsync(AgentLoopFactoryOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Get chat client based on provider/model options asynchronously
        var chatClient = options.Provider is not null
            ? await _clientFactory.CreateAsync(options.Provider, options.Model, cancellationToken)
            : await _clientFactory.CreateAsync(options.Model, cancellationToken);

        // Create agent options with built-in tools (with oops versioning and web search support)
        var tools = BuiltInTools.GetAll(options.WorkingDirectory ?? Directory.GetCurrentDirectory(), _oopsService, _webSearchTool, _deepResearchTool);

        // Load MCP plugins and add their tools
        await LoadMcpToolsAsync(tools, cancellationToken);

        var agentOptions = new IronHive.Agent.Loop.AgentOptions
        {
            SystemPrompt = options.SystemPrompt ?? DefaultSystemPrompt,
            Temperature = options.Temperature ?? DefaultTemperature,
            MaxTokens = options.MaxTokens ?? DefaultMaxTokens,
            Tools = tools
        };

        // Wire context compaction from host config so long sessions compact history
        // instead of overflowing. Without this the loop's ContextManager stays null and
        // CompactionConfig is inert. Model-aware so the context window matches the active model.
        var contextManager = HostContextManagerFactory.Create(_compactionConfig, options.Model);

        // Create ThinkingAgentLoop with IndexThinking support
        return new ThinkingAgentLoop(
            chatClient, _turnManager, agentOptions, options.ThinkingOptions, contextManager: contextManager);
    }

    /// <summary>
    /// Loads MCP plugins from configuration and adds their tools to the tool list.
    /// Plugin loading is resilient — connection failures are logged but don't block agent creation.
    /// </summary>
    private async Task LoadMcpToolsAsync(IList<AITool> tools, CancellationToken cancellationToken)
    {
        if (_mcpPluginManager is null)
        {
            return;
        }

        // Thread-safe: load plugin config only once per factory lifetime
        if (Interlocked.CompareExchange(ref _mcpPluginsLoaded, 1, 0) == 0)
        {
            try
            {
                var config = McpPluginsConfigLoader.LoadFromDefault();
                if (config.Plugins.Count > 0)
                {
                    await _mcpPluginManager.LoadFromConfigAsync(config, cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMcpPluginLoadFailed(_logger, ex);
            }
        }

        // Add tools from connected plugins
        if (_mcpPluginManager.ConnectedPlugins.Count > 0)
        {
            try
            {
                var mcpTools = await _mcpPluginManager.GetToolsAsync(cancellationToken);
                foreach (var tool in mcpTools)
                {
                    tools.Add(tool);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMcpToolsFailed(_logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load MCP plugins from configuration")]
    private static partial void LogMcpPluginLoadFailed(ILogger? logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get tools from MCP plugins")]
    private static partial void LogMcpToolsFailed(ILogger? logger, Exception ex);
}
