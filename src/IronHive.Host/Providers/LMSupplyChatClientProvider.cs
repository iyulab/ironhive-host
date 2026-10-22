using System.Collections.Concurrent;
using IronHive.Agent.Providers;
using IronHive.Host.Config;
using IronHive.Host.Tools;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Providers;

/// <summary>
/// LMSupply-based chat client provider for local inference.
/// Wraps LMSupply.Generator to implement IChatClient.
/// </summary>
public sealed class LMSupplyChatClientProvider : IChatClientProvider, IDisposable
{
    private readonly LMSupplyConfig _config;
    private readonly ILogger<LMSupplyChatClientProvider>? _logger;
    private readonly ConcurrentDictionary<string, (IGeneratorModel generator, IChatClient client)> _clientCache = new();
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
        // Only the configured default goes through initialization. A request that names its own
        // model must not pay for (or fail on) loading a default it never asked for.
        if (modelOverride is null && !_initialized)
        {
            await EnsureInitializedAsync(cancellationToken);
        }

        var model = modelOverride ?? _defaultModel ?? _config.GeneratorModel;

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
    private async Task<(IGeneratorModel generator, IChatClient client)> LoadModelAsync(string modelId, CancellationToken cancellationToken)
    {
        // Check again in case another thread loaded it
        if (_clientCache.TryGetValue(modelId, out var existing))
        {
            return existing;
        }

        var maxContext = GetEffectiveMaxContextLength();
        var generator = await BuildGeneratorAsync(modelId, maxContext, cancellationToken);
        var client = new LMSupplyHostChatClient(generator, modelId);
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
            var chatClient = new LMSupplyHostChatClient(generator, modelId);

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

    private static Task<IGeneratorModel> BuildGeneratorAsync(
        string modelId,
        int maxContextLength,
        CancellationToken cancellationToken)
    {
        // Delegate to LMSupply's documented public surface. LocalGenerator.LoadAsync
        // handles all dispatch (GGUF aliases, "auto"/"default" hardware-aware selection,
        // ONNX repos, local file/dir paths) and routes to the correct factory. The
        // previous TextGeneratorBuilder.With* path always landed on the ONNX factory,
        // failing every GGUF alias including the default "gguf:default".
        var options = new GeneratorOptions { MaxContextLength = maxContextLength };
        return LocalGenerator.LoadAsync(modelId, options, progress: null, cancellationToken);
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
        if (modelId == "auto" || modelId == "gguf:auto")
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
/// The chat client the provider hands out: LMSupply's own <c>generator.AsChatClient()</c>, plus what only this host knows —
/// the model's context window as an <see cref="IContextSizeProvider"/> (a wrapping <see cref="TokenBudgetChatClient"/> sizes
/// its budget to the loaded GGUF context), and the id the provider loaded the model under.
/// </summary>
/// <remarks>
/// <para>
/// The provider loads a model by the id the host configured — usually an alias such as <c>gguf:gemma4-default</c> — and the
/// agent loop sends that same id as <see cref="ChatOptions.ModelId"/>. LMSupply's client answers only to the model's own id
/// (<see cref="ITextGenerator.ModelId"/>) and rejects any other, so a request naming the id this client was loaded under is
/// passed on naming the model itself. A request naming another model still reaches LMSupply as it is and is refused there.
/// </para>
/// <para>Disposing it does not dispose the generator; the provider owns the generator.</para>
/// </remarks>
internal sealed class LMSupplyHostChatClient(IGeneratorModel generator, string? loadedAs = null)
    : DelegatingChatClient(generator.AsChatClient())
{
    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, ForThisModel(options), cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, ForThisModel(options), cancellationToken);

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(IContextSizeProvider) && generator.MaxContextLength > 0
            ? new ContextSize(generator.MaxContextLength)
            : base.GetService(serviceType, serviceKey);

    private ChatOptions? ForThisModel(ChatOptions? options)
    {
        if (options?.ModelId is not { } requested || loadedAs is null
            || !string.Equals(requested, loadedAs, StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        var forModel = options.Clone();
        forModel.ModelId = generator.ModelId;
        return forModel;
    }

    private sealed class ContextSize(int maxContextTokens) : IContextSizeProvider
    {
        public int MaxContextTokens { get; } = maxContextTokens;
    }
}
