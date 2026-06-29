using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Agent.Providers;
using IronHive.Host.Core.Config;
using IronHive.Host.Core.Tools;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatRole = LMSupply.Generator.Models.ChatRole;
using LmChatStreamChunk = LMSupply.Generator.Models.ChatStreamChunk;
using LmChatToolCall = LMSupply.Generator.Models.ChatToolCall;
using LmChatToolDefinition = LMSupply.Generator.Models.ChatToolDefinition;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronHive.Host.Core.Providers;

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
/// IChatClient wrapper for LMSupply ITextGenerator.
/// Maps lm-supply ChatCompletionResult/ChatStreamChunk to M.E.AI types
/// including FunctionCallContent/FunctionResultContent for tool calling.
/// </summary>
/// <remarks>
/// Public surface (since 0.10.2) so umbrella e2e harness and other dogfooding consumers
/// can construct an IChatClient directly over a pre-loaded <see cref="ITextGenerator"/>
/// without going through <see cref="LMSupplyChatClientProvider"/>'s broken
/// <c>BuildGeneratorAsync</c> path (see umbrella's
/// <c>ISSUE-ironhive-cli-lmsupply-provider-builder-routing-broken-20260429-005000</c>).
/// </remarks>
public sealed class LMSupplyChatClient : IChatClient
{
    private readonly ITextGenerator _generator;

