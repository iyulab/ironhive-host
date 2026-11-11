using IronHive.Agent.Skills;
using Microsoft.Extensions.Logging;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// What a host shows about skills discovery could not use. The agent layer computes the verdict
/// (<see cref="SkillsLoader.Diagnostics"/>); without this, a <c>SKILL.md</c> that breaks the specification is dropped and the agent
/// simply lacks the skill. The startup log and <c>doctor</c> read the same mapping, so they cannot disagree.
/// </summary>
internal static class SkillDiagnosticsReport
{
    /// <summary>
    /// A skill that was rejected is a warning (the person who wrote it expected it to load). A configured root that does not
    /// exist, and the notes on skills that did load, are information: the documented example roots name directories that
    /// many projects do not have, and that must not warn at every start.
    /// </summary>
    public static LogLevel LevelFor(SkillDiagnostic diagnostic)
        => diagnostic.Code == SkillDiagnosticCode.RootNotFound || diagnostic.Severity == SkillDiagnosticSeverity.Warning
            ? LogLevel.Information
            : LogLevel.Warning;

    /// <summary>The lines <c>doctor</c> prints for one discovery result: a verdict line, then one line per diagnostic.</summary>
    public static IReadOnlyList<DoctorLine> DoctorLines(SkillDiscoveryResult result)
    {
        var lines = new List<DoctorLine>
        {
            new(Ok: true, IsWarning: false, result.Skills.Count == 1
                ? "1 skill found"
                : $"{result.Skills.Count} skills found")
        };
        foreach (var diagnostic in result.Diagnostics)
        {
            lines.Add(new(Ok: false, IsWarning: true, $"{diagnostic.Code}: {diagnostic.Path} - {diagnostic.Message}"));
        }

        return lines;
    }

    /// <summary>One <c>doctor</c> line: a passed check, or a warning (a rejected skill never fails the run).</summary>
    internal readonly record struct DoctorLine(bool Ok, bool IsWarning, string Text);
}
