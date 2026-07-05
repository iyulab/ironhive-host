using System.Text;

namespace IronHive.Host.Config;

/// <summary>
/// Writes configuration values to .env or YAML config files.
/// </summary>
public class ConfigWriter
{
    private static readonly Dictionary<string, string> KeyToEnvMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // GpuStack mappings
        { "gpustack.endpoint", "GPUSTACK_ENDPOINT" },
        { "gpustack.apikey", "GPUSTACK_API_KEY" },
        { "gpustack.api_key", "GPUSTACK_API_KEY" },
        { "gpustack.model", "GPUSTACK_MODEL" },
        { "gpustack.embeddingmodel", "GPUSTACK_EMBEDDING_MODEL" },
        { "gpustack.embedding_model", "GPUSTACK_EMBEDDING_MODEL" },
        { "gpustack.rerankmodel", "GPUSTACK_RERANK_MODEL" },
        { "gpustack.rerank_model", "GPUSTACK_RERANK_MODEL" },

        // LMSupply mappings
        { "lmsupply.enabled", "LMSUPPLY_ENABLED" },
        { "lmsupply.embeddermodel", "LMSUPPLY_EMBEDDER_MODEL" },
        { "lmsupply.embedder_model", "LMSUPPLY_EMBEDDER_MODEL" },
        { "lmsupply.rerankermodel", "LMSUPPLY_RERANKER_MODEL" },
        { "lmsupply.reranker_model", "LMSUPPLY_RERANKER_MODEL" },
        { "lmsupply.generatormodel", "LMSUPPLY_GENERATOR_MODEL" },
        { "lmsupply.generator_model", "LMSUPPLY_GENERATOR_MODEL" },
        { "lmsupply.maxcontextlength", "LMSUPPLY_MAX_CONTEXT" },
        { "lmsupply.max_context", "LMSUPPLY_MAX_CONTEXT" },
        { "lmsupply.max_context_length", "LMSUPPLY_MAX_CONTEXT" },

        // OpenAI mappings
        { "openai.apikey", "OPENAI_API_KEY" },
        { "openai.api_key", "OPENAI_API_KEY" },
        { "openai.model", "OPENAI_MODEL" },
        { "openai.endpoint", "OPENAI_ENDPOINT" },

        // Anthropic mappings
        { "anthropic.apikey", "ANTHROPIC_API_KEY" },
        { "anthropic.api_key", "ANTHROPIC_API_KEY" },
        { "anthropic.model", "ANTHROPIC_MODEL" },

        // GoogleAI mappings
        { "google.apikey", "GOOGLE_API_KEY" },
        { "google.api_key", "GOOGLE_API_KEY" },
        { "google.model", "GOOGLE_MODEL" },
        { "googleai.apikey", "GOOGLE_API_KEY" },
        { "googleai.api_key", "GOOGLE_API_KEY" },
        { "googleai.model", "GOOGLE_MODEL" },

        // XAI mappings
        { "xai.endpoint", "XAI_ENDPOINT" },
        { "xai.apikey", "XAI_API_KEY" },
        { "xai.api_key", "XAI_API_KEY" },
        { "xai.model", "XAI_MODEL" },

