using System.Text.RegularExpressions;

namespace IronHive.Cli.Core.Config;

/// <summary>
/// Configuration for IronHive CLI.
/// Loaded from .env file and environment variables.
/// </summary>
public class IronHiveConfig
{
    /// <summary>
    /// GpuStack configuration (primary provider).
    /// </summary>
    public GpuStackConfig GpuStack { get; set; } = new();

    /// <summary>
    /// LMSupply configuration (fallback provider).
    /// </summary>
    public LMSupplyConfig LMSupply { get; set; } = new();

    /// <summary>
    /// Approval configuration for HITL (Human-In-The-Loop).
    /// </summary>
    public ApprovalConfig Approval { get; set; } = new();
}

/// <summary>
/// Configuration for automatic approval patterns.
/// </summary>
public class ApprovalConfig
{
    /// <summary>
    /// Tools that are always auto-approved.
    /// </summary>
    public List<string> AutoApprovedTools { get; set; } = [];

    /// <summary>
    /// Shell command patterns that are auto-approved (glob-style).
    /// Examples: "git *", "dotnet build", "npm install"
    /// </summary>
    public List<string> AutoApprovedCommands { get; set; } = [];

    /// <summary>
    /// File path patterns that are auto-approved for write/delete.
    /// Examples: "*.tmp", "obj/**", "bin/**"
    /// </summary>
    public List<string> AutoApprovedPaths { get; set; } = [];

    /// <summary>
    /// Whether to prompt for approval on high-risk operations even if whitelisted.
    /// </summary>
    public bool AlwaysPromptForCritical { get; set; } = true;

    /// <summary>
    /// Checks if a tool is auto-approved.
    /// </summary>
    public bool IsToolAutoApproved(string toolName)
    {
        return AutoApprovedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a shell command matches any auto-approved pattern.
    /// </summary>
    public bool IsCommandAutoApproved(string command)
    {
        foreach (var pattern in AutoApprovedCommands)
        {
            if (MatchesGlobPattern(command, pattern))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a file path matches any auto-approved pattern.
    /// </summary>
    public bool IsPathAutoApproved(string path)
    {
        foreach (var pattern in AutoApprovedPaths)
        {
            if (MatchesGlobPattern(path, pattern))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesGlobPattern(string input, string pattern)
    {
        // Convert glob pattern to regex
        // * matches any characters except path separator
        // ** matches any characters including path separator
        // ? matches single character
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/\\\\]*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// GpuStack/OpenAI-compatible API configuration.
/// </summary>
public class GpuStackConfig
{
    /// <summary>
    /// API endpoint URL.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name for chat/completion.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Model name for embeddings (optional, uses Model if not set).
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// Model name for reranking (optional).
    /// </summary>
    public string? RerankModel { get; set; }

    /// <summary>
    /// Gets whether GpuStack is configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) &&
        !string.IsNullOrEmpty(ApiKey) &&
        !string.IsNullOrEmpty(Model);
}

/// <summary>
/// LMSupply local inference configuration.
/// </summary>
public class LMSupplyConfig
{
    /// <summary>
    /// Whether LMSupply fallback is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Embedder model identifier ("auto", "default", or HuggingFace model ID).
    /// </summary>
    public string EmbedderModel { get; set; } = "auto";

    /// <summary>
    /// Reranker model identifier ("auto", "default", or HuggingFace model ID).
    /// </summary>
    public string RerankerModel { get; set; } = "auto";

    /// <summary>
    /// Generator model identifier ("auto", "gguf:default", or HuggingFace model ID).
    /// </summary>
    public string GeneratorModel { get; set; } = "gguf:default";
}
