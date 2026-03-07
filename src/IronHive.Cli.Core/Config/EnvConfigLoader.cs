using IronHive.Cli.Core.Permissions;

namespace IronHive.Cli.Core.Config;

/// <summary>
/// Loads configuration from settings.json using the provided SettingsManager.
/// </summary>
public class EnvConfigLoader
{
    private readonly SettingsManager _settings;

    /// <summary>
    /// Creates a new EnvConfigLoader with the specified SettingsManager.
    /// </summary>
    public EnvConfigLoader(SettingsManager settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Loads configuration from settings.json.
    /// </summary>
    /// <returns>Loaded configuration.</returns>
    public IronHiveConfig Load()
    {
        // Load from settings.json
        var config = _settings.Load();

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
