using IronHive.Agent.Context;
using IronHive.Agent.Permissions;
using IronHive.Agent.Tracking;
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
    /// Usage budget per agent session (one agent loop — a CLI session, or one server session): <c>maxSessionTokens</c>,
    /// <c>maxSessionCost</c> (USD, priced from the TokenMeter catalog), <c>warningThreshold</c>, <c>stopOnLimit</c>. Uses the
    /// agent's <see cref="UsageLimitsConfig"/> directly. Checked before every model call, each tool round included; a call
    /// past the limit ends the turn with <c>UsageLimitExceededException</c>. Zero (the default) means no limit.
    /// </summary>
    public UsageLimitsConfig Budget { get; set; } = new();

    /// <summary>
    /// AGENTS.md loading for CLI / <c>run --server</c> loops: the files from the repository root down to the session's
    /// working directory join the system instructions, nearest last. On unless <see cref="AgentsMdHostConfig.Enabled"/>
    /// is <c>false</c>.
    /// </summary>
    public AgentsMdHostConfig AgentsMd { get; set; } = new();

    /// <summary>
    /// Tool retrieval for CLI / <c>run --server</c> loops: when on, each request sends the model only the tools most
    /// relevant to the user's request (<c>IronHive.Agent.Context.KeywordToolRetriever</c>) instead of every registered
    /// tool. Off unless <see cref="ToolRetrievalHostConfig.Enabled"/> is <c>true</c>.
    /// </summary>
    public ToolRetrievalHostConfig ToolRetrieval { get; set; } = new();

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
    /// host source.
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

    /// <summary>
    /// Ironbees named agents the working model may delegate to, one tool per agent. Off until
    /// <see cref="DelegationHostConfig.Agents"/> lists at least one agent.
    /// </summary>
    public DelegationHostConfig Delegation { get; set; } = new();

    /// <summary>
    /// How a tool call that needs a person's approval is waited on when a client answers it over the wire
    /// (<c>run --server</c>).
    /// </summary>
    public ApprovalHostConfig Approval { get; set; } = new();
}

/// <summary>
/// Waiting for an approval answered by the client (<c>hitl_request</c> -> <c>hitl_response</c>).
/// </summary>
public class ApprovalHostConfig
{
    /// <summary>
    /// How long a request waits for the client's answer before it is closed as a denial. Default 300 (five minutes);
    /// raise it when a person answers from another device or later.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Delegation — the <c>delegation</c> section of <c>config.yaml</c>. Each listed agent (an Ironbees agent directory,
/// <c>agents/&lt;name&gt;/agent.yaml</c>) becomes a tool the working model can hand a task to. A delegated run calls its
/// tools through the session's own approval path (permission rules, planning mode, the human approval prompt) and
/// spends the session's budget.
/// </summary>
public class DelegationHostConfig
{
    /// <summary>
    /// Where the agent directories are. A relative path is taken from the session's working directory. Default
    /// <c>agents</c>.
    /// </summary>
    public string? AgentsDirectory { get; set; }

    /// <summary>The agents to offer. Empty (the default) turns delegation off.</summary>
    public List<DelegatedAgentHostConfig> Agents { get; set; } = [];

    /// <summary>How deep delegations may nest. 0 uses the library default (2).</summary>
    public int MaxDepth { get; set; }

    /// <summary>How many delegated runs may be in flight at once across all agents. 0 uses the library default (3).</summary>
    public int MaxConcurrent { get; set; }
}

/// <summary>One agent offered for delegation.</summary>
public class DelegatedAgentHostConfig
{
    /// <summary>The agent's name — its directory under <see cref="DelegationHostConfig.AgentsDirectory"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider of the model (e.g. <c>gpustack</c>). Unset uses the default provider.</summary>
    public string? Provider { get; set; }

    /// <summary>The model the agent runs on. Unset uses the model in its <c>agent.yaml</c>, else the session's model.</summary>
    public string? Model { get; set; }

    /// <summary>What the tool tells the working model about the agent. Unset uses the agent's own description.</summary>
    public string? Description { get; set; }

