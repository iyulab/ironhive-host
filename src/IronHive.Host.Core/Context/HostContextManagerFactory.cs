using IronHive.Agent.Context;

namespace IronHive.Host.Core.Context;

/// <summary>
/// Builds an agent <see cref="ContextManager"/> from the host's compaction settings so that
/// host-constructed agent loops actually perform context compaction.
/// </summary>
/// <remarks>
/// The agent loops (<c>AgentLoop</c>/<c>ThinkingAgentLoop</c>) accept an optional
/// <see cref="ContextManager"/>; when none is supplied the loop skips compaction entirely and the
/// configured <see cref="CompactionConfig"/> is inert. The host loop-construction paths use this
/// factory to wire compaction from configuration.
/// </remarks>
public static class HostContextManagerFactory
{
    /// <summary>
    /// Creates a model-aware <see cref="ContextManager"/> wired with token-based compaction.
    /// </summary>
    /// <param name="compaction">Compaction settings. When <c>null</c>, agent defaults are used.</param>
    /// <param name="modelName">
    /// Model id used to size the context window. When <c>null</c>/empty, the token counter default applies.
    /// </param>
    /// <returns>A configured <see cref="ContextManager"/> ready to inject into an agent loop.</returns>
    public static ContextManager Create(CompactionConfig? compaction, string? modelName)
    {
        var config = compaction ?? new CompactionConfig();

        var tokenCounter = string.IsNullOrWhiteSpace(modelName)
            ? new ContextTokenCounter()
            : new ContextTokenCounter(modelName);

        var trigger = new TokenBasedCompactionTrigger(
            config.ProtectRecentTokens,
            config.MinimumPruneTokens);

        var compactor = new TokenBasedHistoryCompactor(tokenCounter, config);

        return new ContextManager(tokenCounter, trigger, compactor);
    }
}
