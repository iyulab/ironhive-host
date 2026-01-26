using IronHive.Cli.Core.Providers;
using MemoryIndexer.Interfaces;

namespace IronHive.Cli.Core.Memory;

/// <summary>
/// Adapts IronHive's IEmbeddingProvider to MemoryIndexer's IEmbeddingService.
/// </summary>
public class EmbeddingServiceAdapter : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;

    public EmbeddingServiceAdapter(IEmbeddingProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc />
    public int Dimensions => _provider.Dimensions;

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var embedding = await _provider.EmbedAsync(text, cancellationToken);
        return new ReadOnlyMemory<float>(embedding);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var embeddings = await _provider.EmbedBatchAsync(textList, cancellationToken);

        return embeddings
            .Select(e => new ReadOnlyMemory<float>(e))
            .ToList();
    }
}
