namespace IronHive.Cli.Core.Providers;

/// <summary>
/// Composite embedding provider with automatic fallback.
/// Tries providers in order until one succeeds.
/// </summary>
public sealed class FallbackEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider[] _providers;
    private IEmbeddingProvider? _activeProvider;
    private bool _initialized;

    public FallbackEmbeddingProvider(params IEmbeddingProvider[] providers)
    {
        if (providers == null || providers.Length == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }

        _providers = providers;
    }

    /// <inheritdoc />
    public string ProviderName => _activeProvider?.ProviderName ?? "fallback";

    /// <inheritdoc />
    public bool IsAvailable => _providers.Any(p => p.IsAvailable);

    /// <inheritdoc />
    public int Dimensions => _activeProvider?.Dimensions ?? 0;

    /// <inheritdoc />
    public async ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _activeProvider!.EmbedAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _activeProvider!.EmbedBatchAsync(texts, cancellationToken);
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _activeProvider != null)
        {
            return;
        }

        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable)
            {
                continue;
            }

            try
            {
                // Try a simple operation to verify the provider works
                await provider.EmbedAsync("test", cancellationToken);
                _activeProvider = provider;
                _initialized = true;
                return;
            }
            catch
            {
                // Provider failed, try next
            }
        }

        throw new InvalidOperationException("No available embedding provider.");
    }

    /// <summary>
    /// Gets the currently active provider.
    /// </summary>
    public IEmbeddingProvider? ActiveProvider => _activeProvider;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }
}
