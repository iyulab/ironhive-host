using System.Text.Json;
using DotNetEnv;

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

        // Load GpuStack config first
        var gpuStackConfig = LoadGpuStackConfig();

        // Create base config from environment
        // LMSupply is auto-enabled when no remote provider is configured
        var config = new IronHiveConfig
        {
            GpuStack = gpuStackConfig,
            LMSupply = LoadLMSupplyConfig(autoEnable: !gpuStackConfig.IsConfigured)
        };

        // Load approval config from YAML/JSON file if exists
        config.Approval = LoadApprovalConfig();

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

    private static LMSupplyConfig LoadLMSupplyConfig(bool autoEnable = false)
    {
        var enabled = GetEnvVar("LMSUPPLY_ENABLED");
        var embedderModel = GetEnvVar("LMSUPPLY_EMBEDDER_MODEL");
        var rerankerModel = GetEnvVar("LMSUPPLY_RERANKER_MODEL");
        var generatorModel = GetEnvVar("LMSUPPLY_GENERATOR_MODEL");

        // Determine if LMSupply should be enabled:
        // 1. Explicitly set via LMSUPPLY_ENABLED=true
        // 2. Auto-enabled when no remote provider (GpuStack/OpenAI) is configured
        var isEnabled = !string.IsNullOrEmpty(enabled)
            ? enabled.Equals("true", StringComparison.OrdinalIgnoreCase)
            : autoEnable;

        return new LMSupplyConfig
        {
            Enabled = isEnabled,
            EmbedderModel = string.IsNullOrEmpty(embedderModel) ? "auto" : embedderModel,
            RerankerModel = string.IsNullOrEmpty(rerankerModel) ? "auto" : rerankerModel,
            GeneratorModel = string.IsNullOrEmpty(generatorModel) ? "gguf:default" : generatorModel
        };
    }

    private static string? GetEnvVar(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    private static ApprovalConfig LoadApprovalConfig()
    {
        var configFile = FindConfigFile();
        if (configFile == null)
        {
            return new ApprovalConfig();
        }

        try
        {
            var content = File.ReadAllText(configFile);

            if (configFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return LoadApprovalFromJson(content);
            }
            else if (configFile.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                     configFile.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                return LoadApprovalFromYaml(content);
            }
        }
        catch
        {
            // Silently ignore config file errors and use defaults
        }

        return new ApprovalConfig();
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

    private static ApprovalConfig LoadApprovalFromJson(string content)
    {
        var wrapper = JsonSerializer.Deserialize<ConfigWrapper>(content, JsonOptions);
        return wrapper?.Approval ?? new ApprovalConfig();
    }

    private static ApprovalConfig LoadApprovalFromYaml(string content)
    {
        // Simple YAML parsing for approval config
        // Format:
        // approval:
        //   autoApprovedTools:
        //     - tool1
        //     - tool2
        //   autoApprovedCommands:
        //     - "git *"
        //   autoApprovedPaths:
        //     - "*.tmp"
        //   alwaysPromptForCritical: true

        var config = new ApprovalConfig();
        var lines = content.Split('\n');
        var currentSection = "";
        var inApprovalSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Check for approval section
            if (trimmed.StartsWith("approval:", StringComparison.Ordinal))
            {
                inApprovalSection = true;
                continue;
            }

            // Check for other top-level sections (exit approval)
            if (!line.StartsWith(' ') && !line.StartsWith('\t') && trimmed.EndsWith(':'))
            {
                inApprovalSection = false;
                continue;
            }

            if (!inApprovalSection)
            {
                continue;
            }

            // Parse subsections
            if (trimmed.StartsWith("autoApprovedTools:", StringComparison.Ordinal))
            {
                currentSection = "tools";
                continue;
            }

            if (trimmed.StartsWith("autoApprovedCommands:", StringComparison.Ordinal))
            {
                currentSection = "commands";
                continue;
            }

            if (trimmed.StartsWith("autoApprovedPaths:", StringComparison.Ordinal))
            {
                currentSection = "paths";
                continue;
            }

            if (trimmed.StartsWith("alwaysPromptForCritical:", StringComparison.Ordinal))
            {
                var value = trimmed.Split(':')[1].Trim().ToLowerInvariant();
                config.AlwaysPromptForCritical = value == "true";
                continue;
            }

            // Parse list items
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                var value = trimmed[2..].Trim().Trim('"', '\'');
                switch (currentSection)
                {
                    case "tools":
                        config.AutoApprovedTools.Add(value);
                        break;
                    case "commands":
                        config.AutoApprovedCommands.Add(value);
                        break;
                    case "paths":
                        config.AutoApprovedPaths.Add(value);
                        break;
                }
            }
        }

        return config;
    }

    private sealed class ConfigWrapper
    {
        public ApprovalConfig? Approval { get; set; }
    }
}
