using System.Text.Json;
using IronHive.Agent.Permissions;
using IronHive.Agent.Webhook;
using IronHive.Host.Core.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;

namespace IronHive.Host.Core.Config;

/// <summary>
/// Configuration scope/level.
/// </summary>
public enum ConfigScope
{
    /// <summary>Global configuration (~/.ironhive/config.yaml).</summary>
    Global,

    /// <summary>Project configuration (.ironhive/config.yaml).</summary>
    Project,

    /// <summary>Environment variables.</summary>
    Environment,

    /// <summary>.env file in current directory.</summary>
    DotEnv
}

/// <summary>
/// Full merged configuration.
/// </summary>
public class MergedConfig
{
    /// <summary>
    /// GpuStack API configuration.
    /// </summary>
    public GpuStackConfig GpuStack { get; set; } = new();

    /// <summary>
    /// LMSupply local inference configuration.
    /// </summary>
    public LMSupplyConfig LMSupply { get; set; } = new();

    /// <summary>
    /// Permission configuration for pattern-based allow/deny/ask rules.
    /// </summary>
    public PermissionConfig Permissions { get; set; } = PermissionConfig.CreateDefault();

    /// <summary>
    /// Webhook configuration.
    /// </summary>
    public WebhookConfig Webhook { get; set; } = new();

    /// <summary>
    /// Token/cost limits configuration.
    /// </summary>
    public IronHive.Agent.Tracking.UsageLimitsConfig Limits { get; set; } = new();

    /// <summary>
    /// Context management configuration.
    /// </summary>
    public ContextConfig Context { get; set; } = new();

    /// <summary>
    /// Session configuration.
    /// </summary>
    public SessionConfig Session { get; set; } = new();

    /// <summary>
    /// CLAUDE.md content (loaded separately).
    /// </summary>
    public ClaudeMdConfig? ClaudeMd { get; set; }
}

// LimitsConfig removed — use IronHive.Agent.Tracking.UsageLimitsConfig instead

/// <summary>
/// Context management configuration.
/// </summary>
public class ContextConfig
{
    /// <summary>
    /// Compaction threshold (0.0 - 1.0, default 0.92).
    /// </summary>
    public float CompactionThreshold { get; set; } = 0.92f;

    /// <summary>
    /// Number of recent messages to preserve during compaction.
    /// </summary>
    public int TailPreserveCount { get; set; } = 10;

    /// <summary>
    /// Whether to inject goal reminders in long conversations.
    /// </summary>
    public bool GoalReminderEnabled { get; set; } = true;

    /// <summary>
    /// Interval for goal reminder injection (in turns).
    /// </summary>
    public int GoalReminderInterval { get; set; } = 10;

    /// <summary>
    /// Whether prompt caching is enabled.
    /// </summary>
    public bool PromptCachingEnabled { get; set; } = true;

    /// <summary>
    /// Minimum system prompt tokens for caching.
    /// </summary>
    public int MinSystemPromptTokensForCaching { get; set; } = 1024;
}

/// <summary>
/// Session configuration.
/// </summary>
public class SessionConfig
{
    /// <summary>
    /// Directory for session transcripts.
    /// </summary>
    public string TranscriptDirectory { get; set; } = ".ironhive/sessions";

    /// <summary>
    /// Maximum number of sessions to keep.
    /// </summary>
    public int MaxSessions { get; set; } = 100;

    /// <summary>
    /// Whether to auto-save transcripts.
    /// </summary>
    public bool AutoSave { get; set; } = true;
}

/// <summary>
/// CLAUDE.md configuration (project instructions).
/// </summary>
public class ClaudeMdConfig
{
    /// <summary>
    /// Content of CLAUDE.md file.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Path where CLAUDE.md was found.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Parent directory CLAUDE.md contents (for inheritance).
    /// </summary>
    public List<ClaudeMdConfig> ParentConfigs { get; set; } = [];
}

/// <summary>
/// Manages hierarchical configuration loading and merging.
/// </summary>
public class ConfigurationManager
{
    private readonly string _globalConfigPath;
    private readonly string _projectRoot;
    private readonly ILogger<ConfigurationManager>? _logger;
    private IronHiveConfig? _cachedConfig;

