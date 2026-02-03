using System.Text.Json;
using IronHive.Cli.Core.Exceptions;
using IronHive.Cli.Core.Permissions;
using IronHive.Cli.Core.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace IronHive.Cli.Core.Config;

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
    public LimitsConfig Limits { get; set; } = new();

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

/// <summary>
/// Token and cost limits configuration.
/// </summary>
public class LimitsConfig
{
    /// <summary>
    /// Maximum tokens per session (0 = unlimited).
    /// </summary>
    public int MaxSessionTokens { get; set; }

    /// <summary>
    /// Maximum cost per session in USD (0 = unlimited).
    /// </summary>
    public decimal MaxSessionCost { get; set; }

    /// <summary>
    /// Warning threshold as percentage of limit (0.0 - 1.0).
    /// </summary>
    public float WarningThreshold { get; set; } = 0.8f;

    /// <summary>
    /// Whether to stop execution when limit is reached.
    /// </summary>
    public bool StopOnLimit { get; set; } = true;
}

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
    private MergedConfig? _cachedConfig;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public ConfigurationManager(string? projectRoot = null, ILogger<ConfigurationManager>? logger = null)
    {
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _logger = logger;
        _globalConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive",
            "config.yaml");
    }

    /// <summary>
    /// Loads and merges configuration from all sources.
    /// Priority: Environment > .env > Project > Global
    /// </summary>
    public MergedConfig Load(bool forceReload = false)
    {
        if (_cachedConfig != null && !forceReload)
        {
            return _cachedConfig;
        }

        var config = new MergedConfig();

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

        // 5. Load CLAUDE.md
        config.ClaudeMd = LoadClaudeMd();

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

    private void MergeFromYaml(MergedConfig config, string path)
    {
        try
        {
            var yaml = File.ReadAllText(path);
            var loaded = YamlDeserializer.Deserialize<MergedConfig>(yaml);
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

    private static void MergeConfig(MergedConfig target, MergedConfig source)
    {
        // Merge GpuStack
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

        // Merge Limits
        if (source.Limits.MaxSessionTokens > 0)
        {
            target.Limits.MaxSessionTokens = source.Limits.MaxSessionTokens;
        }

        if (source.Limits.MaxSessionCost > 0)
        {
            target.Limits.MaxSessionCost = source.Limits.MaxSessionCost;
        }

        // Merge Webhook endpoints
        if (source.Webhook.Endpoints.Count > 0)
        {
            target.Webhook.Endpoints.AddRange(source.Webhook.Endpoints);
        }

        // Merge Context settings
        target.Context.CompactionThreshold = source.Context.CompactionThreshold;
        target.Context.TailPreserveCount = source.Context.TailPreserveCount;
        target.Context.GoalReminderEnabled = source.Context.GoalReminderEnabled;
    }

    private static void ApplyEnvironmentVariables(MergedConfig config)
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

        // Limits from environment
        var maxTokens = Environment.GetEnvironmentVariable("IRONHIVE_MAX_SESSION_TOKENS");
        if (!string.IsNullOrEmpty(maxTokens) && int.TryParse(maxTokens, out var tokens))
        {
            config.Limits.MaxSessionTokens = tokens;
        }

        var maxCost = Environment.GetEnvironmentVariable("IRONHIVE_MAX_SESSION_COST");
        if (!string.IsNullOrEmpty(maxCost) && decimal.TryParse(maxCost, out var cost))
        {
            config.Limits.MaxSessionCost = cost;
        }
    }
}
