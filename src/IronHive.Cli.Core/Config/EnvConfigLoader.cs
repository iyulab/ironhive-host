using IronHive.Cli.Core.Permissions;

namespace IronHive.Cli.Core.Config;

/// <summary>
/// Loads configuration from ~/.ironhive/settings.json.
/// </summary>
public static class EnvConfigLoader
{
    /// <summary>
    /// Loads configuration from ~/.ironhive/settings.json.
    /// </summary>
    /// <returns>Loaded configuration.</returns>
    public static IronHiveConfig Load()
    {
        // Load from ~/.ironhive/settings.json
        var config = SettingsManager.Load();

        // Load permission config from default locations
        config.Permissions = LoadPermissionConfig();

        // Auto-enable LMSupply if no API provider is configured
        if (!HasAnyApiProvider(config))
        {
            config.LMSupply.Enabled = true;
        }

        return config;
    }

    /// <summary>
    /// Checks if any API provider is configured.
    /// </summary>
    private static bool HasAnyApiProvider(IronHiveConfig config)
    {
        return config.GpuStack.IsConfigured ||
               config.OpenAI.IsConfigured ||
               config.Anthropic.IsConfigured ||
               config.GoogleAI.IsConfigured ||
               config.Xai.IsConfigured ||
               config.AzureOpenAI.IsConfigured ||
               config.Ollama.IsConfigured ||
               config.LMStudio.IsConfigured;
    }

    private static PermissionConfig LoadPermissionConfig()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        return PermissionConfigLoader.LoadFromDefaultLocations(workingDirectory);
    }
}
