using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// Factory for creating IChatClient instances with runtime configuration.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Creates an IChatClient with optional model override.
    /// </summary>
    /// <param name="modelOverride">Model to use instead of the default. If null, uses the default model.</param>
    /// <returns>A configured IChatClient instance.</returns>
    IChatClient Create(string? modelOverride = null);

    /// <summary>
    /// Creates an IChatClient with a specific provider and optional model override.
    /// </summary>
    /// <param name="providerName">Provider name (e.g., "gpustack", "lmsupply").</param>
    /// <param name="modelOverride">Model to use instead of the default. If null, uses the default model.</param>
    /// <returns>A configured IChatClient instance.</returns>
    IChatClient Create(string providerName, string? modelOverride);

    /// <summary>
    /// Gets the list of available provider names.
    /// </summary>
    IReadOnlyList<string> AvailableProviders { get; }

    /// <summary>
    /// Gets the default provider name.
    /// </summary>
    string DefaultProviderName { get; }
}

/// <summary>
/// Options for creating a chat client.
/// </summary>
public record ChatClientOptions
{
    /// <summary>
    /// Provider name (e.g., "gpustack", "lmsupply"). If null, uses the default provider.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Model to use. If null, uses the provider's default model.
    /// </summary>
    public string? Model { get; init; }
}
