using Microsoft.Extensions.Logging;

namespace IronHive.Host.Core.Config;

/// <summary>One-time migration of legacy settings.json to config.yaml.</summary>
public static class ConfigMigrator
{
    public static bool MigrateIfNeeded(string globalConfigPath, string projectRoot,
        string legacySettingsPath, ILogger? logger = null)
    {
        var projectYaml = Path.Combine(projectRoot, ".ironhive", "config.yaml");
        if (File.Exists(globalConfigPath) || File.Exists(projectYaml))
        {
            return false;
        }

        var legacy = SettingsManager.TryLoadLegacyJson(legacySettingsPath);
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
        catch (IOException ex)
        {
#pragma warning disable CA1848, CA1873
            logger?.LogWarning(ex, "Failed to migrate settings.json to config.yaml");
#pragma warning restore CA1848, CA1873
            return false;
        }
    }
}