    /// <summary>How many tool turns one delegated run may take. Unset uses the agent runtime's default.</summary>
    public int? MaxToolTurns { get; set; }
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

    /// <summary>
    /// The agent-layer configuration this section describes. A root starting with <c>~</c> is under the user's home
    /// directory; other relative roots resolve against <paramref name="baseDirectory"/>.
    /// </summary>
    public IronHive.Agent.Skills.SkillsConfig ToSkillsConfig(string baseDirectory) => new()
    {
        Roots = Roots.Select(r => Path.GetFullPath(ExpandHome(r), baseDirectory)).ToList(),
        Enabled = Enabled,
        Exclude = Exclude,
        MaxMetadataCharacters = MaxMetadataCharacters,
        UnknownFields = AcceptUnknownFields
            ? IronHive.Agent.Skills.UnknownFieldPolicy.Accept
            : IronHive.Agent.Skills.UnknownFieldPolicy.Reject
    };

    // «~» and «~/…» (either separator) name the home directory, as the README examples write them. Path.GetFullPath does
    // not expand it: «~/.ironhive/skills» became a directory literally named «~» under the working directory.
    internal static string ExpandHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~")
        {
            return home;
        }

        return path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
            ? Path.Combine(home, path[2..])
            : path;
    }
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
    /// Whether an image a tool returns (an MCP tool drawing a chart, a screenshot) is sent to the model in a user message
    /// after the tool results. This endpoint speaks Chat Completions, whose tool messages hold text only; without this the
    /// image is replaced by a note. Turn it on for a vision model. Unset = off.
    /// </summary>
    public bool? CarryToolImages { get; set; }

    /// <summary>
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

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
/// Exposed so the values can be tuned per model without forking.
/// </summary>
/// <remarks>
/// <para>
/// <b>MaximumIterationsPerRequest</b> is the most consequential knob for small
/// quantized models on tight 4K context windows. Lower it (e.g. 5) when a model
/// struggles to self-correct empty-args calls within the default 10 iterations and
/// the retry-storm is overflowing the context window before <c>TokenBudgetChatClient</c>
/// can rescue. Raise it (e.g. 15-20) on 16K+ models where the model legitimately
/// needs more rounds for multi-step task completion.
/// </para>
/// <para>
/// <b>MaximumConsecutiveErrorsPerRequest</b> caps how many back-to-back tool errors are
/// tolerated before the framework aborts. With <see cref="Tools.ResilientArgumentsMiddleware"/>
/// (or <see cref="Tools.ResilientFunctionInvoker"/>) installed this is rarely hit (argument errors
/// are converted to actionable strings), but it remains a backstop. In the CLI, the tool invocation
/// pipeline also ends the turn when the same tool fails with the same error three times in a row;
/// raising this cap does not raise that limit.
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
    /// Maximum consecutive tool errors before the framework gives up. Default: 3.
    /// With <see cref="Tools.ResilientArgumentsMiddleware"/> in the tool invocation pipeline (or
    /// <see cref="Tools.ResilientFunctionInvoker"/> as the invoker) this is rarely hit.
    /// </summary>
    public int MaximumConsecutiveErrorsPerRequest { get; set; } = 3;

