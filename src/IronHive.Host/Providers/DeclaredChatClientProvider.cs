using System.Collections.Concurrent;
using IronHive.Agent.Providers;
using IronHive.Providers.Declarations;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Providers;

/// <summary>
/// A provider declared in the <c>providers:</c> list: a client per model, created by
/// <see cref="ProviderClientFactory"/> so a model the declaration reaches over another wire or endpoint gets its own adapter.
/// </summary>
public sealed class DeclaredChatClientProvider : IChatClientProvider
{
    private readonly ProviderDeclaration _declaration;
    private readonly string _defaultModel;
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);

    /// <param name="declaration">The declaration.</param>
    /// <param name="defaultModel">The model used when none is chosen.</param>
    public DeclaredChatClientProvider(ProviderDeclaration declaration, string defaultModel)
    {
        _declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _defaultModel = !string.IsNullOrWhiteSpace(defaultModel) ? defaultModel : throw new ArgumentException("The default model is blank.", nameof(defaultModel));
    }

    /// <inheritdoc />
    public string ProviderName => _declaration.Id;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<IChatClient> GetChatClientAsync(string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(modelOverride) ? _defaultModel : modelOverride;
        return Task.FromResult(_clients.GetOrAdd(model, m => ProviderClientFactory.Create(_declaration, m)));
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<IReadOnlyList<AvailableModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        // No wire-independent model listing exists; the models this declaration names are the ones it can say.
        var ids = new[] { _defaultModel }.Concat(_declaration.Models?.Keys ?? []).Distinct(StringComparer.Ordinal);
        IReadOnlyList<AvailableModelInfo> models =
        [
            .. ids.Select(id => new AvailableModelInfo
            {
                ModelId = id,
                Provider = _declaration.Id,
                DisplayName = id,
                Source = ModelSource.Static,
                IsDefault = string.Equals(id, _defaultModel, StringComparison.Ordinal),
            }),
        ];
        return Task.FromResult(models);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
        return ValueTask.CompletedTask;
    }
}