    public ConfigurationManager(
        string? projectRoot = null,
        string? globalConfigPath = null,
        ILogger<ConfigurationManager>? logger = null)
    {
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _logger = logger;
        _globalConfigPath = globalConfigPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive",
            "config.yaml");
    }

    /// <summary>
    /// Loads and merges configuration from all sources.
    /// Priority: Environment > .env > Project > Global
    /// </summary>
    public IronHiveConfig Load(bool forceReload = false)
    {
        if (_cachedConfig != null && !forceReload)
        {
            return _cachedConfig;
        }

        var config = new IronHiveConfig();

        // 1. Load global config
        if (File.Exists(_globalConfigPath))
        {
            MergeFromYaml(config, _globalConfigPath);
        }

        // 2. Load project config
        var projectConfigPath = Path.Combine(_projectRoot, ".ironhive", "config.yaml");
        if (File.Exists(projectConfigPath))
        {
            MergeFromYaml(config, projectConfigPath);
        }

        // 3. Load .env file
        var dotEnvPath = Path.Combine(_projectRoot, ".env");
        if (File.Exists(dotEnvPath))
        {
            DotNetEnv.Env.Load(dotEnvPath);
        }

        // 4. Apply environment variables (highest priority)
        ApplyEnvironmentVariables(config);

        _cachedConfig = config;
        return config;
    }

    /// <summary>
    /// Loads CLAUDE.md from current directory and parent directories.
    /// </summary>
    public ClaudeMdConfig? LoadClaudeMd()
    {
        var configs = new List<ClaudeMdConfig>();
        var currentDir = _projectRoot;

        // Walk up directory tree
        while (!string.IsNullOrEmpty(currentDir))
        {
            var claudeMdPath = Path.Combine(currentDir, "CLAUDE.md");
            if (File.Exists(claudeMdPath))
            {
                configs.Add(new ClaudeMdConfig
                {
                    Content = File.ReadAllText(claudeMdPath),
                    SourcePath = claudeMdPath
                });
            }

            var parent = Directory.GetParent(currentDir);
            if (parent == null)
            {
                break;
            }

            currentDir = parent.FullName;
        }

        // Also check global CLAUDE.md
        var globalClaudeMd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "CLAUDE.md");
        if (File.Exists(globalClaudeMd))
        {
            configs.Add(new ClaudeMdConfig
            {
                Content = File.ReadAllText(globalClaudeMd),
                SourcePath = globalClaudeMd
            });
        }

        if (configs.Count == 0)
        {
            return null;
        }

        // configs[0] is the most specific (project root)
        // configs[N] is the most general (global)
        // Return project CLAUDE.md as main, parents as ParentConfigs
        var result = configs[0];
        if (configs.Count > 1)
        {
            result.ParentConfigs = configs.Skip(1).ToList();
        }

        return result;
    }

    /// <summary>
    /// Gets the combined CLAUDE.md content (all levels merged).
    /// </summary>
    public string? GetMergedClaudeMdContent()
    {
        var claudeMd = LoadClaudeMd();
        if (claudeMd == null)
        {
            return null;
        }

        var parts = new List<string>();

        // Add parent configs first
        foreach (var parent in claudeMd.ParentConfigs)
        {
            if (!string.IsNullOrWhiteSpace(parent.Content))
            {
                parts.Add($"# From: {parent.SourcePath}\n{parent.Content}");
            }
        }

        // Add main config
        if (!string.IsNullOrWhiteSpace(claudeMd.Content))
        {
            parts.Add($"# From: {claudeMd.SourcePath}\n{claudeMd.Content}");
        }

        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Gets the path to the global config file.
    /// </summary>
    public string GlobalConfigPath => _globalConfigPath;

    /// <summary>
    /// Gets the path to the project config file.
    /// </summary>
    public string ProjectConfigPath => Path.Combine(_projectRoot, ".ironhive", "config.yaml");

    private void MergeFromYaml(IronHiveConfig config, string path)
    {
        try
        {
            var yaml = File.ReadAllText(path);
            var loaded = YamlConfigSerializer.Deserialize<IronHiveConfig>(yaml);
            if (loaded != null)
            {
                MergeConfig(config, loaded);
            }
        }
        catch (IOException ex)
        {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance-critical paths
            _logger?.LogWarning(ex, "Failed to read config file: {Path}", path);
#pragma warning restore CA1848
        }
        catch (YamlException ex)
        {
#pragma warning disable CA1848
            _logger?.LogWarning(ex, "Failed to parse YAML config: {Path} at line {Line}", path, ex.Start.Line);
#pragma warning restore CA1848
        }
        catch (Exception ex)
        {
#pragma warning disable CA1848
            _logger?.LogWarning(ex, "Unexpected error loading config: {Path}", path);
#pragma warning restore CA1848
        }
    }

    /// <summary>
    /// Mechanical field-by-field merge of <paramref name="source"/> into <paramref name="target"/>
    /// across every <see cref="IronHiveConfig"/> section. String/reference-type fields fall through
    /// from the earlier scope when unset (non-empty-wins); value-type/bool fields overwrite
    /// unconditionally on presence (YAML omission = the type's default, so a later scope that omits
    /// a key cannot be distinguished from one that explicitly sets the default — acceptable per
    /// Task 1 scope; a round-trip regression test lands in a later task).
    /// </summary>
    private static void MergeConfig(IronHiveConfig target, IronHiveConfig source)
    {
        // GpuStack
        if (!string.IsNullOrEmpty(source.GpuStack.Endpoint))
        {
            target.GpuStack.Endpoint = source.GpuStack.Endpoint;
        }

        if (!string.IsNullOrEmpty(source.GpuStack.ApiKey))
        {
            target.GpuStack.ApiKey = source.GpuStack.ApiKey;
        }

        if (!string.IsNullOrEmpty(source.GpuStack.Model))
        {
            target.GpuStack.Model = source.GpuStack.Model;
        }

        if (!string.IsNullOrEmpty(source.GpuStack.EmbeddingModel))
        {
            target.GpuStack.EmbeddingModel = source.GpuStack.EmbeddingModel;
        }

        if (!string.IsNullOrEmpty(source.GpuStack.RerankModel))
        {
            target.GpuStack.RerankModel = source.GpuStack.RerankModel;
        }

        // OpenAI
        if (!string.IsNullOrEmpty(source.OpenAI.ApiKey))
        {
            target.OpenAI.ApiKey = source.OpenAI.ApiKey;
        }

        if (!string.IsNullOrEmpty(source.OpenAI.Model))
        {
            target.OpenAI.Model = source.OpenAI.Model;
        }

        if (!string.IsNullOrEmpty(source.OpenAI.Endpoint))
        {
            target.OpenAI.Endpoint = source.OpenAI.Endpoint;
        }

        // Anthropic
        if (!string.IsNullOrEmpty(source.Anthropic.ApiKey))
        {
            target.Anthropic.ApiKey = source.Anthropic.ApiKey;
        }

        if (!string.IsNullOrEmpty(source.Anthropic.Model))
        {
            target.Anthropic.Model = source.Anthropic.Model;
        }

        // GoogleAI
        if (!string.IsNullOrEmpty(source.GoogleAI.ApiKey))
        {
            target.GoogleAI.ApiKey = source.GoogleAI.ApiKey;
        }

        if (!string.IsNullOrEmpty(source.GoogleAI.Model))
        {
            target.GoogleAI.Model = source.GoogleAI.Model;
        }

        // Xai — Endpoint has a non-empty default; only overwrite when source differs from it,
        // otherwise an unset project scope would clobber a real global override with the default.
        if (!string.IsNullOrEmpty(source.Xai.ApiKey))
        {
            target.Xai.ApiKey = source.Xai.ApiKey;
        }

        if (!string.IsNullOrEmpty(source.Xai.Model))
        {
            target.Xai.Model = source.Xai.Model;
        }

        if (!string.IsNullOrEmpty(source.Xai.Endpoint) && source.Xai.Endpoint != new XaiConfig().Endpoint)
        {
            target.Xai.Endpoint = source.Xai.Endpoint;
        }

        // AzureOpenAI
        if (!string.IsNullOrEmpty(source.AzureOpenAI.Endpoint))
        {
            target.AzureOpenAI.Endpoint = source.AzureOpenAI.Endpoint;
        }

        if (!string.IsNullOrEmpty(source.AzureOpenAI.ApiKey))
        {
            target.AzureOpenAI.ApiKey = source.AzureOpenAI.ApiKey;
        }

        if (!string.IsNullOrEmpty(source.AzureOpenAI.DeploymentName))
        {
            target.AzureOpenAI.DeploymentName = source.AzureOpenAI.DeploymentName;
        }

        // LMSupply
        target.LMSupply.Enabled = source.LMSupply.Enabled;
        if (!string.IsNullOrEmpty(source.LMSupply.EmbedderModel))
        {
            target.LMSupply.EmbedderModel = source.LMSupply.EmbedderModel;
        }

        if (!string.IsNullOrEmpty(source.LMSupply.RerankerModel))
        {
            target.LMSupply.RerankerModel = source.LMSupply.RerankerModel;
        }

        if (!string.IsNullOrEmpty(source.LMSupply.GeneratorModel))
        {
            target.LMSupply.GeneratorModel = source.LMSupply.GeneratorModel;
        }

        if (source.LMSupply.MaxContextLength is not null)
        {
            target.LMSupply.MaxContextLength = source.LMSupply.MaxContextLength;
        }

        // Ollama
        if (!string.IsNullOrEmpty(source.Ollama.Endpoint))
        {
            target.Ollama.Endpoint = source.Ollama.Endpoint;
        }

        if (!string.IsNullOrEmpty(source.Ollama.Model))
        {
            target.Ollama.Model = source.Ollama.Model;
        }

        target.Ollama.Enabled = source.Ollama.Enabled;

        // LMStudio
        if (!string.IsNullOrEmpty(source.LMStudio.Endpoint))
        {
            target.LMStudio.Endpoint = source.LMStudio.Endpoint;
        }

        if (!string.IsNullOrEmpty(source.LMStudio.Model))
        {
            target.LMStudio.Model = source.LMStudio.Model;
        }

        target.LMStudio.Enabled = source.LMStudio.Enabled;

        // Permissions — replace wholesale when the source scope defines any rule. Permissions is
        // re-loaded from its own file post-merge (Task 3), so this is a best-effort fallback only.
        if (source.Permissions.Read.Count > 0 || source.Permissions.Edit.Count > 0 ||
            source.Permissions.Bash.Count > 0 || source.Permissions.ExternalDirectory.Count > 0 ||
            source.Permissions.McpTools.Count > 0)
        {
            target.Permissions = source.Permissions;
        }

        // Compaction — mechanical port of every IronHive.Agent.Context.CompactionConfig scalar.
        target.Compaction.ProtectRecentTokens = source.Compaction.ProtectRecentTokens;
        target.Compaction.MinimumPruneTokens = source.Compaction.MinimumPruneTokens;
        if (source.Compaction.ProtectedToolOutputs.Count > 0)
        {
            target.Compaction.ProtectedToolOutputs = source.Compaction.ProtectedToolOutputs;
        }

        target.Compaction.TargetRatio = source.Compaction.TargetRatio;
        target.Compaction.UseTokenBasedCompaction = source.Compaction.UseTokenBasedCompaction;
        target.Compaction.ThresholdPercentage = source.Compaction.ThresholdPercentage;
        target.Compaction.EnableObservationMasking = source.Compaction.EnableObservationMasking;
        target.Compaction.ObservationMaskingProtectedTurns = source.Compaction.ObservationMaskingProtectedTurns;
        target.Compaction.ObservationMaskingMinResultLength = source.Compaction.ObservationMaskingMinResultLength;
        target.Compaction.ToolSchemaCompression = source.Compaction.ToolSchemaCompression;
        target.Compaction.EnableToolResultCompaction = source.Compaction.EnableToolResultCompaction;
        target.Compaction.MaxToolResultChars = source.Compaction.MaxToolResultChars;
        target.Compaction.ToolResultKeepHeadLines = source.Compaction.ToolResultKeepHeadLines;
        target.Compaction.ToolResultKeepTailLines = source.Compaction.ToolResultKeepTailLines;
        target.Compaction.UseAnchoredCompaction = source.Compaction.UseAnchoredCompaction;
        target.Compaction.MaxAnchorStateChars = source.Compaction.MaxAnchorStateChars;
        if (source.Compaction.MaxContextTokens is not null)
        {
            target.Compaction.MaxContextTokens = source.Compaction.MaxContextTokens;
        }

        // SubAgent
        if (source.SubAgent.MaxDepth > 0)
        {
            target.SubAgent.MaxDepth = source.SubAgent.MaxDepth;
        }

        if (source.SubAgent.MaxConcurrent > 0)
        {
            target.SubAgent.MaxConcurrent = source.SubAgent.MaxConcurrent;
        }

        if (source.SubAgent.Explore.MaxTurns > 0)
        {
            target.SubAgent.Explore.MaxTurns = source.SubAgent.Explore.MaxTurns;
        }

        if (source.SubAgent.Explore.MaxTokens > 0)
        {
            target.SubAgent.Explore.MaxTokens = source.SubAgent.Explore.MaxTokens;
        }

        if (source.SubAgent.Explore.AllowedTools.Count > 0)
        {
            target.SubAgent.Explore.AllowedTools = source.SubAgent.Explore.AllowedTools;
        }

        if (source.SubAgent.General.MaxTurns > 0)
        {
            target.SubAgent.General.MaxTurns = source.SubAgent.General.MaxTurns;
        }

        if (source.SubAgent.General.MaxTokens > 0)
        {
            target.SubAgent.General.MaxTokens = source.SubAgent.General.MaxTokens;
        }

        // WebSearch
        target.WebSearch.Enabled = source.WebSearch.Enabled;
        if (source.WebSearch.DefaultMaxResults > 0)
        {
            target.WebSearch.DefaultMaxResults = source.WebSearch.DefaultMaxResults;
        }

        if (source.WebSearch.MaxSitemapEntries > 0)
        {
            target.WebSearch.MaxSitemapEntries = source.WebSearch.MaxSitemapEntries;
        }

        if (!string.IsNullOrEmpty(source.WebSearch.DuckDuckGoRegion))
        {
            target.WebSearch.DuckDuckGoRegion = source.WebSearch.DuckDuckGoRegion;
        }

        if (!string.IsNullOrEmpty(source.WebSearch.TavilyApiKey))
        {
            target.WebSearch.TavilyApiKey = source.WebSearch.TavilyApiKey;
        }

        if (!string.IsNullOrEmpty(source.WebSearch.SearchApiKey))
        {
            target.WebSearch.SearchApiKey = source.WebSearch.SearchApiKey;
        }

        if (!string.IsNullOrEmpty(source.WebSearch.SearchApiEngine))
        {
            target.WebSearch.SearchApiEngine = source.WebSearch.SearchApiEngine;
        }

        // DeepResearch
        target.DeepResearch.Enabled = source.DeepResearch.Enabled;
        if (!string.IsNullOrEmpty(source.DeepResearch.TavilyApiKey))
        {
            target.DeepResearch.TavilyApiKey = source.DeepResearch.TavilyApiKey;
        }

        if (source.DeepResearch.MaxIterations > 0)
        {
            target.DeepResearch.MaxIterations = source.DeepResearch.MaxIterations;
        }

        if (!string.IsNullOrEmpty(source.DeepResearch.Provider))
        {
            target.DeepResearch.Provider = source.DeepResearch.Provider;
        }

        if (!string.IsNullOrEmpty(source.DeepResearch.Model))
        {
            target.DeepResearch.Model = source.DeepResearch.Model;
        }

        // ChatBehavior
        if (source.ChatBehavior.MaximumIterationsPerRequest > 0)
        {
            target.ChatBehavior.MaximumIterationsPerRequest = source.ChatBehavior.MaximumIterationsPerRequest;
        }

        if (source.ChatBehavior.MaximumConsecutiveErrorsPerRequest > 0)
        {
            target.ChatBehavior.MaximumConsecutiveErrorsPerRequest = source.ChatBehavior.MaximumConsecutiveErrorsPerRequest;
        }
    }

    private static void ApplyEnvironmentVariables(IronHiveConfig config)
    {
        // GpuStack from environment
        var gpuStackEndpoint = Environment.GetEnvironmentVariable("GPUSTACK_ENDPOINT");
        if (!string.IsNullOrEmpty(gpuStackEndpoint))
        {
            config.GpuStack.Endpoint = gpuStackEndpoint;
        }

        var gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_API_KEY");
        if (!string.IsNullOrEmpty(gpuStackApiKey))
        {
            config.GpuStack.ApiKey = gpuStackApiKey;
        }

        var gpuStackModel = Environment.GetEnvironmentVariable("GPUSTACK_MODEL");
        if (!string.IsNullOrEmpty(gpuStackModel))
        {
            config.GpuStack.Model = gpuStackModel;
        }

        var gpuStackEmbeddingModel = Environment.GetEnvironmentVariable("GPUSTACK_EMBEDDING_MODEL");
        if (!string.IsNullOrEmpty(gpuStackEmbeddingModel))
        {
            config.GpuStack.EmbeddingModel = gpuStackEmbeddingModel;
        }

        var gpuStackRerankModel = Environment.GetEnvironmentVariable("GPUSTACK_RERANK_MODEL");
        if (!string.IsNullOrEmpty(gpuStackRerankModel))
        {
            config.GpuStack.RerankModel = gpuStackRerankModel;
        }

        // LMSupply from environment
        var lmSupplyEnabled = Environment.GetEnvironmentVariable("LMSUPPLY_ENABLED");
        if (!string.IsNullOrEmpty(lmSupplyEnabled))
        {
            config.LMSupply.Enabled = bool.TryParse(lmSupplyEnabled, out var enabled) && enabled;
        }

        // OpenAI from environment
        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(openAiApiKey))
        {
            config.OpenAI.ApiKey = openAiApiKey;
        }

        var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (!string.IsNullOrEmpty(openAiModel))
        {
            config.OpenAI.Model = openAiModel;
        }

        var openAiEndpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT");
        if (!string.IsNullOrEmpty(openAiEndpoint))
        {
            config.OpenAI.Endpoint = openAiEndpoint;
        }

        // Anthropic from environment
        var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(anthropicApiKey))
        {
            config.Anthropic.ApiKey = anthropicApiKey;
        }

        var anthropicModel = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL");
        if (!string.IsNullOrEmpty(anthropicModel))
        {
            config.Anthropic.Model = anthropicModel;
        }

        // GoogleAI from environment (GOOGLE_API_KEY accepted as an alias for GOOGLEAI_API_KEY)
        var googleAiApiKey = Environment.GetEnvironmentVariable("GOOGLEAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        if (!string.IsNullOrEmpty(googleAiApiKey))
        {
            config.GoogleAI.ApiKey = googleAiApiKey;
        }

        var googleAiModel = Environment.GetEnvironmentVariable("GOOGLEAI_MODEL");
        if (!string.IsNullOrEmpty(googleAiModel))
        {
            config.GoogleAI.Model = googleAiModel;
        }

        // Xai from environment
        var xaiApiKey = Environment.GetEnvironmentVariable("XAI_API_KEY");
        if (!string.IsNullOrEmpty(xaiApiKey))
        {
            config.Xai.ApiKey = xaiApiKey;
        }

        var xaiModel = Environment.GetEnvironmentVariable("XAI_MODEL");
        if (!string.IsNullOrEmpty(xaiModel))
        {
            config.Xai.Model = xaiModel;
        }

        var xaiEndpoint = Environment.GetEnvironmentVariable("XAI_ENDPOINT");
        if (!string.IsNullOrEmpty(xaiEndpoint))
        {
            config.Xai.Endpoint = xaiEndpoint;
        }

        // AzureOpenAI from environment
        var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        if (!string.IsNullOrEmpty(azureOpenAiEndpoint))
        {
            config.AzureOpenAI.Endpoint = azureOpenAiEndpoint;
        }

        var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(azureOpenAiApiKey))
        {
            config.AzureOpenAI.ApiKey = azureOpenAiApiKey;
        }

        var azureOpenAiDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
        if (!string.IsNullOrEmpty(azureOpenAiDeployment))
        {
            config.AzureOpenAI.DeploymentName = azureOpenAiDeployment;
        }

        // Ollama from environment
        var ollamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT");
        if (!string.IsNullOrEmpty(ollamaEndpoint))
        {
            config.Ollama.Endpoint = ollamaEndpoint;
        }

        var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (!string.IsNullOrEmpty(ollamaModel))
        {
            config.Ollama.Model = ollamaModel;
        }

        var ollamaEnabled = Environment.GetEnvironmentVariable("OLLAMA_ENABLED");
        if (!string.IsNullOrEmpty(ollamaEnabled))
        {
            config.Ollama.Enabled = bool.TryParse(ollamaEnabled, out var ollamaEnabledValue) && ollamaEnabledValue;
        }

        // LMStudio from environment
        var lmStudioEndpoint = Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT");
        if (!string.IsNullOrEmpty(lmStudioEndpoint))
        {
            config.LMStudio.Endpoint = lmStudioEndpoint;
        }

        var lmStudioModel = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL");
        if (!string.IsNullOrEmpty(lmStudioModel))
        {
            config.LMStudio.Model = lmStudioModel;
        }

        var lmStudioEnabled = Environment.GetEnvironmentVariable("LMSTUDIO_ENABLED");
        if (!string.IsNullOrEmpty(lmStudioEnabled))
        {
            config.LMStudio.Enabled = bool.TryParse(lmStudioEnabled, out var lmStudioEnabledValue) && lmStudioEnabledValue;
        }
    }
}
