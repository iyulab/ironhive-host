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
    /// Gets the IChatClient instance for this provider.
    /// </summary>
    IChatClient GetChatClient();

    /// <summary>
    /// Checks if the provider's backend service is reachable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the service is reachable.</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
