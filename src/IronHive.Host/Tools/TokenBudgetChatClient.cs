using System.Runtime.CompilerServices;
using IronHive.Agent.Context;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tools;

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
/// the caller sees an empty <c>response.text</c> with no final-text turn and
/// no indication of why.
/// </para>
/// <para>
/// <b>What this does:</b> Estimates total message tokens with the agent's
/// <see cref="ContextTokenCounter"/> (per script: about four characters per token
/// for Latin text, one to one and a half for Korean, Japanese and Chinese). When the estimate
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

    // Counts text, tool-call arguments and tool results — all sources of the retry-storm prompt growth.
    private static readonly ContextTokenCounter Estimator = new();

    private static long EstimateTokens(IEnumerable<ChatMessage> messages) => Estimator.CountTokens(messages);
}
