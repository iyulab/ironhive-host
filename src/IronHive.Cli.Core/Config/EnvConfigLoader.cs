using System.Text.Json;
using DotNetEnv;
using IronHive.Cli.Core.Permissions;

namespace IronHive.Cli.Core.Config;

/// <summary>
/// Loads configuration from .env files, YAML files, and environment variables.
/// </summary>
public static class EnvConfigLoader
{
    private const string ConfigFileName = "ironhive.yaml";
    private const string ConfigJsonFileName = "ironhive.json";
    private const string ConfigDirName = ".ironhive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads configuration from .env file and environment variables.
    /// </summary>
    /// <param name="envFilePath">Path to .env file. If null, searches in current and parent directories.</param>
    /// <returns>Loaded configuration.</returns>
    public static IronHiveConfig Load(string? envFilePath = null)
    {
        // Load .env file if exists
        var envFile = FindEnvFile(envFilePath);
        if (envFile != null && File.Exists(envFile))
        {
            Env.Load(envFile);
        }

        // Create base config from environment
        // LMSupply is always enabled as fallback (unless explicitly disabled)
        var config = new IronHiveConfig
        {
            GpuStack = LoadGpuStackConfig(),
            LMSupply = LoadLMSupplyConfig()
        };

        // Load permission config from default locations
        config.Permissions = LoadPermissionConfig();

        return config;
    }

    private static string? FindEnvFile(string? envFilePath)
    {
        if (envFilePath != null)
        {
            return envFilePath;
        }

        // Search for .env in current directory and parent directories
        var directory = Directory.GetCurrentDirectory();
        while (directory != null)
        {
            var envFile = Path.Combine(directory, ".env");
            if (File.Exists(envFile))
            {
                return envFile;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static GpuStackConfig LoadGpuStackConfig()
    {
        return new GpuStackConfig
        {
            Endpoint = GetEnvVar("GPUSTACK_ENDPOINT"),
            ApiKey = GetEnvVar("GPUSTACK_API_KEY"),
            Model = GetEnvVar("GPUSTACK_MODEL"),
            EmbeddingModel = GetEnvVar("GPUSTACK_EMBEDDING_MODEL"),
            RerankModel = GetEnvVar("GPUSTACK_RERANK_MODEL")
        };
    }

    private static LMSupplyConfig LoadLMSupplyConfig()
    {
        var enabled = GetEnvVar("LMSUPPLY_ENABLED");
        var embedderModel = GetEnvVar("LMSUPPLY_EMBEDDER_MODEL");
        var rerankerModel = GetEnvVar("LMSUPPLY_RERANKER_MODEL");
        var generatorModel = GetEnvVar("LMSUPPLY_GENERATOR_MODEL");
        var maxContextLength = GetEnvVar("LMSUPPLY_MAX_CONTEXT");

        // LMSupply is available as an optional local provider (not fallback)
        // Must be explicitly enabled with LMSUPPLY_ENABLED=true
        var isEnabled = !string.IsNullOrEmpty(enabled) &&
                        enabled.Equals("true", StringComparison.OrdinalIgnoreCase);

        // Parse max context length (null = auto-detect based on available RAM)
        int? parsedMaxContext = null;
        if (!string.IsNullOrEmpty(maxContextLength) &&
            int.TryParse(maxContextLength, out var contextValue) &&
            contextValue > 0)
        {
            parsedMaxContext = contextValue;
        }

        return new LMSupplyConfig
        {
            Enabled = isEnabled,
            EmbedderModel = string.IsNullOrEmpty(embedderModel) ? "auto" : embedderModel,
            RerankerModel = string.IsNullOrEmpty(rerankerModel) ? "auto" : rerankerModel,
            GeneratorModel = string.IsNullOrEmpty(generatorModel) ? "gguf:default" : generatorModel,
            MaxContextLength = parsedMaxContext
        };
    }

    private static string? GetEnvVar(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    private static PermissionConfig LoadPermissionConfig()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        return PermissionConfigLoader.LoadFromDefaultLocations(workingDirectory);
    }

    private static string? FindConfigFile()
    {
        var directory = Directory.GetCurrentDirectory();

        while (directory != null)
        {
            // Check for config directory
            var configDir = Path.Combine(directory, ConfigDirName);
            if (Directory.Exists(configDir))
            {
                var yamlFile = Path.Combine(configDir, "config.yaml");
                if (File.Exists(yamlFile))
                {
                    return yamlFile;
                }

                var ymlFile = Path.Combine(configDir, "config.yml");
                if (File.Exists(ymlFile))
                {
                    return ymlFile;
                }

                var jsonFile = Path.Combine(configDir, "config.json");
                if (File.Exists(jsonFile))
                {
                    return jsonFile;
                }
            }

            // Check for root config files
            var rootYaml = Path.Combine(directory, ConfigFileName);
            if (File.Exists(rootYaml))
            {
                return rootYaml;
            }

            var rootJson = Path.Combine(directory, ConfigJsonFileName);
            if (File.Exists(rootJson))
            {
                return rootJson;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

}
