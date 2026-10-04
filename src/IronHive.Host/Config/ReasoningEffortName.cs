using Microsoft.Extensions.AI;

namespace IronHive.Host.Config;

/// <summary>
/// The reasoning-effort words the host accepts — in <c>chatBehavior.reasoningEffort</c>, <c>run --reasoning-effort</c> and
/// a server turn's <c>reasoning_effort</c> — and their <see cref="ReasoningEffort"/>.
/// </summary>
public static class ReasoningEffortName
{
    /// <summary>The accepted words, in order of effort.</summary>
    public static IReadOnlyList<string> Values { get; } = ["none", "low", "medium", "high", "extra_high"];

    /// <summary>The <see cref="ReasoningEffort"/> for <paramref name="name"/>, or <c>false</c> when it is not one of <see cref="Values"/>.</summary>
    public static bool TryParse(string? name, out ReasoningEffort effort)
    {
        (var known, effort) = name switch
        {
            "none" => (true, ReasoningEffort.None),
            "low" => (true, ReasoningEffort.Low),
            "medium" => (true, ReasoningEffort.Medium),
            "high" => (true, ReasoningEffort.High),
            "extra_high" => (true, ReasoningEffort.ExtraHigh),
            _ => (false, default),
        };
        return known;
    }

    /// <summary>The <see cref="ReasoningEffort"/> for <paramref name="name"/>.</summary>
    /// <param name="name">One of <see cref="Values"/>.</param>
    /// <param name="paramName">The caller's parameter or setting that held it, for the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not one of <see cref="Values"/>.</exception>
    public static ReasoningEffort Parse(string name, string paramName) =>
        TryParse(name, out var effort)
            ? effort
            : throw new ArgumentException(
                $"Unknown reasoning effort '{name}'. Expected {string.Join(", ", Values.Take(Values.Count - 1))} or {Values[^1]}.",
                paramName);
}
