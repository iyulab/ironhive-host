using IndexThinking.Agents;
using IndexThinking.Client;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Oops;
using IronHive.Cli.Core.Providers;
using IronHive.Cli.Core.Tools;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// Factory for creating IAgentLoop instances with runtime configuration.
/// </summary>
public sealed class AgentLoopFactory : IAgentLoopFactory
{
    private readonly IChatClientFactory _clientFactory;
    private readonly IThinkingTurnManager _turnManager;
    private readonly IOopsService? _oopsService;

    private const string DefaultSystemPrompt = """
        You are a helpful AI assistant with access to tools for file and system operations.

        ## Tool Usage Guidelines
        - Use tools only when necessary to complete the user's request.
        - When a tool returns a success message, trust it and DO NOT verify with additional tool calls.
        - After completing a task (e.g., writing a file), immediately report the result to the user.
        - Avoid redundant operations: do not read a file you just wrote, or list a directory just to confirm.
        - If a tool fails, explain the error and ask for clarification if needed.

        ## Response Format
        - Be concise and direct in your responses.
        - After using tools, summarize what was done without repeating tool output verbatim.
        """;
    private const float DefaultTemperature = 0.7f;
    private const int DefaultMaxTokens = 4096;

    public AgentLoopFactory(IChatClientFactory clientFactory, IThinkingTurnManager turnManager, IOopsService? oopsService = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _turnManager = turnManager ?? throw new ArgumentNullException(nameof(turnManager));
        _oopsService = oopsService;
    }

    /// <inheritdoc />
    public IAgentLoop Create() => Create(new AgentLoopFactoryOptions());

    /// <inheritdoc />
    public IAgentLoop Create(AgentLoopFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Get chat client based on provider/model options
        var chatClient = options.Provider is not null
            ? _clientFactory.Create(options.Provider, options.Model)
            : _clientFactory.Create(options.Model);

        // Create agent options with built-in tools (with oops versioning support)
        var tools = BuiltInTools.GetAll(options.WorkingDirectory ?? Directory.GetCurrentDirectory(), _oopsService);

        var agentOptions = new IronHive.Cli.Core.Agent.AgentOptions
        {
            SystemPrompt = options.SystemPrompt ?? DefaultSystemPrompt,
            Temperature = options.Temperature ?? DefaultTemperature,
            MaxTokens = options.MaxTokens ?? DefaultMaxTokens,
            Tools = tools
        };

        // Create ThinkingAgentLoop with IndexThinking support
        return new ThinkingAgentLoop(chatClient, _turnManager, agentOptions, options.ThinkingOptions);
    }
}
