using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// GpuStack/OpenAI-compatible API reranking provider.
/// Note: Reranking is not standard OpenAI API, this uses a common extension format.
/// </summary>
public sealed class GpuStackRerankProvider : IRerankProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly GpuStackConfig _config;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public GpuStackRerankProvider(GpuStackConfig config, HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();

        if (_config.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_config.Endpoint!);
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
    }

    /// <inheritdoc />
    public string ProviderName => "gpustack";

    /// <inheritdoc />
    public bool IsAvailable => _config.IsConfigured && !string.IsNullOrEmpty(_config.RerankModel);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IEnumerable<string> documents,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("No rerank model configured.");
        }

        var docList = documents.ToList();

        var request = new RerankRequest
        {
            Model = _config.RerankModel!,
            Query = query,
            Documents = docList,
            TopN = topK
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/v1/rerank",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RerankResponse>(JsonOptions, cancellationToken);
        if (result?.Results == null)
        {
            throw new InvalidOperationException("Invalid rerank response.");
        }

        return result.Results
            .Select(r => new RerankResult
            {
                Index = r.Index,
                Document = docList[r.Index],
                Score = r.RelevanceScore
            })
            .ToList();
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

    private sealed record RerankRequest
    {
        public required string Model { get; init; }
        public required string Query { get; init; }
        public required List<string> Documents { get; init; }
        public int? TopN { get; init; }
    }

    private sealed record RerankResponse
    {
        public List<RerankResultItem>? Results { get; init; }
    }

    private sealed record RerankResultItem
    {
        public int Index { get; init; }
        public float RelevanceScore { get; init; }
    }
}
