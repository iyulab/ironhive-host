using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using IronHive.Agent.Permissions;
using IronHive.Host.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
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

    /// <param name="projectRoot">
    /// Directory whose <c>.ironhive/config.yaml</c>, <c>.ironhive/permissions.{yaml,yml,json}</c> and <c>.env</c>
    /// form the project scope. Defaults to the process working directory.
    /// </param>
    /// <param name="globalConfigPath">Global config file. Defaults to <c>~/.ironhive/config.yaml</c>.</param>
    /// <param name="logger">Optional logger.</param>
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

        // 1-2. The global, then the project config.yaml, laid over each other as YAML trees and read once: a key the
        //      project file sets wins, a key it does not set keeps the global value — whatever its section or type.
        //      (A field-by-field merge of two deserialized objects cannot tell «not set» from «set to the default», and
        //      had to be extended for every new setting; sections it did not list were never read.)
        var merged = new YamlMappingNode();
        if (File.Exists(_globalConfigPath))
        {
            OverlayFile(merged, _globalConfigPath);
        }

        var projectConfigPath = Path.Combine(_projectRoot, ".ironhive", "config.yaml");
        if (File.Exists(projectConfigPath))
        {
            OverlayFile(merged, projectConfigPath);
        }

        var config = merged.Children.Count == 0 ? new IronHiveConfig() : Read(merged);

        // 3. Load .env file
        var dotEnvPath = Path.Combine(_projectRoot, ".env");
        if (File.Exists(dotEnvPath))
        {
            DotNetEnv.Env.Load(dotEnvPath);
        }

        // 4. Apply environment variables (highest priority)
        ApplyEnvironmentVariables(config);

        // 5. Permission rules, from the same project scope as config.yaml and .env (a projectRoot keeps whatever sits in
        //    the process working directory out of the config): the project's permission file when there is one;
        //    otherwise the `permissions` section config.yaml gave (global, then project); otherwise the built-in default.
        //    Before, the file lookup replaced the config.yaml rules even when there was no file, so they never applied.
        if (PermissionConfigLoader.TryLoadFromDefaultLocations(_projectRoot, out var fromFile))
        {
            config.Permissions = fromFile;
        }
        else
        {
            config.Permissions.WorkingDirectory ??= _projectRoot;
        }

        // 6. Auto-enable LMSupply if no API provider is configured — unless LMSUPPLY_ENABLED says otherwise.
        if (!HasAnyApiProvider(config) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LMSUPPLY_ENABLED")))
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
    /// Returns every YAML key in <paramref name="yaml"/> that the loader does not read, as a dotted path
    /// (<c>lmsupply.embedderModel</c>, <c>delegation.agents[0].bogus</c>; an unknown section is one entry, not one per
    /// member). A key matches a property by its <see cref="YamlMemberAttribute.Alias"/> if annotated, else by the
    /// CamelCaseNamingConvention-derived name. Comparison is ordinal/case-sensitive, so a wrong-case key (e.g. "openAI"
    /// instead of "openai") is reported even though it "looks" close — the deserializer ignores unmatched members
    /// without a word, so this list is the only place a misspelled or removed setting shows up. The keys of a dictionary
    /// member are data, not settings, and are not checked (its values are). Malformed YAML is not reported here (it is
    /// handled separately by <see cref="OverlayFile"/>'s own try/catch blocks).
    /// </summary>
    public static IReadOnlyList<string> FindUnknownKeys(string yaml)
    {
        var unknown = new List<string>();
        try
        {
            var root = YamlConfigSerializer.Deserialize<Dictionary<object, object>>(yaml);
            if (root != null)
            {
                CollectUnknownKeys(root, typeof(IronHiveConfig), prefix: "", unknown);
            }
        }
        catch
        {
            // Malformed YAML is handled by OverlayFile's own catch blocks.
        }

        return unknown;
    }

    private static void CollectUnknownKeys(object? node, Type type, string prefix, List<string> unknown)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (node is null || type == typeof(string) || type == typeof(object) || type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        var dictionaryValue = DictionaryValueType(type);
        if (dictionaryValue != null)
        {
            if (node is IDictionary<object, object> entries)
            {
                foreach (var (key, value) in entries)
                {
                    CollectUnknownKeys(value, dictionaryValue, $"{prefix}.{key}", unknown);
                }
            }

            return;
        }

        var itemType = ListItemType(type);
        if (itemType != null)
        {
            if (node is IList<object> items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    CollectUnknownKeys(items[i], itemType, $"{prefix}[{i}]", unknown);
                }
            }

            return;
        }

        if (node is not IDictionary<object, object> members || !type.IsClass)
        {
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<YamlIgnoreAttribute>() == null)
            .ToDictionary(
                p => p.GetCustomAttribute<YamlMemberAttribute>()?.Alias ?? (char.ToLowerInvariant(p.Name[0]) + p.Name[1..]),
                StringComparer.Ordinal);

        foreach (var (rawKey, value) in members)
        {
            var key = rawKey?.ToString() ?? string.Empty;
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (properties.TryGetValue(key, out var property))
            {
                CollectUnknownKeys(value, property.PropertyType, path, unknown);
            }
            else
            {
                unknown.Add(path);
            }
        }
    }

    private static Type? DictionaryValueType(Type type) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() is var d &&
         (d == typeof(Dictionary<,>) || d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)))
            ? type.GetGenericArguments()[1]
            : null;

    private static Type? ListItemType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() is var d &&
               (d == typeof(List<>) || d == typeof(IList<>) || d == typeof(IReadOnlyList<>) || d == typeof(IEnumerable<>) ||
                d == typeof(ICollection<>) || d == typeof(IReadOnlyCollection<>))
            ? type.GetGenericArguments()[0]
            : null;
    }

    /// <summary>
    /// Reads one config file and lays it over <paramref name="merged"/>. A file that cannot be read, is not YAML, or does
    /// not deserialize into <see cref="IronHiveConfig"/> (a word where a number belongs) is skipped as a whole, with a
    /// warning, so it cannot leave half its settings applied. Keys the loader does not read are reported.
    /// </summary>
    private void OverlayFile(YamlMappingNode merged, string path)
    {
        try
        {
            var yaml = File.ReadAllText(path);

            foreach (var key in FindUnknownKeys(yaml))
            {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance-critical paths
                _logger?.LogWarning("Unknown config key '{Key}' in {Path} ignored", key, path);
#pragma warning restore CA1848
            }

            // Values must fit their members before the file joins the merge.
            _ = YamlConfigSerializer.Deserialize<IronHiveConfig>(yaml);

            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root)
            {
                Overlay(merged, root, atRoot: true);
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
    /// Lays <paramref name="source"/> over <paramref name="target"/>: mappings merge key by key, anything else (a scalar, a
    /// list) replaces — a list is one decision, never a union with another scope's. Three top-level sections replace as a
    /// whole: <c>permissions</c> (a scope's rules are a complete policy), <c>budget</c> (a scope's threshold and stop flag
    /// belong to its limits) and <c>skills</c> (the roots, the enabled and excluded lists and the size limit are one
    /// choice).
    /// </summary>
    private static void Overlay(YamlMappingNode target, YamlMappingNode source, bool atRoot)
    {
        foreach (var (key, value) in source.Children)
        {
            var replaceWhole = atRoot && key is YamlScalarNode { Value: "permissions" or "budget" or "skills" };
            if (!replaceWhole && value is YamlMappingNode sourceSection &&
                target.Children.TryGetValue(key, out var existing) && existing is YamlMappingNode targetSection)
            {
                Overlay(targetSection, sourceSection, atRoot: false);
                continue;
            }

            target.Children[key] = value;
        }
    }

    private static IronHiveConfig Read(YamlMappingNode merged)
    {
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(merged)).Save(writer, assignAnchors: false);
        return YamlConfigSerializer.Deserialize<IronHiveConfig>(writer.ToString()) ?? new IronHiveConfig();
    }

    /// <summary>
    /// Checks if any API provider is configured.
    /// </summary>
    private static bool HasAnyApiProvider(IronHiveConfig config) =>
        config.GpuStack.IsConfigured || config.OpenAI.IsConfigured || config.Anthropic.IsConfigured ||
        config.GoogleAI.IsConfigured || config.Xai.IsConfigured ||
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
    // keys (e.g. "openai") that Load()/OverlayFile read via YamlConfigSerializer — no silent
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
                // Exact keys only, like the loader: a wrong-case key is not in force, so it has no value to show.
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
