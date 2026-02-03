using IronHive.Cli.Core.Permissions;

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
    /// OpenAI configuration.
    /// </summary>
    public OpenAIConfig OpenAI { get; set; } = new();

    /// <summary>
    /// Anthropic configuration.
    /// </summary>
    public AnthropicConfig Anthropic { get; set; } = new();

    /// <summary>
    /// Google AI configuration.
    /// </summary>
    public GoogleAIConfig GoogleAI { get; set; } = new();

    /// <summary>
    /// Xai (Grok) configuration.
    /// </summary>
    public XaiConfig Xai { get; set; } = new();

    /// <summary>
    /// Azure OpenAI configuration.
    /// </summary>
    public AzureOpenAIConfig AzureOpenAI { get; set; } = new();

    /// <summary>
    /// LMSupply configuration (fallback provider).
    /// </summary>
    public LMSupplyConfig LMSupply { get; set; } = new();

    /// <summary>
    /// Ollama configuration.
    /// </summary>
    public OllamaConfig Ollama { get; set; } = new();

    /// <summary>
    /// LMStudio configuration.
    /// </summary>
    public LMStudioConfig LMStudio { get; set; } = new();

    /// <summary>
    /// Permission configuration for pattern-based allow/deny/ask rules.
    /// </summary>
    public PermissionConfig Permissions { get; set; } = PermissionConfig.CreateDefault();

    /// <summary>
    /// Compaction configuration for context management.
    /// </summary>
    public CompactionConfig Compaction { get; set; } = new();

    /// <summary>
    /// Sub-agent configuration.
    /// </summary>
    public SubAgentConfig SubAgent { get; set; } = new();
}

/// <summary>
/// Configuration for context compaction.
/// </summary>
public class CompactionConfig
{
    /// <summary>
    /// Number of tokens to protect at the end of the history (most recent).
    /// Default: 40,000 tokens.
    /// </summary>
    public int ProtectRecentTokens { get; set; } = 40_000;

    /// <summary>
    /// Minimum number of tokens that must be available for pruning.
    /// Compaction only occurs if there are at least this many tokens to prune.
    /// Default: 20,000 tokens.
    /// </summary>
    public int MinimumPruneTokens { get; set; } = 20_000;

    /// <summary>
    /// Tool outputs that should be protected from aggressive summarization.
    /// These tools' outputs will be preserved more carefully during compaction.
    /// </summary>
    public List<string> ProtectedToolOutputs { get; set; } = ["read_file", "grep", "glob"];

    /// <summary>
    /// Target compression ratio when compacting (0.0-1.0).
    /// After compaction, the context should be approximately this percentage of max tokens.
    /// Default: 0.70 (70%).
    /// </summary>
    public float TargetRatio { get; set; } = 0.70f;

    /// <summary>
    /// Whether to use token-based compaction instead of percentage-based.
    /// When true, uses ProtectRecentTokens and MinimumPruneTokens.
    /// When false, uses traditional percentage-based threshold.
    /// </summary>
    public bool UseTokenBasedCompaction { get; set; } = true;

    /// <summary>
    /// Threshold percentage for percentage-based compaction (legacy mode).
    /// Only used when UseTokenBasedCompaction is false.
    /// </summary>
    public float ThresholdPercentage { get; set; } = 0.92f;
}

/// <summary>
/// Configuration for sub-agents.
/// </summary>
public class SubAgentConfig
{
    /// <summary>
    /// Maximum nesting depth for sub-agents.
    /// Prevents infinite recursion.
    /// </summary>
    public int MaxDepth { get; set; } = 2;

    /// <summary>
    /// Maximum number of concurrent sub-agents.
    /// </summary>
    public int MaxConcurrent { get; set; } = 3;

    /// <summary>
    /// Configuration for Explore sub-agent.
    /// </summary>
    public ExploreAgentConfig Explore { get; set; } = new();

    /// <summary>
    /// Configuration for General sub-agent.
    /// </summary>
    public GeneralAgentConfig General { get; set; } = new();
}

/// <summary>
/// Configuration for Explore sub-agent (read-only).
/// </summary>
public class ExploreAgentConfig
{
    /// <summary>
    /// Maximum number of turns (API round-trips).
    /// </summary>
    public int MaxTurns { get; set; } = 10;

    /// <summary>
    /// Maximum context tokens.
    /// </summary>
    public int MaxTokens { get; set; } = 16_000;

    /// <summary>
    /// Tools allowed for Explore agent (read-only tools).
    /// </summary>
    public List<string> AllowedTools { get; set; } =
    [
        "read_file",
        "list_directory",
        "glob",
        "grep"
    ];
}

/// <summary>
/// Configuration for General sub-agent (full access).
/// </summary>
public class GeneralAgentConfig
{
    /// <summary>
    /// Maximum number of turns (API round-trips).
    /// </summary>
    public int MaxTurns { get; set; } = 30;

    /// <summary>
    /// Maximum context tokens.
    /// </summary>
    public int MaxTokens { get; set; } = 64_000;
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

    /// <summary>
    /// Maximum context length for local models.
    /// Lower values use less memory. Set to 0 or null for auto-detection based on available RAM.
    /// Recommended: 16384 (4GB RAM), 32768 (8GB), 65536 (16GB), 131072 (32GB+).
    /// Default: null (auto-detect).
    /// </summary>
    public int? MaxContextLength { get; set; }
}

/// <summary>
/// OpenAI API configuration.
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name (e.g., "gpt-4o", "gpt-4o-mini").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Optional custom endpoint URL (for OpenAI-compatible APIs).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets whether OpenAI is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
}

/// <summary>
/// Anthropic API configuration.
/// </summary>
public class AnthropicConfig
{
    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name (e.g., "claude-sonnet-4-20250514", "claude-3-5-haiku-20241022").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets whether Anthropic is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
}

/// <summary>
/// Google AI (Gemini) configuration.
/// </summary>
public class GoogleAIConfig
{
    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name (e.g., "gemini-2.0-flash", "gemini-1.5-pro").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets whether Google AI is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
}

/// <summary>
/// Xai (Grok) API configuration.
/// Uses OpenAI-compatible API.
/// </summary>
public class XaiConfig
{
    /// <summary>
    /// API endpoint URL.
    /// Default: "https://api.x.ai/v1"
    /// </summary>
    public string Endpoint { get; set; } = "https://api.x.ai/v1";

    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name (e.g., "grok-3", "grok-3-mini").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets whether Xai is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) &&
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
}

/// <summary>
/// Azure OpenAI configuration.
/// </summary>
public class AzureOpenAIConfig
{
    /// <summary>
    /// Azure OpenAI endpoint URL (e.g., "https://my-resource.openai.azure.com").
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Deployment name (e.g., "gpt-4o-deployment").
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// Gets whether Azure OpenAI is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) &&
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(DeploymentName);
}

/// <summary>
/// Ollama local inference configuration.
/// </summary>
public class OllamaConfig
{
    /// <summary>
    /// Ollama API endpoint URL.
    /// Default: "http://localhost:11434"
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Default model name (e.g., "llama3.2", "qwen2.5").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Whether Ollama provider is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets whether Ollama is configured and enabled.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrEmpty(Endpoint);
}

/// <summary>
/// LMStudio local inference configuration.
/// </summary>
public class LMStudioConfig
{
    /// <summary>
    /// LMStudio API endpoint URL (OpenAI-compatible).
    /// Default: "http://localhost:1234/v1"
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:1234/v1";

    /// <summary>
    /// Default model name (loaded in LMStudio).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Whether LMStudio provider is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets whether LMStudio is configured and enabled.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrEmpty(Endpoint);
}
