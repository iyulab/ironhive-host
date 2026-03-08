using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Agent.Providers;
using IronHive.Cli.Core.Config;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatRole = LMSupply.Generator.Models.ChatRole;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;
using LmToolCall = LMSupply.Generator.Models.ToolCall;
using LmToolDefinition = LMSupply.Generator.Models.ToolDefinition;

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
/// Maps lm-supply ChatCompletionResult/ChatStreamChunk to M.E.AI types
/// including FunctionCallContent/FunctionResultContent for tool calling.
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

        var result = await _generator.GenerateChatCompleteAsync(messages, genOptions, cancellationToken);

        // Build response contents
        var contents = new List<AIContent>();

        if (result.Text is not null)
        {
            contents.Add(new TextContent(result.Text));
        }

        if (result.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in result.ToolCalls)
            {
                var args = ParseArguments(tc.Arguments);
                contents.Add(new FunctionCallContent(tc.Id, tc.Name, args));
            }
        }

        var responseMessage = new ChatMessage(ChatRole.Assistant, contents);
        var finishReason = MapFinishReason(result.FinishReason);

        return new ChatResponse(responseMessage)
        {
            FinishReason = finishReason,
            Usage = new UsageDetails
            {
                InputTokenCount = result.Usage.PromptTokens,
                OutputTokenCount = result.Usage.CompletionTokens,
                TotalTokenCount = result.Usage.TotalTokens
            }
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = ConvertMessages(chatMessages);
        var genOptions = ConvertOptions(options);

        // Track tool call accumulation across chunks
        Dictionary<int, (string Id, string Name, string Args)>? toolCallAccumulator = null;

        await foreach (var chunk in _generator.GenerateChatAsync(messages, genOptions, cancellationToken))
        {
            // Text delta
            if (chunk.Text is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(chunk.Text)]
                };
            }

            // Tool call deltas — accumulate then emit on finish
            if (chunk.ToolCalls is { Count: > 0 })
            {
                toolCallAccumulator ??= [];
                foreach (var delta in chunk.ToolCalls)
                {
                    if (!toolCallAccumulator.TryGetValue(delta.Index, out var existing))
                    {
                        existing = ("", "", "");
                    }

                    toolCallAccumulator[delta.Index] = (
                        delta.Id ?? existing.Id,
                        delta.Name ?? existing.Name,
                        existing.Args + (delta.Arguments ?? "")
                    );
                }
            }

            // Finish reason
            if (chunk.FinishReason is not null)
            {
                var finishReason = MapFinishReason(chunk.FinishReason);

                // If tool calls were accumulated, emit them now
                if (toolCallAccumulator is { Count: > 0 })
                {
                    var contents = new List<AIContent>();
                    foreach (var (_, (id, name, args)) in toolCallAccumulator)
                    {
                        var parsedArgs = ParseArguments(args);
                        contents.Add(new FunctionCallContent(id, name, parsedArgs));
                    }
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = contents,
                        FinishReason = finishReason
                    };
                }
                else
                {
                    yield return new ChatResponseUpdate
                    {
                        FinishReason = finishReason
                    };
                }
            }
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

    private static IEnumerable<LmChatMessage> ConvertMessages(
        IEnumerable<ChatMessage> messages)
    {
        foreach (var msg in messages)
        {
            // Check for tool-related content
            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var functionResults = msg.Contents.OfType<FunctionResultContent>().ToList();

            // Assistant message with tool calls
            if (msg.Role == ChatRole.Assistant && functionCalls.Count > 0)
            {
                var toolCalls = functionCalls.Select(fc => new LmToolCall(
                    fc.CallId ?? $"call_{Guid.NewGuid():N}",
                    fc.Name,
                    fc.Arguments is not null ? JsonSerializer.Serialize(fc.Arguments) : "{}"
                )).ToList();

                yield return new LmChatMessage(
                    LmChatRole.Assistant,
                    msg.Text ?? string.Empty)
                { ToolCalls = toolCalls };
                continue;
            }

            // Tool result message
            if (msg.Role == ChatRole.Tool && functionResults.Count > 0)
            {
                foreach (var fr in functionResults)
                {
                    yield return LmChatMessage.Tool(
                        fr.CallId ?? "",
                        fr.Result?.ToString() ?? "");
                }
                continue;
            }

            // Regular message (system/user/assistant without tools)
            var role = msg.Role.Value switch
            {
                "system" => LmChatRole.System,
                "user" => LmChatRole.User,
                "assistant" => LmChatRole.Assistant,
                _ => LmChatRole.User
            };
            yield return new LmChatMessage(role, msg.Text ?? string.Empty);
        }
    }

    private static LmGenerationOptions? ConvertOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var genOptions = new LmGenerationOptions();

        if (options.Temperature.HasValue)
        {
            genOptions.Temperature = options.Temperature.Value;
        }

        if (options.MaxOutputTokens.HasValue)
        {
            genOptions.MaxTokens = options.MaxOutputTokens.Value;
        }

        // Convert tool definitions
        if (options.Tools is { Count: > 0 })
        {
            var tools = new List<LmToolDefinition>();
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction func)
                {
                    var parameters = func.JsonSchema.ValueKind != JsonValueKind.Undefined
                        ? func.JsonSchema
                        : (JsonElement?)null;
                    tools.Add(new LmToolDefinition(
                        func.Name,
                        func.Description,
                        parameters));
                }
            }
            if (tools.Count > 0)
            {
                genOptions.Tools = tools;
                genOptions.ToolChoice = "auto";
            }
        }

        return genOptions;
    }

    private static Dictionary<string, object?>? ParseArguments(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static ChatFinishReason? MapFinishReason(string? reason) => reason switch
    {
        "stop" => ChatFinishReason.Stop,
        "tool_calls" => ChatFinishReason.ToolCalls,
        "length" => ChatFinishReason.Length,
        _ => null
    };
}
