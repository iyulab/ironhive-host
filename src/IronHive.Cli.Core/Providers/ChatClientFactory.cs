using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// Default implementation of IChatClientFactory.
/// Creates IChatClient instances with optional model/provider override.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly IReadOnlyDictionary<string, IChatClientProvider> _providers;
    private readonly IChatClientProvider _defaultProvider;
    private readonly Func<IChatClient, IChatClient>? _clientDecorator;

    /// <summary>
    /// Creates a new ChatClientFactory.
    /// </summary>
    /// <param name="providers">Dictionary of provider name to provider instance.</param>
    /// <param name="defaultProvider">The default provider to use when no provider is specified.</param>
    /// <param name="clientDecorator">Optional decorator to apply to created clients (e.g., FunctionInvokingChatClient).</param>
    public ChatClientFactory(
        IReadOnlyDictionary<string, IChatClientProvider> providers,
        IChatClientProvider defaultProvider,
        Func<IChatClient, IChatClient>? clientDecorator = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _defaultProvider = defaultProvider ?? throw new ArgumentNullException(nameof(defaultProvider));
        _clientDecorator = clientDecorator;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableProviders =>
        _providers.Where(p => p.Value.IsAvailable).Select(p => p.Key).ToList();

    /// <inheritdoc />
    public string DefaultProviderName => _defaultProvider.ProviderName;

    /// <inheritdoc />
    public IChatClient Create(string? modelOverride = null)
    {
        var client = _defaultProvider.GetChatClient(modelOverride);
        return _clientDecorator?.Invoke(client) ?? client;
    }

    /// <inheritdoc />
    public IChatClient Create(string providerName, string? modelOverride)
    {
        if (!_providers.TryGetValue(providerName.ToLowerInvariant(), out var provider))
        {
            throw new ArgumentException($"Provider '{providerName}' not found. Available: {string.Join(", ", AvailableProviders)}", nameof(providerName));
        }

        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException($"Provider '{providerName}' is not available or configured.");
        }

        var client = provider.GetChatClient(modelOverride);
        return _clientDecorator?.Invoke(client) ?? client;
    }
}
