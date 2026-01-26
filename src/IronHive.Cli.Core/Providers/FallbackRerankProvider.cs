namespace IronHive.Cli.Core.Providers;

/// <summary>
/// Composite rerank provider with automatic fallback.
/// Tries providers in order until one succeeds.
/// </summary>
public sealed class FallbackRerankProvider : IRerankProvider
{
    private readonly IRerankProvider[] _providers;
    private IRerankProvider? _activeProvider;
    private bool _initialized;

    public FallbackRerankProvider(params IRerankProvider[] providers)
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
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IEnumerable<string> documents,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _activeProvider!.RerankAsync(query, documents, topK, cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
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
                await provider.RerankAsync("test", ["doc1", "doc2"], 1, cancellationToken);
                _activeProvider = provider;
                _initialized = true;
                return;
            }
            catch
            {
                // Provider failed, try next
            }
        }

        throw new InvalidOperationException("No available rerank provider.");
    }

    /// <summary>
    /// Gets the currently active provider.
    /// </summary>
    public IRerankProvider? ActiveProvider => _activeProvider;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }
}
