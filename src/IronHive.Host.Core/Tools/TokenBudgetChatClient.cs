using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Core.Tools;

/// <summary>
/// <see cref="IChatClient"/> decorator that short-circuits chat calls whose
/// estimated message-history size would exceed a configurable fraction of the
/// inner model's context window. Sits BETWEEN <c>FunctionInvokingChatClient</c>
/// and the underlying provider so each iteration's accumulated history is
/// measured before another model round trip.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> Small/quantized models (Gemma 4 E4B at gguf:default)
/// fail to self-correct empty tool args even with the actionable directive
/// synthesized by <see cref="ResilientFunctionInvoker"/>. The model retries
/// 6–8 rounds, each adding ~500 tokens, until the prompt overflows the
/// (e.g. 4K) context window. llama-server then returns "Input too large" and
/// the consumer sees an empty <c>response.text</c> with no final-text turn —
/// the worst possible UX for dogfooding (Filer 2026-04-30 §12 evidence).
/// </para>
/// <para>
/// <b>What this does:</b> Estimates total message tokens (chars ÷ 4 — a
/// conservative upper bound for English; rough but stable). When the estimate
/// exceeds <c>maxContextTokens × threshold</c>, emits a single
/// <see cref="ChatResponseUpdate"/> carrying a graceful explanation and
/// <see cref="ChatFinishReason.Length"/>, then yields no further updates and
/// does NOT call the inner client. Upstream observers see a non-empty
/// response body instead of a silent fail.
/// </para>
/// <para>
/// <b>Context window discovery:</b> If the inner exposes
/// <see cref="IContextSizeProvider"/> via <c>GetService</c>, that value is
/// used; otherwise the constructor's <c>defaultMaxContextTokens</c> applies.
/// </para>
/// <para>
/// <b>Reference:</b> ecosystem ISSUE Option D-2, 2026-04-30.
/// </para>
/// </remarks>
public sealed class TokenBudgetChatClient : IChatClient
{
    private const string PartialResponseText =
        "[partial response: token budget exhausted before the assistant could finish — " +
        "the conversation history reached the model's context window. " +
        "Please shorten your message or start a new conversation.]";

    private readonly IChatClient _inner;
    private readonly int _defaultMaxContextTokens;
    private readonly double _threshold;

    public TokenBudgetChatClient(
        IChatClient inner,
        int defaultMaxContextTokens = 4096,
        double threshold = 0.8)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (defaultMaxContextTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMaxContextTokens), "must be positive");
        }
        if (threshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "must be in (0, 1]");
        }

        _inner = inner;
        _defaultMaxContextTokens = defaultMaxContextTokens;
        _threshold = threshold;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Non-streaming path: budget guard does not apply (no streaming
        // accumulator history to bound). Forward as-is.
        return _inner.GetResponseAsync(messages, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var maxTokens = ResolveMaxContextTokens();
        var estimatedTokens = EstimateTokens(messageList);
        var budgetTokens = (long)(maxTokens * _threshold);

        if (estimatedTokens > budgetTokens)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(PartialResponseText)],
                FinishReason = ChatFinishReason.Length
            };
            yield break;
        }

        await foreach (var update in _inner.GetStreamingResponseAsync(messageList, options, cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(IChatClient))
        {
            return this;
        }
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }

    private int ResolveMaxContextTokens()
    {
        if (_inner.GetService(typeof(IContextSizeProvider)) is IContextSizeProvider provider
            && provider.MaxContextTokens > 0)
        {
            return provider.MaxContextTokens;
        }
        return _defaultMaxContextTokens;
    }

    /// <summary>
    /// Conservative upper-bound token estimate using char-count ÷ 4. Works
    /// across model tokenizers within a factor of ~2; combined with the 0.8
    /// threshold this leaves enough headroom for inflight tokens. Counts text,
    /// tool-call argument JSON, and tool-result content — all sources of the
    /// retry-storm prompt growth.
    /// </summary>
    private static long EstimateTokens(IEnumerable<ChatMessage> messages)
    {
        long charCount = 0;
        foreach (var message in messages)
        {
            charCount += message.Text?.Length ?? 0;

            if (message.Contents is null)
            {
                continue;
            }
            foreach (var content in message.Contents)
            {
                charCount += content switch
                {
                    TextContent text => text.Text?.Length ?? 0,
                    FunctionResultContent result => result.Result?.ToString()?.Length ?? 0,
                    FunctionCallContent call => EstimateFunctionCall(call),
                    _ => 0
                };
            }
        }
        return charCount / 4;
    }

    private static int EstimateFunctionCall(FunctionCallContent call)
    {
        var len = call.Name?.Length ?? 0;
        if (call.Arguments is null)
        {
            return len;
        }
        foreach (var (key, value) in call.Arguments)
        {
            len += key.Length + (value?.ToString()?.Length ?? 0) + 4; // separators/quotes
        }
        return len;
    }
}
