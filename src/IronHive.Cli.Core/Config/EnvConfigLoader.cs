using DotNetEnv;

namespace IronHive.Cli.Core.Config;

/// <summary>
/// Loads configuration from .env files and environment variables.
/// </summary>
public static class EnvConfigLoader
{
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

        return new IronHiveConfig
        {
            GpuStack = LoadGpuStackConfig(),
            LMSupply = LoadLMSupplyConfig()
        };
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

        return new LMSupplyConfig
        {
            Enabled = string.IsNullOrEmpty(enabled) || enabled.Equals("true", StringComparison.OrdinalIgnoreCase),
            EmbedderModel = string.IsNullOrEmpty(embedderModel) ? "auto" : embedderModel,
            RerankerModel = string.IsNullOrEmpty(rerankerModel) ? "auto" : rerankerModel,
            GeneratorModel = string.IsNullOrEmpty(generatorModel) ? "gguf:default" : generatorModel
        };
    }

    private static string? GetEnvVar(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}
