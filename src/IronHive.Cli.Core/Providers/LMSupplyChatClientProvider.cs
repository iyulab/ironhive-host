using System.Collections.Concurrent;
using IronHive.Cli.Core.Config;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// LMSupply-based chat client provider for local inference.
/// Wraps LMSupply.Generator to implement IChatClient.
/// </summary>
public sealed class LMSupplyChatClientProvider : IChatClientProvider, IDisposable
{
    private readonly LMSupplyConfig _config;
    private readonly ILogger<LMSupplyChatClientProvider>? _logger;
    private readonly ConcurrentDictionary<string, (IGeneratorModel generator, LMSupplyChatClient client)> _clientCache = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _defaultModel;
    private volatile bool _initialized;
    private bool _disposed;

    public LMSupplyChatClientProvider(LMSupplyConfig config, ILogger<LMSupplyChatClientProvider>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => "lmsupply";

    /// <inheritdoc />
    public bool IsAvailable => _config.Enabled;

    /// <inheritdoc />
    public async Task<IChatClient> GetChatClientAsync(string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        // Lazy initialization - automatically initialize if not done
        if (!_initialized)
        {
            await EnsureInitializedAsync(cancellationToken);
        }

        var model = modelOverride ?? _defaultModel!;

        // Try to get from cache, or load dynamically
        if (!_clientCache.TryGetValue(model, out var cached))
        {
            // Load new model asynchronously
            cached = await LoadModelAsync(model, cancellationToken);
        }

        return cached.client;
    }

    /// <summary>
    /// Asynchronously loads a model.
    /// </summary>
    private async Task<(IGeneratorModel generator, LMSupplyChatClient client)> LoadModelAsync(string modelId, CancellationToken cancellationToken)
    {
        // Check again in case another thread loaded it
        if (_clientCache.TryGetValue(modelId, out var existing))
        {
            return existing;
        }

        var maxContext = GetEffectiveMaxContextLength();
        var generator = await BuildGeneratorAsync(modelId, maxContext, cancellationToken);
        var client = new LMSupplyChatClient(generator);
        var result = (generator, client);

        // Try to add to cache, but if another thread beat us, dispose and use theirs
        if (!_clientCache.TryAdd(modelId, result))
        {
            await generator.DisposeAsync();
            return _clientCache[modelId];
        }

        return result;
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
        catch (OperationCanceledException)
        {
            // Cancellation is expected, don't log as warning
            return false;
        }
        catch (Exception ex)
        {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance-critical paths
            _logger?.LogWarning(ex, "LMSupply health check failed for model: {Model}", _config.GeneratorModel);
#pragma warning restore CA1848
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

            var maxContext = GetEffectiveMaxContextLength();
            var generator = await BuildGeneratorAsync(modelId, maxContext, cancellationToken);
            var chatClient = new LMSupplyChatClient(generator);

            _clientCache[modelId] = (generator, chatClient);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gets the effective max context length based on config or auto-detection.
    /// </summary>
    private int GetEffectiveMaxContextLength()
    {
        // If explicitly configured, use that value
        if (_config.MaxContextLength.HasValue && _config.MaxContextLength.Value > 0)
        {
            return _config.MaxContextLength.Value;
        }

        // Auto-detect based on available memory
        return CalculateMaxContextLengthFromMemory();
    }

    /// <summary>
    /// Calculates recommended max context length based on available system memory.
    /// </summary>
    private static int CalculateMaxContextLengthFromMemory()
    {
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            var availableBytes = memoryInfo.TotalAvailableMemoryBytes;
            var availableGB = availableBytes / (1024.0 * 1024 * 1024);

            // Conservative estimates for OGA models (Phi-3.5-mini class)
            // KV cache formula: batch(1) × kv_heads(8) × max_len × head_size(128) × layers(32) × 2 × dtype
            return availableGB switch
            {
                >= 32 => 131072,  // 128K context
                >= 16 => 65536,   // 64K context
                >= 8 => 32768,    // 32K context
                >= 4 => 16384,    // 16K context
                _ => 8192         // 8K context (minimum for usability)
            };
        }
        catch
        {
            // Fallback to safe default if memory detection fails
            return 16384;
        }
    }

    private static async Task<IGeneratorModel> BuildGeneratorAsync(
        string modelId,
        int maxContextLength,
        CancellationToken cancellationToken)
    {
        var builder = TextGeneratorBuilder.Create();

        // Configure model source
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

        // Set max context length to prevent OOM on memory-constrained systems
        builder.WithMaxContextLength(maxContextLength);

        return await builder.BuildAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AvailableModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult<IReadOnlyList<AvailableModelInfo>>(Array.Empty<AvailableModelInfo>());
        }

        var models = new List<AvailableModelInfo>();

        // Return the configured generator model
        var modelId = _config.GeneratorModel;
        models.Add(new AvailableModelInfo
        {
            ModelId = modelId,
            Provider = ProviderName,
            DisplayName = GetDisplayName(modelId),
            Source = ModelSource.Cached,
            IsDefault = true
        });

        return Task.FromResult<IReadOnlyList<AvailableModelInfo>>(models);
    }

    private static string GetDisplayName(string modelId)
    {
        if (modelId == "auto" || modelId == "gguf:default")
        {
            return "Default Local Model (auto)";
        }

        if (modelId.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase))
        {
            return modelId[5..];
        }

        return modelId;
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
