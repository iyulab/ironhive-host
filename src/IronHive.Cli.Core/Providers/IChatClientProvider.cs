using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// Provider interface for obtaining IChatClient instances.
/// </summary>
public interface IChatClientProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the provider name (e.g., "gpustack", "lmsupply").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether this provider is available and configured.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the IChatClient instance for this provider using the default model.
    /// </summary>
    IChatClient GetChatClient();

    /// <summary>
    /// Gets the IChatClient instance for this provider with an optional model override.
    /// </summary>
    /// <param name="modelOverride">Model to use instead of the default. If null, uses the default model.</param>
    IChatClient GetChatClient(string? modelOverride);

    /// <summary>
    /// Checks if the provider's backend service is reachable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the service is reachable.</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
