using IronHive.Agent.Providers;
using IronHive.Host.Core.Config;
using LMSupply.Embedder;

namespace IronHive.Host.Core.Providers;

/// <summary>
/// LMSupply-based embedding provider for local inference.
/// </summary>
public sealed class LMSupplyEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly LMSupplyConfig _config;
    private IEmbeddingModel? _model;
    private bool _initialized;
    private bool _disposed;

    public LMSupplyEmbeddingProvider(LMSupplyConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public string ProviderName => "lmsupply";

    /// <inheritdoc />
    public bool IsAvailable => _config.Enabled;

    /// <inheritdoc />
    public int Dimensions => _model?.Dimensions ?? 0;

    /// <inheritdoc />
    public async ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _model!.EmbedAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _model!.EmbedAsync(texts, cancellationToken);
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        var modelId = _config.EmbedderModel;
        if (modelId == "auto")
        {
            modelId = "default";
        }

        _model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
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
