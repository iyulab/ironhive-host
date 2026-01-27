using IronHive.Cli.Core.Config;
using LMSupply.Reranker;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// LMSupply-based reranking provider for local inference.
/// </summary>
public sealed class LMSupplyRerankProvider : IRerankProvider, IDisposable
{
    private readonly LMSupplyConfig _config;
    private IRerankerModel? _model;
    private bool _initialized;
    private bool _disposed;

    public LMSupplyRerankProvider(LMSupplyConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public string ProviderName => "lmsupply";

    /// <inheritdoc />
    public bool IsAvailable => _config.Enabled;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IEnumerable<string> documents,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var docList = documents.ToList();
        var results = await _model!.RerankAsync(query, docList, topK, cancellationToken);

        return results.Select(r => new RerankResult
        {
            Index = r.OriginalIndex,
            Document = r.Document,
            Score = r.Score
        }).ToList();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        var modelId = _config.RerankerModel;
        if (modelId == "auto")
        {
            modelId = "default";
        }

        _model = await LocalReranker.LoadAsync(modelId, cancellationToken: cancellationToken);
        _initialized = true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_model != null)
        {
            await _model.DisposeAsync();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_model is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }
}
