using Microsoft.Extensions.AI;

namespace IronHive.Cli.Infrastructure.Delegation;

/// <summary>
/// A chat client for a delegated agent that creates the real client on its first call, with <c>await</c>. The agent
/// runtime asks for clients synchronously, while the host's client factory is asynchronous and may load a local model;
/// creating every configured agent's client when a session starts would pay that for agents the session never uses.
/// </summary>
/// <remarks>
/// It also sends the bare model id: the agent runtime puts the agent's whole deployment string in
/// <see cref="ChatOptions.ModelId"/>, and the provider sends that field as the model name. A failed or cancelled creation
/// is not kept, so the next call tries again.
/// </remarks>
internal sealed class DeferredChatClient : IChatClient
{
    private readonly Func<CancellationToken, Task<IChatClient>> _create;
    private readonly string _modelId;
    private readonly Lock _gate = new();
    private Task<IChatClient>? _client;

    public DeferredChatClient(Func<CancellationToken, Task<IChatClient>> create, string modelId)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _modelId = modelId;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetResponseAsync(messages, WithModel(options), cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var update in client.GetStreamingResponseAsync(messages, WithModel(options), cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata)
            ? new ChatClientMetadata(defaultModelId: _modelId)
            : serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        Task<IChatClient>? created;
        lock (_gate)
        {
            created = _client;
            _client = null;
        }

        if (created is { IsCompletedSuccessfully: true })
        {
            created.Result.Dispose();
        }
    }

    private async Task<IChatClient> GetClientAsync(CancellationToken cancellationToken)
    {
        Task<IChatClient> creation;
        lock (_gate)
        {
            // Created without the caller's token: one caller's cancellation must not leave a cancelled task for the next.
            creation = _client ??= _create(CancellationToken.None);
        }

        try
        {
            return await creation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (creation.IsFaulted || creation.IsCanceled)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_client, creation))
                {
                    _client = null;
                }
            }

            throw;
        }
    }

    private ChatOptions WithModel(ChatOptions? options)
    {
        var copy = options?.Clone() ?? new ChatOptions();
        copy.ModelId = _modelId;
        return copy;
    }
}