    public LMSupplyChatClient(ITextGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public ChatClientMetadata Metadata => new("LMSupply", null, _generator.ModelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lmMessages = ConvertMessages(messages, options);
        var genOptions = ConvertOptions(options);

        // Use tool-aware method for structured response
        var result = await _generator.GenerateChatWithToolsAsync(lmMessages, genOptions, cancellationToken);

        // Build response contents
        var contents = new List<AIContent>();

        if (result.Content is not null)
        {
            contents.Add(new TextContent(result.Content));
        }

        if (result.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in result.ToolCalls)
            {
                var args = ParseArguments(tc.Arguments);
                contents.Add(new FunctionCallContent(tc.Id, tc.FunctionName, args));
            }
        }

        var responseMessage = new ChatMessage(ChatRole.Assistant, contents);
        var finishReason = MapFinishReason(result.FinishReason);

        return new ChatResponse(responseMessage)
        {
            FinishReason = finishReason
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lmMessages = ConvertMessages(messages, options);
        var genOptions = ConvertOptions(options);

        // Track tool call accumulation across chunks
        Dictionary<int, (string Id, string Name, string Args)>? toolCallAccumulator = null;

        await foreach (var chunk in _generator.GenerateChatStreamAsync(lmMessages, genOptions, cancellationToken))
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
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = BuildToolCallContents(toolCallAccumulator),
                        FinishReason = finishReason
                    };
                    toolCallAccumulator = null;
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

        // Post-loop flush: emit accumulated tool calls if stream ended without FinishReason
        if (toolCallAccumulator is { Count: > 0 })
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = BuildToolCallContents(toolCallAccumulator),
                FinishReason = ChatFinishReason.ToolCalls
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(IChatClient))
        {
            return this;
        }
        // Expose the model's context window so a wrapping TokenBudgetChatClient
        // can size its budget to the actual GGUF context length instead of a
        // hard-coded default (Option D-2, ecosystem ISSUE 2026-04-30).
        if (serviceType == typeof(IContextSizeProvider) && _generator is IGeneratorModel model && model.MaxContextLength > 0)
        {
            return new LMSupplyContextSizeProvider(model.MaxContextLength);
        }
        return null;
    }

    public void Dispose()
    {
        // Generator is disposed by the provider
    }

    private sealed class LMSupplyContextSizeProvider(int maxContextTokens) : IContextSizeProvider
    {
        public int MaxContextTokens { get; } = maxContextTokens;
    }

    private static IEnumerable<LmChatMessage> ConvertMessages(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        // M.E.AI standard ChatOptions.Instructions: emit as a leading System message
        // so downstream chat formatters apply it like an explicit system prompt.
        // If the messages collection already carries System messages, they remain
        // intact behind this one — most chat formatters concatenate or honor each.
        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            yield return new LmChatMessage(LmChatRole.System, options.Instructions);
        }

        foreach (var msg in messages)
        {
            // Check for tool-related content
            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var functionResults = msg.Contents.OfType<FunctionResultContent>().ToList();

            // Assistant message with tool calls
            if (msg.Role == ChatRole.Assistant && functionCalls.Count > 0)
            {
                var toolCalls = functionCalls.Select(fc => new LmChatToolCall(
                    fc.CallId ?? $"call_{Guid.NewGuid():N}",
                    fc.Name,
                    fc.Arguments is not null ? JsonSerializer.Serialize(fc.Arguments) : "{}")).ToList();

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
                    yield return LmChatMessage.ToolResult(
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
                "tool" => LmChatRole.Tool,
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

        // Forward standard M.E.AI sampler properties so consumers can probe
        // alternate decoding parameters from the chat-pipeline boundary
        // (ecosystem ISSUE Surface B sampler-probe path, 2026-05-01).
        if (options.TopP.HasValue)
        {
            genOptions.TopP = options.TopP.Value;
        }

        if (options.TopK.HasValue)
        {
            genOptions.TopK = options.TopK.Value;
        }

        if (options.FrequencyPenalty.HasValue)
        {
            genOptions.FrequencyPenalty = options.FrequencyPenalty.Value;
        }

        if (options.PresencePenalty.HasValue)
        {
            genOptions.PresencePenalty = options.PresencePenalty.Value;
        }

        if (options.Seed.HasValue)
        {
            genOptions.Seed = unchecked((int)options.Seed.Value);
        }

        if (options.StopSequences is { Count: > 0 })
        {
            genOptions.StopSequences = [.. options.StopSequences];
        }

        // Forward lm-supply native sampler params via M.E.AI AdditionalProperties
        // bag (provider-specific opt-in). RepetitionPenalty and MinP have no
        // standard M.E.AI surface (they're llama.cpp / hf-tgi family idioms,
        // distinct from OpenAI's frequency/presence penalties), so the bag is
        // the correct ingress per M.E.AI design. Keys match lm-supply
        // property names in snake_case: repetition_penalty, min_p.
        // Keeps lm-supply defaults (RepetitionPenalty=1.1f, MinP=0.05f) when
        // unset so existing consumers see no behavior change.
        if (options.AdditionalProperties is { Count: > 0 } extras)
        {
            if (extras.TryGetValue("repetition_penalty", out var rp) && TryToFloat(rp, out var rpVal))
            {
                genOptions.RepetitionPenalty = rpVal;
            }
            if (extras.TryGetValue("min_p", out var mp) && TryToFloat(mp, out var mpVal))
            {
                genOptions.MinP = mpVal;
            }
        }

        // Convert tool definitions
        if (options.Tools is { Count: > 0 })
        {
            var tools = new List<LmChatToolDefinition>();
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction func)
                {
                    var parameters = func.JsonSchema.ValueKind != JsonValueKind.Undefined
                        ? func.JsonSchema
                        : (JsonElement?)null;
                    tools.Add(new LmChatToolDefinition(func.Name, func.Description, parameters));
                }
            }
            if (tools.Count > 0)
            {
                genOptions.Tools = tools;
            }
        }

        return genOptions;
    }

    private static bool TryToFloat(object? value, out float result)
    {
        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case string s when float.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed):
                result = parsed;
                return true;
            default:
                result = 0f;
                return false;
        }
    }

    private static List<AIContent> BuildToolCallContents(
        Dictionary<int, (string Id, string Name, string Args)> accumulator)
    {
        var contents = new List<AIContent>();
        foreach (var (_, (id, name, args)) in accumulator.OrderBy(kvp => kvp.Key))
        {
            if (string.IsNullOrEmpty(name))
            {
                continue; // skip malformed deltas without a function name
            }

            var callId = string.IsNullOrEmpty(id) ? $"call_{Guid.NewGuid():N}" : id;
            contents.Add(new FunctionCallContent(callId, name, ParseArguments(args)));
        }
        return contents;
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
