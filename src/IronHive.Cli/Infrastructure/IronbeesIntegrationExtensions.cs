using Ironbees.Core;
using Ironbees.Core.Conversation;
using IronHive.Agent.Ironbees;
using IronHive.Agent.Loop;
using IronHive.Host.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// Extension methods for configuring Ironbees multi-agent orchestration in IronHive CLI.
/// </summary>
public static class IronbeesIntegrationExtensions
{
    /// <summary>
    /// Adds Ironbees multi-agent orchestration services.
    /// This enables using multiple specialized agents defined in the agents/ directory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="agentsDirectory">Directory containing agent definitions (default: ./agents).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronbeesOrchestration(
        this IServiceCollection services,
        string agentsDirectory = "./agents")
    {
        return services.AddIronbeesOrchestration(options =>
        {
            options.AgentsDirectory = agentsDirectory;
            options.SelectorType = AgentSelectorType.Keyword;

            // Use existing IChatClient from DI
            options.ChatClientFactory = model =>
            {
                // For now, use the default chat client regardless of model config
                // In the future, this could create model-specific clients
                using var scope = services.BuildServiceProvider().CreateScope();
                var chatClient = scope.ServiceProvider.GetRequiredService<IChatClient>();
                return chatClient;
            };
        });
    }

    /// <summary>
    /// Adds Ironbees orchestration with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure Ironbees options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronbeesOrchestration(
        this IServiceCollection services,
        Action<IronbeesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Captured so the keyed "orchestrated" registration below can honor the same
        // DefaultAgentName/ConversationsDirectory settings AddIronbees applies internally.
        IronbeesOptions? capturedOptions = null;
        services.AddIronbees(options =>
        {
            configure(options);
            capturedOptions = options;
        });

        // Register OrchestratedAgentLoop as an alternative IAgentLoop. Resolves the same
        // IConversationStore that AddIronbees registers so history persists on this path too
        // (previously dropped: this factory ignored both the store and DefaultAgentName).
        services.AddKeyedTransient<IAgentLoop>("orchestrated", (sp, _) =>
        {
            var orchestrator = sp.GetRequiredService<IAgentOrchestrator>();
            var conversationStore = sp.GetService<IConversationStore>();
            return new OrchestratedAgentLoop(orchestrator, capturedOptions?.DefaultAgentName, conversationStore);
        });

        return services;
    }
}
