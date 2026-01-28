using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Context;

/// <summary>
/// Context usage statistics.
/// </summary>
public record ContextUsage
{
    /// <summary>
    /// Current token count in context.
    /// </summary>
    public int CurrentTokens { get; init; }

    /// <summary>
    /// Maximum allowed tokens.
    /// </summary>
    public int MaxTokens { get; init; }

    /// <summary>
    /// Usage percentage (0.0 to 1.0).
    /// </summary>
    public float UsagePercentage => MaxTokens > 0 ? (float)CurrentTokens / MaxTokens : 0;

    /// <summary>
    /// Whether compaction is needed.
    /// </summary>
    public bool NeedsCompaction { get; init; }

    /// <summary>
    /// Number of messages in history.
    /// </summary>
    public int MessageCount { get; init; }
}

/// <summary>
/// Manages context window for agent conversations.
/// Handles token counting, compaction triggering, and history management.
/// </summary>
public class ContextManager
{
    private readonly IContextTokenCounter _tokenCounter;
    private readonly ICompactionTrigger _compactionTrigger;
    private readonly IHistoryCompactor _historyCompactor;

    public ContextManager(
        IContextTokenCounter tokenCounter,
        ICompactionTrigger? compactionTrigger = null,
        IHistoryCompactor? historyCompactor = null)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _compactionTrigger = compactionTrigger ?? new ThresholdCompactionTrigger();
        _historyCompactor = historyCompactor ?? new HistoryCompactor(tokenCounter);
    }

    /// <summary>
    /// Gets the token counter being used.
    /// </summary>
    public IContextTokenCounter TokenCounter => _tokenCounter;

    /// <summary>
    /// Gets the maximum context tokens.
    /// </summary>
    public int MaxContextTokens => _tokenCounter.MaxContextTokens;

    /// <summary>
    /// Gets the current context usage.
    /// </summary>
    public ContextUsage GetUsage(IReadOnlyList<ChatMessage> history)
    {
        var currentTokens = _tokenCounter.CountTokens(history);

        return new ContextUsage
        {
            CurrentTokens = currentTokens,
            MaxTokens = _tokenCounter.MaxContextTokens,
            NeedsCompaction = _compactionTrigger.ShouldCompact(currentTokens, _tokenCounter.MaxContextTokens),
            MessageCount = history.Count
        };
    }

    /// <summary>
    /// Checks if compaction should be triggered.
    /// </summary>
    public bool ShouldCompact(IReadOnlyList<ChatMessage> history)
    {
        var currentTokens = _tokenCounter.CountTokens(history);
        return _compactionTrigger.ShouldCompact(currentTokens, _tokenCounter.MaxContextTokens);
    }

    /// <summary>
    /// Compacts the history if needed.
    /// </summary>
    /// <param name="history">The conversation history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compacted history if compaction was performed, otherwise original history.</returns>
    public async Task<CompactionResult> CompactIfNeededAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var currentTokens = _tokenCounter.CountTokens(history);

        if (!_compactionTrigger.ShouldCompact(currentTokens, _tokenCounter.MaxContextTokens))
        {
            return new CompactionResult
            {
                CompactedHistory = history,
                OriginalTokens = currentTokens,
                CompactedTokens = currentTokens,
                MessagesCompacted = 0
            };
        }

        // Target: reduce to 70% of max to leave room for future messages
        var targetTokens = (int)(_tokenCounter.MaxContextTokens * 0.70f);

        return await _historyCompactor.CompactAsync(history, targetTokens, cancellationToken);
    }

    /// <summary>
    /// Forces compaction to a specific target.
    /// </summary>
    public Task<CompactionResult> CompactAsync(
        IReadOnlyList<ChatMessage> history,
        int targetTokens,
        CancellationToken cancellationToken = default)
    {
        return _historyCompactor.CompactAsync(history, targetTokens, cancellationToken);
    }

    /// <summary>
    /// Estimates how many more tokens can be added before compaction is triggered.
    /// </summary>
    public int GetRemainingTokens(IReadOnlyList<ChatMessage> history)
    {
        var currentTokens = _tokenCounter.CountTokens(history);
        var thresholdTokens = (int)(_tokenCounter.MaxContextTokens * _compactionTrigger.ThresholdPercentage);
        return Math.Max(0, thresholdTokens - currentTokens);
    }

    /// <summary>
    /// Creates a context manager with default settings for a model.
    /// </summary>
    public static ContextManager ForModel(string modelName, IChatClient? summarizer = null)
    {
        var tokenCounter = new ContextTokenCounter(modelName);
        var compactionTrigger = new ThresholdCompactionTrigger(0.92f);
        var historyCompactor = new HistoryCompactor(tokenCounter, summarizer);

        return new ContextManager(tokenCounter, compactionTrigger, historyCompactor);
    }
}
