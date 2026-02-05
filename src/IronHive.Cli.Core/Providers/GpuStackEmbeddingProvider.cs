using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IronHive.Agent.Providers;
using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// GpuStack/OpenAI-compatible API embedding provider.
/// </summary>
public sealed class GpuStackEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly GpuStackConfig _config;
    private readonly HttpClient _httpClient;
    private int _dimensions;
    private bool _disposed;

    public GpuStackEmbeddingProvider(GpuStackConfig config, HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();

        if (_config.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_config.Endpoint!.TrimEnd('/') + "/");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
    }

    /// <inheritdoc />
    public string ProviderName => "gpustack";

    /// <inheritdoc />
    public bool IsAvailable => _config.IsConfigured && !string.IsNullOrEmpty(GetEmbeddingModel());

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public async ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var results = await EmbedBatchAsync([text], cancellationToken);
        return results[0];
    }

    /// <inheritdoc />
    public async ValueTask<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var model = GetEmbeddingModel();
        if (string.IsNullOrEmpty(model))
        {
            throw new InvalidOperationException("No embedding model configured.");
        }

        var request = new EmbeddingRequest
        {
            Model = model,
            Input = texts.ToList()
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/v1/embeddings",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, cancellationToken);
        if (result?.Data == null)
        {
            throw new InvalidOperationException("Invalid embedding response.");
        }

        var embeddings = result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();

        if (embeddings.Length > 0)
        {
            _dimensions = embeddings[0].Length;
        }

        return embeddings;
    }

    private string? GetEmbeddingModel()
    {
        return _config.EmbeddingModel ?? _config.Model;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }

    private sealed record EmbeddingRequest
    {
        public required string Model { get; init; }
        public required List<string> Input { get; init; }
    }

    private sealed record EmbeddingResponse
    {
        public List<EmbeddingData>? Data { get; init; }
    }

    private sealed record EmbeddingData
    {
        public int Index { get; init; }
        public float[] Embedding { get; init; } = [];
    }
}
