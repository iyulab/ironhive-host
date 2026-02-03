namespace IronHive.Cli.Core.Exceptions;

/// <summary>
/// Base exception for IronHive-specific errors.
/// </summary>
public class IronHiveException : Exception
{
    /// <summary>
    /// Error code for categorization.
    /// </summary>
    public string? ErrorCode { get; }

    public IronHiveException(string message) : base(message)
    {
    }

    public IronHiveException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public IronHiveException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public IronHiveException(string message, string errorCode, Exception innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Exception thrown when configuration is invalid or missing.
/// </summary>
public class ConfigurationException : IronHiveException
{
    public string? ConfigPath { get; }

    public ConfigurationException(string message) : base(message, "CONFIG_ERROR")
    {
    }

    public ConfigurationException(string message, string configPath) : base(message, "CONFIG_ERROR")
    {
        ConfigPath = configPath;
    }

    public ConfigurationException(string message, string configPath, Exception innerException)
        : base(message, "CONFIG_ERROR", innerException)
    {
        ConfigPath = configPath;
    }
}

/// <summary>
/// Exception thrown when a provider is not available or fails.
/// </summary>
public class ProviderException : IronHiveException
{
    public string? ProviderName { get; }

    public ProviderException(string message) : base(message, "PROVIDER_ERROR")
    {
    }

    public ProviderException(string message, string providerName) : base(message, "PROVIDER_ERROR")
    {
        ProviderName = providerName;
    }

    public ProviderException(string message, string providerName, Exception innerException)
        : base(message, "PROVIDER_ERROR", innerException)
    {
        ProviderName = providerName;
    }
}

/// <summary>
/// Exception thrown when MCP plugin operations fail.
/// </summary>
public class McpPluginException : IronHiveException
{
    public string? PluginName { get; }

    public McpPluginException(string message) : base(message, "MCP_ERROR")
    {
    }

    public McpPluginException(string message, string pluginName) : base(message, "MCP_ERROR")
    {
        PluginName = pluginName;
    }

    public McpPluginException(string message, string pluginName, Exception innerException)
        : base(message, "MCP_ERROR", innerException)
    {
        PluginName = pluginName;
    }
}

/// <summary>
/// Exception thrown when session operations fail.
/// </summary>
public class SessionException : IronHiveException
{
    public string? SessionId { get; }

    public SessionException(string message) : base(message, "SESSION_ERROR")
    {
    }

    public SessionException(string message, string sessionId) : base(message, "SESSION_ERROR")
    {
        SessionId = sessionId;
    }

    public SessionException(string message, string sessionId, Exception innerException)
        : base(message, "SESSION_ERROR", innerException)
    {
        SessionId = sessionId;
    }
}
