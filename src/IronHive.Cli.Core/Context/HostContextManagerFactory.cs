using IronHive.Agent.Context;
using HostCompactionConfig = IronHive.Cli.Core.Config.CompactionConfig;

namespace IronHive.Cli.Core.Context;

/// <summary>
/// Builds an agent <see cref="ContextManager"/> from the host's compaction settings so that
/// host-constructed agent loops actually perform context compaction.
/// </summary>
/// <remarks>
/// The agent loops (<c>AgentLoop</c>/<c>ThinkingAgentLoop</c>) accept an optional
/// <see cref="ContextManager"/>; when none is supplied the loop skips compaction entirely and the
/// host's <see cref="HostCompactionConfig"/> is inert. The host loop-construction paths use this
/// factory to wire compaction from configuration.
/// </remarks>
public static class HostContextManagerFactory
{
    /// <summary>
    /// Creates a model-aware <see cref="ContextManager"/> wired with token-based compaction.
    /// </summary>
    /// <param name="compaction">Host compaction settings. When <c>null</c>, agent defaults are used.</param>
    /// <param name="modelName">
    /// Model id used to size the context window. When <c>null</c>/empty, the token counter default applies.
    /// </param>
    /// <returns>A configured <see cref="ContextManager"/> ready to inject into an agent loop.</returns>
    public static ContextManager Create(HostCompactionConfig? compaction, string? modelName)
    {
        var config = ToAgentConfig(compaction);

        var tokenCounter = string.IsNullOrWhiteSpace(modelName)
            ? new ContextTokenCounter()
            : new ContextTokenCounter(modelName);

        var trigger = new TokenBasedCompactionTrigger(
            config.ProtectRecentTokens,
            config.MinimumPruneTokens);

        var compactor = new TokenBasedHistoryCompactor(tokenCounter, config);

        return new ContextManager(tokenCounter, trigger, compactor);
    }

    /// <summary>
    /// Maps the host's compaction config onto the agent's superset config. The host surface is a
    /// strict subset (identical field names); agent-only knobs keep their defaults.
    /// </summary>
    private static CompactionConfig ToAgentConfig(HostCompactionConfig? host)
    {
        if (host is null)
        {
            return new CompactionConfig();
        }

        return new CompactionConfig
        {
            ProtectRecentTokens = host.ProtectRecentTokens,
            MinimumPruneTokens = host.MinimumPruneTokens,
            ProtectedToolOutputs = host.ProtectedToolOutputs,
            TargetRatio = host.TargetRatio,
            UseTokenBasedCompaction = host.UseTokenBasedCompaction,
            ThresholdPercentage = host.ThresholdPercentage,
        };
    }
}
