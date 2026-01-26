using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Config;
using IronHive.Cli.Core.Memory;
using IronHive.Cli.Core.Providers;
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

        // Register IChatClient from provider
        services.AddSingleton<IChatClient>(sp =>
        {
            var provider = sp.GetRequiredService<IChatClientProvider>();
            return provider.GetChatClient();
        });

        // Register IndexThinking services
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();

        // Register Memory services (MemoryIndexer integration)
        services.AddIronHiveMemory();

        // Register agent loop with IndexThinking support
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

