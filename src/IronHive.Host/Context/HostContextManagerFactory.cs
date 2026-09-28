using IronHive.Agent.Context;

namespace IronHive.Host.Context;

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
    // The token counter's own default model, used when the host has no model id to size the window from.
    private const string DefaultModelName = "gpt-4o";

    /// <summary>
    /// Creates a model-aware <see cref="ContextManager"/> from the host's compaction settings.
    /// </summary>
    /// <param name="compaction">Compaction settings. When <c>null</c>, agent defaults are used.</param>
    /// <param name="modelName">
    /// Model id used to size the context window. When <c>null</c>/empty, the token counter default applies.
    /// </param>
    /// <param name="instructionContributors">
    /// Sections added after the system prompt on every turn (see <see cref="ISystemInstructionContributor"/>);
    /// <c>null</c> for none.
    /// </param>
    /// <returns>A configured <see cref="ContextManager"/> ready to inject into an agent loop.</returns>
    public static ContextManager Create(
        CompactionConfig? compaction,
        string? modelName,
        IEnumerable<ISystemInstructionContributor>? instructionContributors = null)
    {
        var config = compaction ?? new CompactionConfig();

        // The agent's own builder, so every setting reaches the manager: the window (MaxContextTokens), the trigger and
        // compactor it selects (anchored / token-based / threshold, budgets clamped to the window), TargetRatio and
        // CompactOnOverflow, and the per-request reductions. A hand-assembled copy of it here once dropped the first four.
        var manager = ContextManager.ForModel(
            string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName, config);
        foreach (var contributor in instructionContributors ?? [])
        {
            manager.AddInstructionContributor(contributor);
        }

        return manager;
    }
}