    /// <summary>
    /// Output tokens allowed per model call. Default: 4096. A reasoning model spends part of it on its thinking, so a
    /// turn that ends at <c>output_limit</c> with little or no answer needs a larger value (e.g. 16384) — within what the
    /// server allows.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>
    /// How much the model reasons on every call: <c>none</c>, <c>low</c>, <c>medium</c>, <c>high</c> or
    /// <c>extra_high</c> (<see cref="ReasoningEffortName"/>). Null (the default) sends nothing, so a reasoning model
    /// uses its own default — which can be tens of thousands of thinking tokens on one call. <c>none</c> turns thinking
    /// off; the other levels are requests a server may hold loosely. A server turn's <c>reasoning_effort</c> replaces it
    /// for that turn.
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Longest one tool call may run, in seconds. Past it the call is stopped and the model reads that the tool ran out
    /// of time (so it can ask for less), and the same slow call repeated three times ends the turn. Only the tool's own
    /// run is timed — an approval prompt in front of it is not. Null (the default): no limit. Must be positive when set.
    /// </summary>
    public int? ToolCallTimeoutSeconds { get; set; }
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
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

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
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

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
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

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
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets whether Xai is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) &&
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
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
    /// Whether an image a tool returns (an MCP tool drawing a chart, a screenshot) is sent to the model in a user message
    /// after the tool results. This endpoint speaks Chat Completions, whose tool messages hold text only; without this the
    /// image is replaced by a note. Turn it on for a vision model. Unset = off.
    /// </summary>
    public bool? CarryToolImages { get; set; }

    /// <summary>
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

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
    /// Whether an image a tool returns (an MCP tool drawing a chart, a screenshot) is sent to the model in a user message
    /// after the tool results. This endpoint speaks Chat Completions, whose tool messages hold text only; without this the
    /// image is replaced by a note. Turn it on for a vision model. Unset = off.
    /// </summary>
    public bool? CarryToolImages { get; set; }

    /// <summary>
    /// How long a streaming response may stay silent — before its first event and between events — before the request
    /// ends with a «stream idle timeout». Seconds; must be positive. Unset = no limit. Unlike a whole-request deadline it
    /// tells a slow stream (a long answer still flowing) from a dead one, so for a local server on slow hardware set it
    /// above the prompt-evaluation time before the first token.
    /// </summary>
    public int? StreamIdleTimeoutSeconds { get; set; }

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

/// <summary>
/// The <c>toolRetrieval</c> section: whether CLI / server loops send the model a selection of the registered tools, and how
/// that selection is made. The fields map onto <c>IronHive.Agent.Context.ToolRetrievalOptions</c>.
/// </summary>
public sealed class ToolRetrievalHostConfig
{
    /// <summary><c>true</c> turns tool retrieval on; unset (the default) or <c>false</c> sends every tool.</summary>
    public bool? Enabled { get; set; }

    /// <summary>The most tools a request carries, pinned tools aside (0 = the library default, 10).</summary>
    public int MaxTools { get; set; }

    /// <summary>The least relevance (0–1) a scored tool needs (unset = the library default, 0.3).</summary>
    public float? MinRelevanceScore { get; set; }

    /// <summary>Scored slots kept even when <see cref="AlwaysInclude"/> fills <see cref="MaxTools"/> (0 = none guaranteed).</summary>
    public int MinScoredSlots { get; set; }

    /// <summary>Tool names sent with every request whatever their score.</summary>
    public List<string> AlwaysInclude { get; set; } = [];

    /// <summary>
    /// When above 0, the tools a conversation already sent are sent again unchanged while they serve the request; the
    /// set changes only for a pin, an exact name or alias, or the request's best-scored tool, and starts over when it
    /// would exceed this many. Keeps a prefix-cached server's prompt cache; 0 (the default) selects every request on
    /// its own.
    /// </summary>
    public int StickyToolLimit { get; set; }

    /// <summary>
    /// With <see cref="StickyToolLimit"/> on, the score (0–1) a scored tool the carried set lacks must reach before it may
    /// change the set; below it the set is held and the tool is withheld (unset = the library default, 0.7). Pins, exact
    /// names and aliases change the set whatever this is.
    /// </summary>
    public float? StickyChangeScore { get; set; }
}

/// <summary>
/// The <c>agentsMd</c> section: whether CLI / server loops read the AGENTS.md files that apply to their working directory
/// (<c>IronHive.Agent.Context.AgentsMdInstructions</c>) and how much of them.
/// </summary>
public sealed class AgentsMdHostConfig
{
    /// <summary><c>false</c> turns AGENTS.md loading off; unset (the default) or <c>true</c> keeps it on.</summary>
    public bool? Enabled { get; set; }

    /// <summary>The most characters of AGENTS.md text to add (0 = the library default, 32,000).</summary>
    public int MaxCharacters { get; set; }
}
