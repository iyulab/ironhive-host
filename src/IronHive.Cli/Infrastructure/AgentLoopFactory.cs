using IndexThinking.Agents;
using IndexThinking.Client;
using IronHive.Cli.Core.Agent;
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

    private const string DefaultSystemPrompt = "You are a helpful AI assistant.";
    private const float DefaultTemperature = 0.7f;
    private const int DefaultMaxTokens = 4096;

    public AgentLoopFactory(IChatClientFactory clientFactory, IThinkingTurnManager turnManager)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _turnManager = turnManager ?? throw new ArgumentNullException(nameof(turnManager));
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

        // Create agent options with built-in tools
        var tools = BuiltInTools.GetAll(options.WorkingDirectory ?? Directory.GetCurrentDirectory());

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
