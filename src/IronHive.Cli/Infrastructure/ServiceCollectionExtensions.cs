using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Agent.Mode;
using IronHive.Cli.Core.Config;
using IronHive.Cli.Core.Memory;
using IronHive.Cli.Core.Providers;
using IronHive.Cli.Core.Update;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// Extension methods for configuring IronHive services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds IronHive CLI services to the service collection.
    /// </summary>
    public static IServiceCollection AddIronHiveServices(this IServiceCollection services)
    {
        // Load configuration from .env file
        var config = EnvConfigLoader.Load();
        services.AddSingleton(config);

        // Register providers with fallback chain
        RegisterProviders(services, config);

        // Register IChatClient from provider, wrapped with FunctionInvokingChatClient
        // This enables automatic tool/function execution in the chat loop
        services.AddSingleton<IChatClient>(sp =>
        {
            var provider = sp.GetRequiredService<IChatClientProvider>();
            var innerClient = provider.GetChatClient();

            // Wrap with FunctionInvokingChatClient for automatic tool execution
            // The actual tools are provided via ChatOptions.Tools at runtime
            return new FunctionInvokingChatClient(innerClient)
            {
                MaximumIterationsPerRequest = 10,
                MaximumConsecutiveErrorsPerRequest = 3,
                IncludeDetailedErrors = true
            };
        });

        // Register IndexThinking services
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();

        // Register Memory services (MemoryIndexer integration)
        services.AddIronHiveMemory();

        // Register agent loop with IndexThinking support (default instance)
        services.AddTransient<IAgentLoop>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var turnManager = sp.GetRequiredService<IThinkingTurnManager>();

            return new ThinkingAgentLoop(
                chatClient,
                turnManager,
                new IronHive.Cli.Core.Agent.AgentOptions
                {
                    SystemPrompt = "You are a helpful AI assistant.",
                    Temperature = 0.7f,
                    MaxTokens = 4096
                });
        });

        // Register IAgentLoopFactory for runtime model/provider selection
        services.AddSingleton<IAgentLoopFactory>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IChatClientFactory>();
            var turnManager = sp.GetRequiredService<IThinkingTurnManager>();

            return new AgentLoopFactory(clientFactory, turnManager);
        });

        // Register usage tracker for session-level token tracking
        services.AddSingleton<IUsageTracker, UsageTracker>();

        // Register mode manager for Plan/Work/HITL mode system
        services.AddSingleton<IModeManager, ModeManager>();
        services.AddSingleton<IModeToolFilter>(sp =>
        {
            var ironHiveConfig = sp.GetRequiredService<IronHiveConfig>();
            return new ModeToolFilter(ironHiveConfig.Approval);
        });
        services.AddSingleton<IHumanApprovalService, Services.ConsoleApprovalService>();
        services.AddSingleton<IReplanningService, ReplanningService>();

        // Register update service for self-update functionality
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IUpdateService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new GitHubUpdateService(httpClient, "iyulab", "ironhive-cli");
        });

        return services;
    }

    private static void RegisterProviders(IServiceCollection services, IronHiveConfig config)
    {
        // GpuStack providers (primary)
        if (config.GpuStack.IsConfigured)
        {
            services.AddSingleton<GpuStackChatClientProvider>(sp =>
                new GpuStackChatClientProvider(config.GpuStack));

            services.AddSingleton<GpuStackEmbeddingProvider>(sp =>
                new GpuStackEmbeddingProvider(config.GpuStack));

            services.AddSingleton<GpuStackRerankProvider>(sp =>
                new GpuStackRerankProvider(config.GpuStack));
        }

        // LMSupply providers (fallback)
        if (config.LMSupply.Enabled)
        {
            services.AddSingleton<LMSupplyChatClientProvider>(sp =>
                new LMSupplyChatClientProvider(config.LMSupply));

            services.AddSingleton<LMSupplyEmbeddingProvider>(sp =>
                new LMSupplyEmbeddingProvider(config.LMSupply));

            services.AddSingleton<LMSupplyRerankProvider>(sp =>
                new LMSupplyRerankProvider(config.LMSupply));
        }

        // Fallback providers (composite)
        services.AddSingleton<IChatClientProvider>(sp =>
        {
            var providers = new List<IChatClientProvider>();

            var gpuStack = sp.GetService<GpuStackChatClientProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providers.Add(gpuStack);
            }

            var lmSupply = sp.GetService<LMSupplyChatClientProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No chat providers configured. Set GPUSTACK_* environment variables or enable LMSupply.");
            }

            return new FallbackChatClientProvider(providers.ToArray());
        });

        // Register IChatClientFactory for runtime model/provider selection
        services.AddSingleton<IChatClientFactory>(sp =>
        {
            var providersDict = new Dictionary<string, IChatClientProvider>();

            var gpuStack = sp.GetService<GpuStackChatClientProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providersDict["gpustack"] = gpuStack;
            }

            var lmSupply = sp.GetService<LMSupplyChatClientProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providersDict["lmsupply"] = lmSupply;
            }

            var defaultProvider = sp.GetRequiredService<IChatClientProvider>();

            // Decorator for FunctionInvokingChatClient
            IChatClient ClientDecorator(IChatClient inner) =>
                new FunctionInvokingChatClient(inner)
                {
                    MaximumIterationsPerRequest = 10,
                    MaximumConsecutiveErrorsPerRequest = 3,
                    IncludeDetailedErrors = true
                };

            return new ChatClientFactory(providersDict, defaultProvider, ClientDecorator);
        });

        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var providers = new List<IEmbeddingProvider>();

            var gpuStack = sp.GetService<GpuStackEmbeddingProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providers.Add(gpuStack);
            }

            var lmSupply = sp.GetService<LMSupplyEmbeddingProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No embedding providers configured. Set GPUSTACK_* environment variables or enable LMSupply.");
            }

            return new FallbackEmbeddingProvider(providers.ToArray());
        });

        services.AddSingleton<IRerankProvider>(sp =>
        {
            var providers = new List<IRerankProvider>();

            var gpuStack = sp.GetService<GpuStackRerankProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providers.Add(gpuStack);
            }

            var lmSupply = sp.GetService<LMSupplyRerankProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No rerank providers configured. Set GPUSTACK_* environment variables or enable LMSupply.");
            }

            return new FallbackRerankProvider(providers.ToArray());
        });
    }
}

