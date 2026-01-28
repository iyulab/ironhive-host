using System.Text;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Context;

/// <summary>
/// Options for history compaction.
/// </summary>
public class HistoryCompactorOptions
{
    /// <summary>
    /// Number of recent turns to preserve in the tail (not summarized).
    /// Default is 4 turns (8 messages: 4 user + 4 assistant).
    /// </summary>
    public int PreserveTailTurns { get; init; } = 4;

    /// <summary>
    /// Target compression ratio for the middle section.
    /// Default is 0.5 (compress to 50% of original).
    /// </summary>
    public float TargetCompressionRatio { get; init; } = 0.5f;

    /// <summary>
    /// Whether to use LLM for summarization (true) or simple truncation (false).
    /// </summary>
    public bool UseLlmSummarization { get; init; } = true;
}

/// <summary>
/// Compacts conversation history using Head/Middle/Tail strategy.
/// </summary>
public class HistoryCompactor : IHistoryCompactor
{
    private readonly IContextTokenCounter _tokenCounter;
    private readonly IChatClient? _summarizer;
    private readonly HistoryCompactorOptions _options;

    public HistoryCompactor(
        IContextTokenCounter tokenCounter,
        IChatClient? summarizer = null,
        HistoryCompactorOptions? options = null)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _summarizer = summarizer;
        _options = options ?? new HistoryCompactorOptions();
    }

    /// <inheritdoc />
    public async Task<CompactionResult> CompactAsync(
        IReadOnlyList<ChatMessage> history,
        int targetTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        var originalTokens = _tokenCounter.CountTokens(history);

        // If already within target, return as-is
        if (originalTokens <= targetTokens)
        {
            return new CompactionResult
            {
                CompactedHistory = history,
                OriginalTokens = originalTokens,
                CompactedTokens = originalTokens,
                MessagesCompacted = 0
            };
        }

        // Split into Head/Middle/Tail
        var (head, middle, tail) = SplitHistory(history);

        // Calculate token budgets
        var headTokens = _tokenCounter.CountTokens(head);
        var tailTokens = _tokenCounter.CountTokens(tail);
        var middleTargetTokens = Math.Max(0, targetTokens - headTokens - tailTokens);

        // Compact the middle section
        var compactedMiddle = await CompactMiddleAsync(
            middle,
            middleTargetTokens,
            cancellationToken);

        // Reassemble
        var compactedHistory = new List<ChatMessage>();
        compactedHistory.AddRange(head);
        compactedHistory.AddRange(compactedMiddle);
        compactedHistory.AddRange(tail);

        var compactedTokens = _tokenCounter.CountTokens(compactedHistory);

        return new CompactionResult
        {
            CompactedHistory = compactedHistory,
            OriginalTokens = originalTokens,
            CompactedTokens = compactedTokens,
            MessagesCompacted = middle.Count - compactedMiddle.Count
        };
    }

    private (List<ChatMessage> head, List<ChatMessage> middle, List<ChatMessage> tail) SplitHistory(
        IReadOnlyList<ChatMessage> history)
    {
        var head = new List<ChatMessage>();
        var middle = new List<ChatMessage>();
        var tail = new List<ChatMessage>();

        // Head: System message(s)
        var index = 0;
        while (index < history.Count && history[index].Role == ChatRole.System)
        {
            head.Add(history[index]);
            index++;
        }

        // Tail: Last N turns (preserve recent context)
        var tailMessageCount = _options.PreserveTailTurns * 2; // 2 messages per turn (user + assistant)
        var tailStartIndex = Math.Max(index, history.Count - tailMessageCount);

        // Middle: Everything between head and tail
        for (var i = index; i < tailStartIndex; i++)
        {
            middle.Add(history[i]);
        }

        // Tail
        for (var i = tailStartIndex; i < history.Count; i++)
        {
            tail.Add(history[i]);
        }

        return (head, middle, tail);
    }

    private async Task<List<ChatMessage>> CompactMiddleAsync(
        List<ChatMessage> middle,
        int targetTokens,
        CancellationToken cancellationToken)
    {
        if (middle.Count == 0)
        {
            return [];
        }

        var middleTokens = _tokenCounter.CountTokens(middle);

        // If middle already fits, return as-is
        if (middleTokens <= targetTokens)
        {
            return middle;
        }

        // Try LLM summarization if available
        if (_options.UseLlmSummarization && _summarizer is not null)
        {
            return await SummarizeWithLlmAsync(middle, targetTokens, cancellationToken);
        }

        // Fallback: Simple truncation (remove oldest messages)
        return TruncateMiddle(middle, targetTokens);
    }

    private async Task<List<ChatMessage>> SummarizeWithLlmAsync(
        List<ChatMessage> middle,
        int targetTokens,
        CancellationToken cancellationToken)
    {
        // Build conversation text for summarization
        var conversationText = new StringBuilder();
        foreach (var message in middle)
        {
            conversationText.Append(System.Globalization.CultureInfo.InvariantCulture, $"[{message.Role}]: {message.Text}");
            conversationText.AppendLine();
        }

        var summarizationPrompt = $"""
            Summarize the following conversation history concisely.
            Preserve key information: decisions made, tasks completed, important context.
            Keep the summary under {targetTokens / 4} tokens.

            Conversation:
            {conversationText}

            Summary:
            """;

        try
        {
            var response = await _summarizer!.GetResponseAsync(summarizationPrompt, cancellationToken: cancellationToken);
            var summary = response.Text ?? string.Empty;

            // Return as a single system-style context message
            return
            [
                new ChatMessage(ChatRole.System, $"[Previous conversation summary]: {summary}")
            ];
        }
        catch
        {
            // Fallback to truncation on error
            return TruncateMiddle(middle, targetTokens);
        }
    }

    private List<ChatMessage> TruncateMiddle(List<ChatMessage> middle, int targetTokens)
    {
        if (targetTokens <= 0)
        {
            // Create a minimal summary
            return
            [
                new ChatMessage(ChatRole.System, "[Earlier conversation omitted due to context limits]")
            ];
        }

        // Keep messages from the end until we hit the target
        var result = new List<ChatMessage>();
        var currentTokens = 0;

        for (var i = middle.Count - 1; i >= 0; i--)
        {
            var messageTokens = _tokenCounter.CountTokens(middle[i]);
            if (currentTokens + messageTokens > targetTokens)
            {
                break;
            }

            result.Insert(0, middle[i]);
            currentTokens += messageTokens;
        }

        // Add marker if we truncated
        if (result.Count < middle.Count)
        {
            var omittedCount = middle.Count - result.Count;
            result.Insert(0, new ChatMessage(ChatRole.System, $"[{omittedCount} earlier messages omitted]"));
        }

        return result;
    }
}
