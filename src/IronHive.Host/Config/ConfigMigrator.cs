using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Config;

/// <summary>One-time migration of legacy settings.json to config.yaml.</summary>
public static class ConfigMigrator
{
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static bool MigrateIfNeeded(string globalConfigPath, string projectRoot,
        string legacySettingsPath, ILogger? logger = null)
    {
        var projectYaml = Path.Combine(projectRoot, ".ironhive", "config.yaml");
        if (File.Exists(globalConfigPath) || File.Exists(projectYaml))
        {
            return false;
        }

        var legacy = TryLoadLegacyJson(legacySettingsPath);
        if (legacy is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
            File.WriteAllText(globalConfigPath, YamlConfigSerializer.Serialize(legacy));
#pragma warning disable CA1848, CA1873
            logger?.LogInformation("Migrated legacy settings.json to config.yaml at {Path}", globalConfigPath);
#pragma warning restore CA1848, CA1873
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
#pragma warning disable CA1848, CA1873
            logger?.LogWarning(ex, "Failed to migrate settings.json to config.yaml");
#pragma warning restore CA1848, CA1873
            return false;
        }
    }

    /// <summary>
    /// Reads a legacy settings.json file at the given path into an IronHiveConfig.
    /// Returns null if the file does not exist or fails to parse.
    /// </summary>
    private static IronHiveConfig? TryLoadLegacyJson(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(settingsFilePath);
            return JsonSerializer.Deserialize<IronHiveConfig>(json, LegacyJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
