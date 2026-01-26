namespace IronHive.Cli.Core.Config;

/// <summary>
/// Configuration for IronHive CLI.
/// Loaded from .env file and environment variables.
/// </summary>
public class IronHiveConfig
{
    /// <summary>
    /// GpuStack configuration (primary provider).
    /// </summary>
    public GpuStackConfig GpuStack { get; set; } = new();

    /// <summary>
    /// LMSupply configuration (fallback provider).
    /// </summary>
    public LMSupplyConfig LMSupply { get; set; } = new();
}

/// <summary>
/// GpuStack/OpenAI-compatible API configuration.
/// </summary>
public class GpuStackConfig
{
    /// <summary>
    /// API endpoint URL.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name for chat/completion.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Model name for embeddings (optional, uses Model if not set).
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// Model name for reranking (optional).
    /// </summary>
    public string? RerankModel { get; set; }

    /// <summary>
    /// Gets whether GpuStack is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) &&
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
}

/// <summary>
/// LMSupply local inference configuration.
/// </summary>
public class LMSupplyConfig
{
    /// <summary>
    /// Whether LMSupply fallback is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Embedder model identifier ("auto", "default", or HuggingFace model ID).
    /// </summary>
    public string EmbedderModel { get; set; } = "auto";

    /// <summary>
    /// Reranker model identifier ("auto", "default", or HuggingFace model ID).
    /// </summary>
    public string RerankerModel { get; set; } = "auto";

    /// <summary>
    /// Generator model identifier ("auto", "gguf:default", or HuggingFace model ID).
    /// </summary>
    public string GeneratorModel { get; set; } = "gguf:default";
}
