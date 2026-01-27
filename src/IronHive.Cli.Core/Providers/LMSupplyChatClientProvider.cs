using System.Collections.Concurrent;
using IronHive.Cli.Core.Config;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// LMSupply-based chat client provider for local inference.
/// Wraps LMSupply.Generator to implement IChatClient.
/// </summary>
public sealed class LMSupplyChatClientProvider : IChatClientProvider, IDisposable
{
    private readonly LMSupplyConfig _config;
    private readonly ConcurrentDictionary<string, (IGeneratorModel generator, LMSupplyChatClient client)> _clientCache = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _defaultModel;
    private volatile bool _initialized;
    private bool _disposed;

    public LMSupplyChatClientProvider(LMSupplyConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public string ProviderName => "lmsupply";

    /// <inheritdoc />
    public bool IsAvailable => _config.Enabled;

    /// <inheritdoc />
    public IChatClient GetChatClient() => GetChatClient(null);

    /// <inheritdoc />
    public IChatClient GetChatClient(string? modelOverride)
    {
        // Lazy initialization - automatically initialize if not done
        if (!_initialized)
        {
            EnsureInitializedAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        var model = modelOverride ?? _defaultModel!;

        // Try to get from cache, or load dynamically
        if (!_clientCache.TryGetValue(model, out var cached))
        {
            // Load new model dynamically
            cached = LoadModelSync(model);
        }

        return cached.client;
    }

    /// <summary>
    /// Synchronously loads a model (used when GetChatClient is called with a new model).
    /// </summary>
    private (IGeneratorModel generator, LMSupplyChatClient client) LoadModelSync(string modelId)
    {
        return _clientCache.GetOrAdd(modelId, id =>
        {
            var generator = BuildGeneratorAsync(id, CancellationToken.None).GetAwaiter().GetResult();
            var client = new LMSupplyChatClient(generator);
            return (generator, client);
        });
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return false;
        }

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_initialized)
            {
                return;
            }

            var modelId = _config.GeneratorModel;
            _defaultModel = modelId;

            var generator = await BuildGeneratorAsync(modelId, cancellationToken);
            var chatClient = new LMSupplyChatClient(generator);

            _clientCache[modelId] = (generator, chatClient);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<IGeneratorModel> BuildGeneratorAsync(string modelId, CancellationToken cancellationToken)
    {
        var builder = TextGeneratorBuilder.Create();

        if (modelId == "auto" || modelId == "gguf:default")
        {
            builder.WithDefaultModel();
        }
        else if (modelId.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase))
        {
            builder.WithHuggingFaceModel(modelId[5..]);
        }
        else
        {
            builder.WithHuggingFaceModel(modelId);
        }

        return await builder.BuildAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (generator, _) in _clientCache.Values)
        {
            await generator.DisposeAsync();
        }

        _clientCache.Clear();
        _initLock.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (generator, _) in _clientCache.Values)
        {
            if (generator is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _clientCache.Clear();
        _initLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// IChatClient wrapper for LMSupply IGeneratorModel.
/// </summary>
internal sealed class LMSupplyChatClient : IChatClient
{
    private readonly ITextGenerator _generator;

    public LMSupplyChatClient(ITextGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public ChatClientMetadata Metadata => new("LMSupply", null, _generator.ModelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = ConvertMessages(chatMessages);
        var genOptions = ConvertOptions(options);

        var response = await _generator.GenerateChatCompleteAsync(messages, genOptions, cancellationToken);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, response));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = ConvertMessages(chatMessages);
        var genOptions = ConvertOptions(options);

        await foreach (var token in _generator.GenerateChatAsync(messages, genOptions, cancellationToken))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, token);
        }
    }

    public TService? GetService<TService>(object? key = null) where TService : class
    {
        return this as TService;
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        return serviceType == typeof(IChatClient) ? this : null;
    }

    public void Dispose()
    {
        // Generator is disposed by the provider
    }

    private static IEnumerable<LMSupply.Generator.Models.ChatMessage> ConvertMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (var msg in messages)
        {
            var role = msg.Role.Value switch
            {
                "system" => LMSupply.Generator.Models.ChatRole.System,
                "user" => LMSupply.Generator.Models.ChatRole.User,
                "assistant" => LMSupply.Generator.Models.ChatRole.Assistant,
                _ => LMSupply.Generator.Models.ChatRole.User
            };

            yield return new LMSupply.Generator.Models.ChatMessage(role, msg.Text ?? string.Empty);
        }
    }

    private static LMSupply.Generator.Models.GenerationOptions? ConvertOptions(ChatOptions? options)
    {
        if (options == null)
        {
            return null;
        }

        var genOptions = new LMSupply.Generator.Models.GenerationOptions();

        if (options.Temperature.HasValue)
        {
            genOptions.Temperature = options.Temperature.Value;
        }

        if (options.MaxOutputTokens.HasValue)
        {
            genOptions.MaxTokens = options.MaxOutputTokens.Value;
        }

        return genOptions;
    }
}
