using System.Text.Json;
using System.Text.Json.Nodes;

namespace IronHive.Host.Core.Config;

/// <summary>
/// Manages settings in a configurable base directory (default: ~/.ironhive/).
/// </summary>
public class SettingsManager
{
    private const string SettingsFileName = "settings.json";
    private const string DefaultDirName = ".ironhive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Gets the path to the settings directory.
    /// </summary>
    public string SettingsDirectory { get; }

    /// <summary>
    /// Gets the path to the settings file.
    /// </summary>
    public string SettingsFilePath { get; }

    /// <summary>
    /// Creates a new SettingsManager with the specified base directory.
    /// </summary>
    /// <param name="basePath">
    /// Base directory for settings. If null, defaults to ~/.ironhive/.
    /// </param>
    public SettingsManager(string? basePath = null)
    {
        SettingsDirectory = basePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DefaultDirName);
        SettingsFilePath = Path.Combine(SettingsDirectory, SettingsFileName);
    }

    /// <summary>
    /// Loads settings from settings.json in the configured directory.
    /// </summary>
    public IronHiveConfig Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new IronHiveConfig();
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<IronHiveConfig>(json, JsonOptions)
                ?? new IronHiveConfig();
        }
        catch (JsonException)
        {
            return new IronHiveConfig();
        }
    }

    /// <summary>
    /// Saves settings to settings.json in the configured directory.
    /// </summary>
    public void Save(IronHiveConfig config)
    {
        EnsureDirectoryExists();

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>
    /// Gets a value by dot-notation key (e.g., "openai.apiKey").
    /// </summary>
    public string? GetValue(string key)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return null;
            }

            return GetNestedValue(node, key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets a value by dot-notation key (e.g., "openai.apiKey").
    /// </summary>
    public void SetValue(string key, string value)
    {
        EnsureDirectoryExists();

        JsonNode root;
        if (File.Exists(SettingsFilePath))
        {
            var json = File.ReadAllText(SettingsFilePath);
            root = JsonNode.Parse(json) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        SetNestedValue(root, key, value);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(SettingsFilePath, root.ToJsonString(options));
    }

    /// <summary>
    /// Removes a value by dot-notation key.
    /// </summary>
    public bool UnsetValue(string key)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var root = JsonNode.Parse(json);
            if (root is null)
            {
                return false;
            }

            var removed = RemoveNestedValue(root, key);
            if (removed)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(SettingsFilePath, root.ToJsonString(options));
            }

            return removed;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lists all settings as flat key-value pairs.
    /// </summary>
    public IReadOnlyDictionary<string, string> ListAll()
    {
        var result = new Dictionary<string, string>();

        if (!File.Exists(SettingsFilePath))
        {
            return result;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                FlattenJsonObject(obj, string.Empty, result);
            }
        }
        catch
        {
            // Return empty on error
        }

        return result;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(SettingsDirectory))
        {
            Directory.CreateDirectory(SettingsDirectory);
        }
    }

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

        return current?.GetValue<string>();
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
