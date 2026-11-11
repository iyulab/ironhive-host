using AwesomeAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Skills;
using IronHive.Cli.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Agent;

/// <summary>
/// A <c>SKILL.md</c> that breaks the specification is refused by discovery and the loader drops it, so the agent simply lacks
/// the skill. The agent layer computes the verdict (<see cref="SkillsLoader.Diagnostics"/>); these facts pin that the host
/// shows it - in its startup log and in <c>doctor</c> - instead of leaving it as a return value nobody reads.
/// </summary>
public class SkillDiagnosticsSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"host-skills-{Guid.NewGuid():N}");

    public SkillDiagnosticsSurfaceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ARefusedSkill_IsAWarning_AndAMissingRootOrANoteOnALoadedSkillIsInformation()
    {
        SkillDiagnosticsReport.LevelFor(new(SkillDiagnosticCode.InvalidFrontmatter, "a/SKILL.md", "x")).Should().Be(LogLevel.Warning);
        SkillDiagnosticsReport.LevelFor(new(SkillDiagnosticCode.UnexpectedField, "a/SKILL.md", "x")).Should().Be(LogLevel.Warning, "an unexpected key is an error by default");
        // The documented example roots name directories many projects do not have: that must not warn at every start.
        SkillDiagnosticsReport.LevelFor(new(SkillDiagnosticCode.RootNotFound, "~/skills", "x")).Should().Be(LogLevel.Information);
        SkillDiagnosticsReport.LevelFor(new(SkillDiagnosticCode.UnexpectedField, "a/SKILL.md", "x", SkillDiagnosticSeverity.Warning)).Should().Be(LogLevel.Information);
        SkillDiagnosticsReport.LevelFor(new(SkillDiagnosticCode.Shadowed, "a/SKILL.md", "x", SkillDiagnosticSeverity.Warning)).Should().Be(LogLevel.Information);
    }

    [Fact]
    public void TheStartupLog_NamesTheRefusedSkillFileAndTheReason_AndStaysQuietForAValidOne()
    {
        var invalid = WriteSkill("bad-skill", "---\nname: bad-skill\ndescription: Does a thing.\nlicence-typo: MIT\n---\nbody");
        WriteSkill("good-skill", "---\nname: good-skill\ndescription: Does another thing.\n---\nbody");
        var loader = SkillsLoader.Create(new SkillsConfig { Roots = [_root] });
        var logger = new CapturingLogger();

        _ = new AgentLoopFactory(Substitute.For<IronHive.Agent.Providers.IChatClientFactory>(), Substitute.For<IThinkingTurnManager>(),
            logger: logger, skills: loader);

        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which;
        warning.Message.Should().Contain(invalid).And.Contain("was not used").And.Contain("licence-typo");
        logger.Entries.Should().NotContain(e => e.Message.Contains("good-skill"), "a valid skill needs no log line");
    }

    [Fact]
    public void ADirectoryWithNoSkillProblems_LogsNothing()
    {
        WriteSkill("good-skill", "---\nname: good-skill\ndescription: Does another thing.\n---\nbody");
        var logger = new CapturingLogger();

        _ = new AgentLoopFactory(Substitute.For<IronHive.Agent.Providers.IChatClientFactory>(), Substitute.For<IThinkingTurnManager>(),
            logger: logger, skills: SkillsLoader.Create(new SkillsConfig { Roots = [_root] }));

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Doctor_ListsEachDiagnosticAsAWarning_AfterAVerdictLine()
    {
        var invalid = WriteSkill("bad-skill", "---\nname: not-the-directory\ndescription: Does a thing.\n---\nbody");
        WriteSkill("good-skill", "---\nname: good-skill\ndescription: Does another thing.\n---\nbody");

        var lines = SkillDiagnosticsReport.DoctorLines(SkillDiscovery.Discover(new SkillsConfig { Roots = [_root] }));

        lines[0].Should().Be(new SkillDiagnosticsReport.DoctorLine(Ok: true, IsWarning: false, "1 skill found"));
        var finding = lines.Should().ContainSingle(l => !l.Ok).Which;
        finding.IsWarning.Should().BeTrue("a rejected skill never fails the doctor run");
        finding.Text.Should().Contain(nameof(SkillDiagnosticCode.NameDoesNotMatchDirectory)).And.Contain(invalid);
    }

    [Fact]
    public void Doctor_ForAFolderOfValidSkills_HasOnlyTheVerdictLine()
    {
        WriteSkill("a-skill", "---\nname: a-skill\ndescription: One.\n---\nbody");
        WriteSkill("b-skill", "---\nname: b-skill\ndescription: Two.\n---\nbody");

        var lines = SkillDiagnosticsReport.DoctorLines(SkillDiscovery.Discover(new SkillsConfig { Roots = [_root] }));

        lines.Should().ContainSingle().Which.Text.Should().Be("2 skills found");
    }

    private string WriteSkill(string directory, string content)
    {
        var dir = Path.Combine(_root, directory);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "SKILL.md");
        File.WriteAllText(file, content);
        return file;
    }

    private sealed class CapturingLogger : ILogger<AgentLoopFactory>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
