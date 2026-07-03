using IronHive.Agent.Context;
using IronHive.Agent.Permissions;
using YamlDotNet.Serialization;

namespace IronHive.Host.Core.Config;

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
    [YamlMember(Alias = "openai")]
    public OpenAIConfig OpenAI { get; set; } = new();

    /// <summary>
    /// Anthropic configuration.
    /// </summary>
    public AnthropicConfig Anthropic { get; set; } = new();

    /// <summary>
    /// Google AI configuration.
    /// </summary>
    [YamlMember(Alias = "googleai")]
    public GoogleAIConfig GoogleAI { get; set; } = new();

    /// <summary>
    /// Xai (Grok) configuration.
    /// </summary>
    public XaiConfig Xai { get; set; } = new();

    /// <summary>
    /// Azure OpenAI configuration.
    /// </summary>
    [YamlMember(Alias = "azureopenai")]
    public AzureOpenAIConfig AzureOpenAI { get; set; } = new();

    /// <summary>
    /// LMSupply configuration (fallback provider).
    /// </summary>
    [YamlMember(Alias = "lmsupply")]
    public LMSupplyConfig LMSupply { get; set; } = new();

    /// <summary>
    /// Ollama configuration.
    /// </summary>
    public OllamaConfig Ollama { get; set; } = new();

    /// <summary>
    /// LMStudio configuration.
    /// </summary>
    [YamlMember(Alias = "lmstudio")]
    public LMStudioConfig LMStudio { get; set; } = new();

    /// <summary>
    /// Permission configuration for pattern-based allow/deny/ask rules.
    /// </summary>
    public PermissionConfig Permissions { get; set; } = PermissionConfig.CreateDefault();

    /// <summary>
    /// Compaction configuration for context management. Uses the agent's
    /// <see cref="IronHive.Agent.Context.CompactionConfig"/> directly (single source of truth;
    /// the host previously duplicated a subset of these fields).
    /// </summary>
    public CompactionConfig Compaction { get; set; } = new();

    /// <summary>
    /// Sub-agent configuration.
    /// </summary>
    public SubAgentConfig SubAgent { get; set; } = new();

    /// <summary>
    /// WebLookup web search configuration.
    /// </summary>
    public WebSearchConfig WebSearch { get; set; } = new();

    /// <summary>
    /// DeepResearch configuration.
    /// </summary>
    public DeepResearchConfig DeepResearch { get; set; } = new();

    /// <summary>
    /// Chat-behavior configuration — caps that govern how
    /// <c>FunctionInvokingChatClient</c> orchestrates tool-call iteration. Exposed so
    /// consumers can tune per-model behavior (small 4K-window models often want a
    /// lower iteration cap; 16K+ models can take a higher one) without forking the
    /// cli source. Phase D-4, ecosystem ISSUE 2026-04-30.
    /// </summary>
    public ChatBehaviorConfig ChatBehavior { get; set; } = new();
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
/// Caps that govern <c>FunctionInvokingChatClient</c>'s tool-call orchestration loop.
/// Phase D-4 — exposed so consumers can tune values per-model without forking.
/// </summary>
/// <remarks>
/// <para>
/// <b>MaximumIterationsPerRequest</b> is the most consequential knob for small
/// quantized models on tight 4K context windows. Lower it (e.g. 5) when a model
/// struggles to self-correct empty-args calls within the default 10 iterations and
/// the retry-storm is overflowing the context window before <c>TokenBudgetChatClient</c>
/// (D-2) can rescue. Raise it (e.g. 15-20) on 16K+ models where the model legitimately
/// needs more rounds for multi-step task completion.
/// </para>
/// <para>
/// <b>MaximumConsecutiveErrorsPerRequest</b> caps how many back-to-back marshaller
/// errors are tolerated before the framework aborts. With <see cref="ResilientFunctionInvoker"/>
/// installed this is rarely hit (errors are converted to actionable strings), but it
/// remains a backstop.
/// </para>
/// </remarks>
public class ChatBehaviorConfig
{
    /// <summary>
    /// Maximum tool-call iteration rounds the M.E.AI <c>FunctionInvokingChatClient</c>
    /// will run inside a single request. Default: 10. Lower values (5-7) help small/quantized
    /// models on 4K context windows; higher values (15-20) suit large-context models that
    /// need more rounds for multi-step tasks.
    /// </summary>
    public int MaximumIterationsPerRequest { get; set; } = 10;

    /// <summary>
    /// Maximum consecutive marshaller errors before the framework gives up. Default: 3.
    /// With <see cref="ResilientFunctionInvoker"/> in the decorator chain this is rarely hit.
    /// </summary>
    public int MaximumConsecutiveErrorsPerRequest { get; set; } = 3;
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

/// <summary>
/// WebLookup web search configuration.
/// </summary>
public class WebSearchConfig
{
    /// <summary>
    /// Whether web search tools are enabled.
    /// Default: true (uses DuckDuckGo which requires no API key).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default maximum search results.
    /// </summary>
    public int DefaultMaxResults { get; set; } = 10;

    /// <summary>
    /// Maximum sitemap entries to return.
    /// </summary>
    public int MaxSitemapEntries { get; set; } = 50;

    /// <summary>
    /// DuckDuckGo region (e.g., "wt-wt" for worldwide, "kr-kr" for Korea).
    /// </summary>
    public string? DuckDuckGoRegion { get; set; }

    /// <summary>
    /// Tavily API key (optional, enables Tavily search).
    /// </summary>
    public string? TavilyApiKey { get; set; }

    /// <summary>
    /// SearchApi API key (optional, enables SearchApi search).
    /// </summary>
    public string? SearchApiKey { get; set; }

    /// <summary>
    /// SearchApi engine (default: "google").
    /// </summary>
    public string SearchApiEngine { get; set; } = "google";
}

/// <summary>
/// DeepResearch autonomous research configuration.
/// </summary>
public class DeepResearchConfig
{
    /// <summary>
    /// Whether DeepResearch tools are enabled.
    /// Requires a Tavily API key for search functionality.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Tavily API key for DeepResearch search. Falls back to WebSearch.TavilyApiKey if not set.
    /// </summary>
    public string? TavilyApiKey { get; set; }

    /// <summary>
    /// Maximum research iterations per query.
    /// </summary>
    public int MaxIterations { get; set; } = 5;

    /// <summary>
    /// Provider name for the research LLM (e.g., "openai", "anthropic").
    /// If not set, uses the default provider.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Model name for the research LLM.
    /// If not set, uses the provider's default model.
    /// </summary>
    public string? Model { get; set; }
}