        // Azure OpenAI mappings
        { "azure.endpoint", "AZURE_OPENAI_ENDPOINT" },
        { "azure.apikey", "AZURE_OPENAI_API_KEY" },
        { "azure.api_key", "AZURE_OPENAI_API_KEY" },
        { "azure.deployment", "AZURE_OPENAI_DEPLOYMENT" },
        { "azure.deploymentname", "AZURE_OPENAI_DEPLOYMENT" },
        { "azure.deployment_name", "AZURE_OPENAI_DEPLOYMENT" },
        { "azureopenai.endpoint", "AZURE_OPENAI_ENDPOINT" },
        { "azureopenai.apikey", "AZURE_OPENAI_API_KEY" },
        { "azureopenai.api_key", "AZURE_OPENAI_API_KEY" },
        { "azureopenai.deployment", "AZURE_OPENAI_DEPLOYMENT" }
    };

    /// <summary>
    /// Sets a configuration value in the .env file.
    /// </summary>
    /// <param name="envFilePath">Path to the .env file.</param>
    /// <param name="key">Configuration key (e.g., "gpustack.endpoint" or "GPUSTACK_ENDPOINT").</param>
    /// <param name="value">Value to set.</param>
    /// <returns>True if the value was written successfully.</returns>
    public static bool SetValue(string envFilePath, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envFilePath, nameof(envFilePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        // Convert dot notation to env var name if needed
        var envKey = ConvertToEnvKey(key);

        // Read existing content or create new
        var lines = File.Exists(envFilePath)
            ? File.ReadAllLines(envFilePath).ToList()
            : [];

        // Find and update or append the key
        var updated = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].TrimStart();

            // Skip comments and empty lines
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            // Check if this line has the key
            var equalIndex = line.IndexOf('=');
            if (equalIndex > 0)
            {
                var lineKey = line[..equalIndex].Trim();
                if (lineKey.Equals(envKey, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = FormatEnvLine(envKey, value);
                    updated = true;
                    break;
                }
            }
        }

        // Append if not found
        if (!updated)
        {
            // Add a newline if file doesn't end with one
            if (lines.Count > 0 && !string.IsNullOrEmpty(lines[^1]))
            {
                lines.Add(string.Empty);
            }
            lines.Add(FormatEnvLine(envKey, value));
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(envFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write back
        File.WriteAllLines(envFilePath, lines, Encoding.UTF8);
        return true;
    }

    /// <summary>
    /// Removes a configuration value from the .env file.
    /// </summary>
    /// <param name="envFilePath">Path to the .env file.</param>
    /// <param name="key">Configuration key to remove.</param>
    /// <returns>True if the key was removed.</returns>
    public static bool RemoveValue(string envFilePath, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envFilePath, nameof(envFilePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        if (!File.Exists(envFilePath))
        {
            return false;
        }

        var envKey = ConvertToEnvKey(key);
        var lines = File.ReadAllLines(envFilePath).ToList();
        var removed = false;

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i].TrimStart();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex > 0)
            {
                var lineKey = line[..equalIndex].Trim();
                if (lineKey.Equals(envKey, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
        }

        if (removed)
        {
            File.WriteAllLines(envFilePath, lines, Encoding.UTF8);
        }

        return removed;
    }

    /// <summary>
    /// Lists all valid configuration keys.
    /// </summary>
    /// <returns>Collection of valid keys grouped by section.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetValidKeys()
    {
        var result = new Dictionary<string, List<string>>();

        foreach (var key in KeyToEnvMapping.Keys)
        {
            var parts = key.Split('.');
            if (parts.Length == 2)
            {
                var section = parts[0];
                if (!result.TryGetValue(section, out var list))
                {
                    list = [];
                    result[section] = list;
                }

                if (!list.Contains(key))
                {
                    list.Add(key);
                }
            }
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly());
    }

    /// <summary>
    /// Converts a dot-notation key to environment variable name.
    /// </summary>
    private static string ConvertToEnvKey(string key)
    {
        // If already looks like an env var (all caps with underscores), use as-is
        if (key.Equals(key.ToUpperInvariant(), StringComparison.Ordinal) && !key.Contains('.'))
        {
            return key;
        }

        // Try to map from dot notation
        if (KeyToEnvMapping.TryGetValue(key, out var envKey))
        {
            return envKey;
        }

        // Fallback: convert to uppercase with underscores
        return key.Replace('.', '_').ToUpperInvariant();
    }

    /// <summary>
    /// Formats an environment variable line, quoting value if needed.
    /// </summary>
    private static string FormatEnvLine(string key, string value)
    {
        // Quote if value contains spaces, special characters, or is empty
        if (string.IsNullOrEmpty(value) ||
            value.Contains(' ') ||
            value.Contains('"') ||
            value.Contains('\'') ||
            value.Contains('#') ||
            value.Contains('\n'))
        {
            // Escape existing quotes and use double quotes
            var escaped = value.Replace("\"", "\\\"");
            return $"{key}=\"{escaped}\"";
        }

        return $"{key}={value}";
    }
}
