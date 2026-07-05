using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using IronHive.Agent.Permissions;
using IronHive.Host.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace IronHive.Host.Config;

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

        // 5. Load permission config from default locations
        config.Permissions = PermissionConfigLoader.LoadFromDefaultLocations(Directory.GetCurrentDirectory());

        // 6. Auto-enable LMSupply if no API provider is configured
        if (!HasAnyApiProvider(config))
        {
            config.LMSupply.Enabled = true;
        }

        _cachedConfig = config;
        return config;
    }

    /// <summary>Persists the given config to the global config.yaml (YAML) and invalidates the cache.</summary>
    public void SaveGlobal(IronHiveConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_globalConfigPath)!);
        File.WriteAllText(_globalConfigPath, YamlConfigSerializer.Serialize(config));
        _cachedConfig = null;
    }

    /// <summary>Gets a config value by dot-notation key (e.g. "openai.apiKey") from config.yaml.</summary>
    public string? GetValue(string key)
    {
        var node = ReadConfigAsJsonNode();
        return node is null ? null : GetNestedValue(node, key);
    }

    /// <summary>Sets a config value by dot-notation key in config.yaml, then invalidates the cache.</summary>
    public void SetValue(string key, string value)
    {
        var node = ReadConfigAsJsonNode() ?? new JsonObject();
        SetNestedValue(node, key, value);
        WriteJsonNodeAsYaml(node);
        _cachedConfig = null;
    }

    /// <summary>Removes a config value by dot-notation key. Returns true if removed.</summary>
    public bool UnsetValue(string key)
    {
        var node = ReadConfigAsJsonNode();
        if (node is null)
        {
            return false;
        }

        var removed = RemoveNestedValue(node, key);
        if (removed)
        {
            WriteJsonNodeAsYaml(node);
            _cachedConfig = null;
        }

        return removed;
    }

    /// <summary>Lists all config values as flat dot-notation key/value pairs.</summary>
    public IReadOnlyDictionary<string, string> ListAll()
    {
        var result = new Dictionary<string, string>();
        if (ReadConfigAsJsonNode() is JsonObject obj)
        {
            FlattenJsonObject(obj, string.Empty, result);
        }

        return result;
    }

    /// <summary>
    /// Gets the path to the global config file.
    /// </summary>
    public string GlobalConfigPath => _globalConfigPath;

    /// <summary>
    /// Gets the path to the project config file.
    /// </summary>
    public string ProjectConfigPath => Path.Combine(_projectRoot, ".ironhive", "config.yaml");

    /// <summary>
    /// Returns the top-level YAML keys in <paramref name="yaml"/> that do not correspond to a
    /// known <see cref="IronHiveConfig"/> section (its <see cref="YamlMemberAttribute.Alias"/> if
    /// annotated, else the CamelCaseNamingConvention-derived key). Comparison is ordinal/
    /// case-sensitive, so a wrong-case key (e.g. "openAI" instead of "openai") is reported as
    /// unknown even though it "looks" close to a valid section — this is intentional: it is the
    /// exact silent-drop defect this hardening closes. Malformed YAML is not reported here (it is
    /// handled separately by <see cref="MergeFromYaml"/>'s own try/catch blocks).
    /// </summary>
    public static IReadOnlyList<string> FindUnknownTopLevelKeys(string yaml)
    {
        var known = typeof(IronHiveConfig).GetProperties()
            .Select(p => p.GetCustomAttribute<YamlMemberAttribute>()?.Alias
                         ?? (char.ToLowerInvariant(p.Name[0]) + p.Name[1..]))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = new List<string>();
        try
        {
            var root = YamlConfigSerializer.Deserialize<Dictionary<string, object>>(yaml);
            if (root != null)
            {
                foreach (var key in root.Keys)
                {
                    if (!known.Contains(key))
                    {
                        unknown.Add(key);
                    }
                }
            }
        }
        catch
        {
            // Malformed YAML is handled by MergeFromYaml's own catch blocks.
        }

        return unknown;
    }

    private void MergeFromYaml(IronHiveConfig config, string path)
    {
        try
        {
            var yaml = File.ReadAllText(path);

            foreach (var key in FindUnknownTopLevelKeys(yaml))
            {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance-critical paths
                _logger?.LogWarning("Unknown config key '{Key}' in {Path} ignored", key, path);
#pragma warning restore CA1848
            }

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

    /// <summary>
    /// Checks if any API provider is configured.
    /// </summary>
    private static bool HasAnyApiProvider(IronHiveConfig config) =>
        config.GpuStack.IsConfigured || config.OpenAI.IsConfigured || config.Anthropic.IsConfigured ||
        config.GoogleAI.IsConfigured || config.Xai.IsConfigured || config.AzureOpenAI.IsConfigured ||
        config.Ollama.IsConfigured || config.LMStudio.IsConfigured;

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

    // --- YAML <-> JsonNode bridge for key-path mutation (GetValue/SetValue/UnsetValue/ListAll) ---
    // Uses dotted-key JsonNode navigation so a SetValue writes the same clean aliased top-level
    // keys (e.g. "openai") that Load()/MergeFromYaml read via YamlConfigSerializer — no silent
    // ignore between the mutation API and the typed loader.

    private JsonNode? ReadConfigAsJsonNode()
    {
        if (!File.Exists(_globalConfigPath))
        {
            return null;
        }

        try
        {
            var yaml = File.ReadAllText(_globalConfigPath);
            var obj = new DeserializerBuilder().Build().Deserialize<object?>(yaml);
            if (obj is null)
            {
                return null;
            }

            var json = new SerializerBuilder().JsonCompatible().Build().Serialize(obj);
            return JsonNode.Parse(json);
        }
        catch (YamlException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteJsonNodeAsYaml(JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_globalConfigPath)!);
        var obj = new DeserializerBuilder().Build().Deserialize<object?>(node.ToJsonString());
        var yaml = new SerializerBuilder().Build().Serialize(obj ?? new Dictionary<string, object>());
        File.WriteAllText(_globalConfigPath, yaml);
    }

    // --- Pure JsonNode/string helpers below (no instance state). ---

    private static string? GetNestedValue(JsonNode node, string key)
    {
        var parts = key.Split('.');
        var current = node;

        foreach (var part in parts)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(part, out var next))
            {
                current = next;
            }
            else
            {
                // Try case-insensitive match
                if (current is JsonObject objCi)
                {
                    var match = objCi.FirstOrDefault(p =>
                        p.Key.Equals(part, StringComparison.OrdinalIgnoreCase));
                    if (match.Value is not null)
                    {
                        current = match.Value;
                        continue;
                    }
                }
                return null;
            }
        }

        if (current is null)
        {
            return null;
        }

        if (current is JsonValue jsonValue)
        {
            return jsonValue.TryGetValue<string>(out var stringValue) ? stringValue : jsonValue.ToString();
        }

        return current.ToString();
    }

    private static void SetNestedValue(JsonNode root, string key, string value)
    {
        var parts = key.Split('.');
        var current = root.AsObject();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            var camelPart = ToCamelCase(part);

            if (!current.TryGetPropertyValue(camelPart, out var next) || next is not JsonObject)
            {
                var newObj = new JsonObject();
                current[camelPart] = newObj;
                current = newObj;
            }
            else
            {
                current = next.AsObject();
            }
        }

        var finalKey = ToCamelCase(parts[^1]);

        // Try to parse as appropriate type
        if (bool.TryParse(value, out var boolValue))
        {
            current[finalKey] = boolValue;
        }
        else if (int.TryParse(value, out var intValue))
        {
            current[finalKey] = intValue;
        }
        else
        {
            current[finalKey] = value;
        }
    }

    private static bool RemoveNestedValue(JsonNode root, string key)
    {
        var parts = key.Split('.');
        var current = root.AsObject();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            var camelPart = ToCamelCase(part);

            if (current.TryGetPropertyValue(camelPart, out var next) && next is JsonObject nextObj)
            {
                current = nextObj;
            }
            else
            {
                return false;
            }
        }

        var finalKey = ToCamelCase(parts[^1]);
        return current.Remove(finalKey);
    }

    private static void FlattenJsonObject(JsonObject obj, string prefix, Dictionary<string, string> result)
    {
        foreach (var prop in obj)
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Key : $"{prefix}.{prop.Key}";

            if (prop.Value is JsonObject nested)
            {
                FlattenJsonObject(nested, key, result);
            }
            else if (prop.Value is not null)
            {
                var value = prop.Value.ToString();

                // Mask sensitive values
                if (key.Contains("apiKey", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("api_key", StringComparison.OrdinalIgnoreCase))
                {
                    value = MaskValue(value);
                }

                result[key] = value;
            }
        }
    }

    private static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Handle snake_case
        if (input.Contains('_'))
        {
            var parts = input.Split('_');
            return parts[0].ToLowerInvariant() +
                string.Concat(parts.Skip(1).Select(p =>
                    char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        }

        // Simple lowercase first char
        return char.ToLowerInvariant(input[0]) + input[1..];
    }

    private static string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 8)
        {
            return "***";
        }

        return value[..4] + "..." + value[^4..];
    }
}
