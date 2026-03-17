using IronHive.Agent.Providers;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IronHive.Agent.Memory;

/// <summary>
/// Adapts IEmbeddingProvider to IAgentEmbeddingProvider for memory service integration.
/// </summary>
internal sealed class EmbeddingProviderAdapter : IAgentEmbeddingProvider
{
    private readonly IEmbeddingProvider _provider;

    public EmbeddingProviderAdapter(IEmbeddingProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public int Dimensions => _provider.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _provider.EmbedAsync(text, cancellationToken);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await _provider.EmbedAsync(text, cancellationToken));
        }
        return results;
    }
}

/// <summary>
/// Extension methods for registering memory services.
/// </summary>
public static class MemoryServiceExtensions
{
    /// <summary>
    /// Adds memory services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddIronHiveMemory(
        this IServiceCollection services,
        Action<MemoryIndexerOptions>? configure = null)
    {
        // Register IAgentEmbeddingProvider adapter from IEmbeddingProvider
        services.TryAddSingleton<IAgentEmbeddingProvider>(sp =>
        {
            var embeddingProvider = sp.GetService<IEmbeddingProvider>();
            if (embeddingProvider is null)
            {
                throw new InvalidOperationException(
                    "IEmbeddingProvider is required for memory services. " +
                    "Register an IEmbeddingProvider before calling AddIronHiveMemory().");
            }
            return new EmbeddingProviderAdapter(embeddingProvider);
        });

        // Register MemoryIndexer's IEmbeddingService adapter (before AddMemoryIndexer)
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var agentEmbedding = sp.GetRequiredService<IAgentEmbeddingProvider>();
            return new EmbeddingServiceAdapter(agentEmbedding);
        });

        // Register MemoryIndexer's ITextCompletionService adapter (before AddMemoryIndexer)
        services.AddSingleton<ITextCompletionService>(sp =>
        {
            var chatClient = sp.GetService<IChatClient>();
            if (chatClient is null)
            {
                throw new InvalidOperationException(
                    "IChatClient is required for memory services. " +
                    "Register an IChatClient before calling AddIronHiveMemory().");
            }
            return new TextCompletionServiceAdapter(chatClient);
        });

        // Add MemoryIndexer core services
        services.AddMemoryIndexer(options =>
        {
            // Use Mock providers (adapters are already registered above)
            options.Embedding.Provider = EmbeddingProvider.Mock;
            options.Completion.Provider = CompletionProvider.Mock;
        });

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Register ISessionMemoryService
        services.TryAddSingleton<ISessionMemoryService>(sp =>
        {
            var memoryService = sp.GetRequiredService<IMemoryService>();
            return new SessionMemoryService(memoryService);
        });

        return services;
    }
}
