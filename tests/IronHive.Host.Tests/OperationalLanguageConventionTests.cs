using Iyu.Conventions.Testing;
using Xunit;

namespace IronHive.Host.Tests;

/// <summary>
/// Operational text - every <c>[LoggerMessage]</c> template and every exception message - is ASCII. Operators grep it,
/// paste it into issues and search it in log pipelines whose tokenizers split on Latin word boundaries; a dash or an
/// arrow outside ASCII is as opaque there as a Korean word. The scan is <c>Iyu.Conventions.Testing</c>'s, shared with
/// the other repositories, over the same assemblies the options roster scans (every assembly this repository ships).
/// </summary>
public class OperationalLanguageConventionTests
{
    private static readonly Lazy<OperationalLanguageReport> Result = new(() =>
        OperationalLanguage.Scan(OptionsReachabilityRosterTests.Libraries, OperationalLanguage.NonAscii));

    [Fact]
    public void LogTemplatesAndExceptionMessages_AreAscii()
    {
        var findings = Result.Value.Findings;
        Assert.True(findings.Count == 0,
            "Non-ASCII operational text:\n" + string.Join("\n", findings.Select(f => $"  [{f.Kind}] {f.Location}: {f.Text}")));
    }

    /// <summary>
    /// No log template carries what a sender or a model wrote - a request line, a prompt, a reason someone typed. A
    /// server's logs leave its per-user boundary and outlive a deletion request; an unreadable line is logged by the
    /// exception type, and the sender hears the details on its own channel.
    /// </summary>
    [Fact]
    public void LogTemplates_CarryNoContent()
    {
        var report = OperationalLanguage.Scan(OptionsReachabilityRosterTests.Libraries,
            OperationalLanguage.PlaceholderNamed([.. OperationalLanguage.ContentPlaceholderNames, "Reason", "Line", "Request", "Message"]));
        Assert.True(report.Findings.Count == 0,
            "Log templates that carry content (log a length, a count or a type instead):\n"
            + string.Join("\n", report.Findings.Select(f => $"  {f.Location}: {f.Text}")));
    }

    // Positive control: the scan must read operational text at all, or an empty finding list would pass because the
    // reader sees nothing.
    [Fact]
    public void Scan_SeesOperationalText() =>
        Assert.True(Result.Value.LogMessagesRead + Result.Value.ExceptionLiteralsRead > 0,
            $"log templates read: {Result.Value.LogMessagesRead}, exception messages read: {Result.Value.ExceptionLiteralsRead}");
}
