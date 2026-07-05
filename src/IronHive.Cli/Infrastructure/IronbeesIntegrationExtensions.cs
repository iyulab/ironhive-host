using Ironbees.Core;
using IronHive.Agent.Loop;
using IronHive.Host.Ironbees;
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
        services.AddIronbees(options =>
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

        // Register OrchestratedAgentLoop as an alternative IAgentLoop
        services.AddKeyedTransient<IAgentLoop>("orchestrated", (sp, _) =>
        {
            var orchestrator = sp.GetRequiredService<IAgentOrchestrator>();
            return new OrchestratedAgentLoop(orchestrator);
        });

        return services;
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

        services.AddIronbees(configure);

        // Register OrchestratedAgentLoop as an alternative IAgentLoop
        services.AddKeyedTransient<IAgentLoop>("orchestrated", (sp, _) =>
        {
            var orchestrator = sp.GetRequiredService<IAgentOrchestrator>();
            return new OrchestratedAgentLoop(orchestrator);
        });

        return services;
    }
}
