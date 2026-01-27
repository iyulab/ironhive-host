namespace IronHive.Cli.Core.Agent;

/// <summary>
/// Tracks token usage across a session.
/// </summary>
public interface IUsageTracker
{
    /// <summary>
    /// Records token usage from a request.
    /// </summary>
    void Record(TokenUsage usage);

    /// <summary>
    /// Gets the cumulative usage for the session.
    /// </summary>
    SessionUsage GetSessionUsage();

    /// <summary>
    /// Resets the session statistics.
    /// </summary>
    void Reset();
}

/// <summary>
/// Session-level usage statistics.
/// </summary>
public record SessionUsage
{
    /// <summary>
    /// Total input tokens used in the session.
    /// </summary>
    public long TotalInputTokens { get; init; }

    /// <summary>
    /// Total output tokens used in the session.
    /// </summary>
    public long TotalOutputTokens { get; init; }

    /// <summary>
    /// Total tokens used in the session.
    /// </summary>
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>
    /// Number of requests made in the session.
    /// </summary>
    public int RequestCount { get; init; }

    /// <summary>
    /// Average tokens per request.
    /// </summary>
    public double AverageTokensPerRequest => RequestCount > 0 ? (double)TotalTokens / RequestCount : 0;

    /// <summary>
    /// Estimated cost in USD (based on typical GPT-4 pricing).
    /// This is a rough estimate - actual costs depend on the model used.
    /// </summary>
    public decimal EstimatedCostUsd { get; init; }
}

/// <summary>
/// Default implementation of usage tracker.
/// </summary>
public class UsageTracker : IUsageTracker
{
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private int _requestCount;
    private readonly object _lock = new();

    // Default pricing (GPT-4o-mini style, can be made configurable)
    private const decimal InputCostPer1MTokens = 0.15m;  // $0.15 per 1M input tokens
    private const decimal OutputCostPer1MTokens = 0.60m; // $0.60 per 1M output tokens

    /// <inheritdoc />
    public void Record(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        lock (_lock)
        {
            _totalInputTokens += usage.InputTokens;
            _totalOutputTokens += usage.OutputTokens;
            _requestCount++;
        }
    }

    /// <inheritdoc />
    public SessionUsage GetSessionUsage()
    {
        lock (_lock)
        {
            var inputCost = _totalInputTokens * InputCostPer1MTokens / 1_000_000m;
            var outputCost = _totalOutputTokens * OutputCostPer1MTokens / 1_000_000m;

            return new SessionUsage
            {
                TotalInputTokens = _totalInputTokens,
                TotalOutputTokens = _totalOutputTokens,
                RequestCount = _requestCount,
                EstimatedCostUsd = inputCost + outputCost
            };
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_lock)
        {
            _totalInputTokens = 0;
            _totalOutputTokens = 0;
            _requestCount = 0;
        }
    }
}
