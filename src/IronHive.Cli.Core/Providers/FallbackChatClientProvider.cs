using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// Composite chat client provider with automatic fallback.
/// Tries providers in order until one succeeds.
/// </summary>
public sealed class FallbackChatClientProvider : IChatClientProvider
{
    private readonly IChatClientProvider[] _providers;
    private IChatClientProvider? _activeProvider;

    public FallbackChatClientProvider(params IChatClientProvider[] providers)
    {
        if (providers == null || providers.Length == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }

        _providers = providers;
    }

    /// <inheritdoc />
    public string ProviderName => _activeProvider?.ProviderName ?? "fallback";

    /// <inheritdoc />
    public bool IsAvailable => _providers.Any(p => p.IsAvailable);

    /// <inheritdoc />
    public IChatClient GetChatClient() => GetChatClient(null);

    /// <inheritdoc />
    public IChatClient GetChatClient(string? modelOverride)
    {
        if (_activeProvider != null)
        {
            return _activeProvider.GetChatClient(modelOverride);
        }

        foreach (var provider in _providers)
        {
            if (provider.IsAvailable)
            {
                _activeProvider = provider;
                return provider.GetChatClient(modelOverride);
            }
        }

        throw new InvalidOperationException("No available chat client provider.");
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable)
            {
                continue;
            }

            if (await provider.CheckHealthAsync(cancellationToken))
            {
                _activeProvider = provider;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the currently active provider.
    /// </summary>
    public IChatClientProvider? ActiveProvider => _activeProvider;

    /// <summary>
    /// Forces a switch to the next available provider.
    /// </summary>
    public async Task<bool> SwitchToNextProviderAsync(CancellationToken cancellationToken = default)
    {
        var startIndex = _activeProvider != null
            ? Array.IndexOf(_providers, _activeProvider) + 1
            : 0;

        for (var i = startIndex; i < _providers.Length; i++)
        {
            var provider = _providers[i];
            if (provider.IsAvailable && await provider.CheckHealthAsync(cancellationToken))
            {
                _activeProvider = provider;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }
}
