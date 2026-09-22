using IronHive.Agent.Context;
using IronHive.Agent.Permissions;
using YamlDotNet.Serialization;

namespace IronHive.Host.Config;

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

    /// <summary>
    /// Advisor configuration — a stronger model the working model can consult through the <c>advisor</c> tool.
    /// Off until <see cref="AdvisorConfig.Model"/> is set.
    /// </summary>
    public AdvisorConfig Advisor { get; set; } = new();

    /// <summary>
    /// Agent Skills (<c>SKILL.md</c> bundles) the host's loops may use. Off until <see cref="SkillsHostConfig.Roots"/>
    /// names at least one directory.
    /// </summary>
    public SkillsHostConfig Skills { get; set; } = new();
}

/// <summary>
/// The advisor: a stronger model the working model consults through a no-argument <c>advisor</c> tool, which sends
/// it the conversation so far and returns its review. Set <see cref="Model"/> to turn it on.
/// </summary>
public class AdvisorConfig
{
    /// <summary>Provider of the advisor model (e.g. <c>anthropic</c>). Unset uses the default provider.</summary>
    public string? Provider { get; set; }

    /// <summary>The advisor model. The tool is offered only when this is set.</summary>
    public string? Model { get; set; }

    /// <summary>How many times one session may consult the advisor. Default 5.</summary>
    public int MaxCalls { get; set; } = 5;
}

/// <summary>
/// Agent Skills for the host's loops — the <c>skills</c> section of <c>config.yaml</c>. Maps onto
/// <see cref="IronHive.Agent.Skills.SkillsConfig"/>; the loop gets the skills' metadata in its system
/// instructions and a <c>load_skill</c> tool for the bodies. Nothing is loaded while <see cref="Roots"/> is empty.
/// </summary>
public class SkillsHostConfig
{
    /// <summary>Directories whose subdirectories are skills, in precedence order (the first root wins a name collision).</summary>
    public List<string> Roots { get; set; } = [];

    /// <summary>Names to use, in the order the model sees them; unset means every valid skill.</summary>
    public List<string>? Enabled { get; set; }

    /// <summary>Names never used.</summary>
    public List<string> Exclude { get; set; } = [];

    /// <summary>The most characters the skills section of the system instructions may take. Default 12 000.</summary>
    public int MaxMetadataCharacters { get; set; } = 12_000;

    /// <summary>
    /// Load a skill whose frontmatter has keys the specification does not define, reporting them as a warning,
    /// instead of rejecting it. Off by default — the specification's validator rejects such skills — but bundles
    /// written for another client often carry its keys, and this is how they are accepted.
    /// </summary>
    public bool AcceptUnknownFields { get; set; }

    /// <summary>The agent-layer configuration this section describes. Relative roots resolve against <paramref name="baseDirectory"/>.</summary>
    public IronHive.Agent.Skills.SkillsConfig ToSkillsConfig(string baseDirectory) => new()
    {
        Roots = Roots.Select(r => Path.GetFullPath(r, baseDirectory)).ToList(),
        Enabled = Enabled,
        Exclude = Exclude,
        MaxMetadataCharacters = MaxMetadataCharacters,
        UnknownFields = AcceptUnknownFields
            ? IronHive.Agent.Skills.UnknownFieldPolicy.Accept
            : IronHive.Agent.Skills.UnknownFieldPolicy.Reject
    };
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
/// errors are tolerated before the framework aborts. With <see cref="Tools.ResilientFunctionInvoker"/>
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
    /// With <see cref="Tools.ResilientFunctionInvoker"/> in the decorator chain this is rarely hit.
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
    /// Generator model identifier: a GGUF alias LMSupply registers (<c>"gguf:auto"</c> picks by
    /// hardware; <c>"gguf:qwen3-default"</c>, <c>"gguf:phi-4-mini"</c>, ...), <c>"auto"</c>, or a
    /// HuggingFace model ID. Default <c>"gguf:auto"</c> — the previous <c>"gguf:default"</c> is not an
    /// alias LMSupply knows, so a fresh install failed at its first local inference.
    /// </summary>
    public string GeneratorModel { get; set; } = "gguf:auto";

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
