using AwesomeAssertions;
using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Cli.Infrastructure;
using IronHive.Cli.Services;
using IronHive.Host.Config;
using IronHive.Host.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// What «always approve» covers on the console. For a tool the permission rules judge on a path it covers the folder that
/// was asked about, and nothing else; every other tool keeps its tool-wide memory.
/// </summary>
public class ConsoleAlwaysApproveScopeTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ironhive-always-scope");

    /// <summary>A console whose person picks «always» whenever it is offered, and a record of what each question offered.</summary>
    private sealed class Person
    {
        public List<IReadOnlyList<string>> Questions { get; } = [];

        public ConsoleApprovalService Console(string? workingDirectory = null) => new(
            hasInteractiveConsole: () => true,
            showProposalRecommendation: false,
            workingDirectory: () => workingDirectory ?? Root,
            choose: (_, choices) =>
            {
                Questions.Add(choices);
                return choices.FirstOrDefault(c => c.StartsWith("Yes, always", StringComparison.Ordinal)) ?? choices[0];
            });
    }

    private static ApprovalRequest Call(string tool, params (string Key, object? Value)[] arguments) => new()
    {
        ToolName = tool,
        Arguments = arguments.ToDictionary(a => a.Key, a => a.Value),
        RiskAssessment = RiskAssessment.Risky(RiskLevel.Medium, "asked", "Allow this operation?")
    };

    private static async Task<bool> IsAskedAsync(ConsoleApprovalService console, Person person, ApprovalRequest request)
    {
        var before = person.Questions.Count;
        var result = await console.RequestApprovalAsync(request, TestContext.Current.CancellationToken);
        result.Approved.Should().BeTrue();
        return person.Questions.Count > before;
    }

    [Theory]
    [InlineData("ListDirectory")]
    [InlineData("list_directory")]
    [InlineData("GlobFiles")]
    [InlineData("glob_files")]
    [InlineData("GrepFiles")]
    [InlineData("grep_files")]
    public async Task AlwaysForOneFolder_DoesNotCoverAnotherFolder(string tool)
    {
        var person = new Person();
        var console = person.Console();

        (await IsAskedAsync(console, person, Call(tool, ("path", "a")))).Should().BeTrue();

        (await IsAskedAsync(console, person, Call(tool, ("path", "a")))).Should().BeFalse();
        (await IsAskedAsync(console, person, Call(tool, ("path", "b")))).Should().BeTrue();
    }

    [Fact]
    public async Task AFolder_IsTheSameFolderInEverySpellingThePathResolvesTo_AndItsSubfolderIsAnother()
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a")));

        foreach (var spelling in new[] { "./a", "b/../a", Path.Combine(Root, "a") })
        {
            (await IsAskedAsync(console, person, Call("GrepFiles", ("path", spelling)))).Should().BeFalse(spelling);
        }

        // A subfolder can be asked about by a rule of its own.
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a/inner")))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "..")))).Should().BeTrue();
    }

    [Fact]
    public async Task ATrailingSeparator_IsAskedAgain_BecauseTheRulesCanJudgeItDifferently()
    {
        // "x/**" matches "x/" and not "x": the two are asked on different terms, and what the search may return differs.
        var person = new Person();
        var console = person.Console();
        var outside = Path.Combine(Path.GetTempPath(), "ironhive-always-outside", "x");
        var rules = new PermissionConfig
        {
            DefaultAction = PermissionAction.Ask,
            WorkingDirectory = Root,
            ExternalDirectory = [new PermissionRule { Pattern = outside.Replace('\\', '/') + "/**", Action = PermissionAction.Ask }],
        };
        var evaluator = new PermissionEvaluator(rules);
        var withSeparator = outside + Path.DirectorySeparatorChar;
        evaluator.EvaluateRead(withSeparator).MatchedRule.Should().NotBeNull();
        evaluator.EvaluateRead(outside).MatchedRule.Should().BeNull();

        await IsAskedAsync(console, person, Call("GrepFiles", ("path", outside)));

        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", withSeparator)))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a")))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a/")))).Should().BeTrue();
    }

    [Theory]
    [InlineData("GlobFiles", "pattern", "../**")]
    [InlineData("glob_files", "pattern", "sub/../../other/*.cs")]
    [InlineData("GrepFiles", "filePattern", "..\\**\\*.env")]
    [InlineData("grep_files", "filePattern", "../*")]
    public async Task ASearchWhoseFilePatternClimbsOut_IsAskedEveryTime(string tool, string argument, string glob)
    {
        // The yes was for the folder; a ".." segment makes the call look outside it.
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call(tool, ("path", "a")));

        (await IsAskedAsync(console, person, Call(tool, ("path", "a"), (argument, glob)))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call(tool, ("path", "a"), (argument, glob)))).Should().BeTrue();
        person.Questions[^1].Should().NotContain(c => c.Contains("always", StringComparison.OrdinalIgnoreCase));
        // Two dots inside a name climb nowhere.
        (await IsAskedAsync(console, person, Call(tool, ("path", "a"), (argument, "..notes/*..bak")))).Should().BeFalse();
    }

    [Fact]
    public async Task TheTextAGrepLooksFor_IsNotAPath()
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a"), ("filePattern", "*.cs"), ("pattern", "x")));

        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a"), ("filePattern", "*.cs"), ("pattern", "../.."))))
            .Should().BeFalse();
    }

    [Fact]
    public async Task NoPath_IsTheWorkingDirectory_AsThePolicyReadsIt()
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call("GrepFiles"));

        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", ".")))).Should().BeFalse();
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "")))).Should().BeFalse();
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", Root)))).Should().BeFalse();
        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a")))).Should().BeTrue();
    }

    [Fact]
    public async Task ARelativePath_IsResolvedAgainstTheLoopsDirectory_NotTheProcessDirectory()
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a")));

        (await IsAskedAsync(console, person, Call("GrepFiles", ("path", Path.Combine(Directory.GetCurrentDirectory(), "a")))))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("ReadFile")]
    [InlineData("read_file")]
    [InlineData("WriteFile")]
    [InlineData("write_file")]
    [InlineData("EditFile")]
    [InlineData("edit_file")]
    [InlineData("DeleteFile")]
    [InlineData("delete_file")]
    public async Task AlwaysForAFile_CoversTheFilesOfItsFolder_AndNoOtherFolder(string tool)
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call(tool, ("path", "a/one.env")));

        (await IsAskedAsync(console, person, Call(tool, ("path", "a/two.env")))).Should().BeFalse();
        (await IsAskedAsync(console, person, Call(tool, ("path", "b/one.env")))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call(tool, ("path", "a/inner/one.env")))).Should().BeTrue();
    }

    [Theory]
    [InlineData("MoveFile")]
    [InlineData("move_file")]
    public async Task AlwaysForAMove_CoversOnlyTheSamePairOfFolders(string tool)
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call(tool, ("source", "a/x"), ("destination", "b/x")));

        (await IsAskedAsync(console, person, Call(tool, ("source", "a/y"), ("destination", "b/z")))).Should().BeFalse();
        (await IsAskedAsync(console, person, Call(tool, ("source", "a/y"), ("destination", "c/y")))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call(tool, ("source", "c/y"), ("destination", "b/y")))).Should().BeTrue();
        // The reverse direction takes from the other folder.
        (await IsAskedAsync(console, person, Call(tool, ("source", "b/x"), ("destination", "a/x")))).Should().BeTrue();
    }

    [Theory]
    [InlineData("WriteFile")]
    [InlineData("DeleteFile")]
    [InlineData("MoveFile")]
    public async Task ACallWhosePathIsUnknown_IsNotOfferedAlways(string tool)
    {
        var person = new Person();
        var console = person.Console();

        (await IsAskedAsync(console, person, Call(tool))).Should().BeTrue();
        (await IsAskedAsync(console, person, Call(tool))).Should().BeTrue();

        person.Questions[0].Should().NotContain(c => c.Contains("always", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheAlwaysChoice_SaysWhatItCovers()
    {
        var person = new Person();
        var console = person.Console();

        await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a")));
        await IsAskedAsync(console, person, Call("WriteFile", ("path", "b/new.txt")));
        await IsAskedAsync(console, person, Call("MoveFile", ("source", "a/x"), ("destination", "b/x")));
        await IsAskedAsync(console, person, Call("ExecuteCommand", ("command", "git log")));

        var always = person.Questions.Select(q => q.Single(c => c.StartsWith("Yes, always", StringComparison.Ordinal))).ToArray();
        var a = Spectre.Console.Markup.Escape(Path.Combine(Root, "a"));
        var b = Spectre.Console.Markup.Escape(Path.Combine(Root, "b"));
        always[0].Should().Be($"Yes, always approve this tool on the folder {a}, including what a call reaches below it");
        always[1].Should().Be($"Yes, always approve this tool for files in {b}");
        always[2].Should().Be($"Yes, always approve this tool for moves from {a} to {b}");
        always[3].Should().Be("Yes, always approve this tool");
    }

    [Theory]
    [InlineData("ExecuteCommand", "command")]
    [InlineData("mcp__server__search", "query")]
    [InlineData("web_search", "query")]
    public async Task AToolThatIsNotJudgedOnAPath_KeepsItsToolWideMemory(string tool, string argument)
    {
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call(tool, (argument, "one")));

        (await IsAskedAsync(console, person, Call(tool, (argument, "two")))).Should().BeFalse();
    }

    [Fact]
    public void EveryBuiltInToolThePolicyJudgesOnAPath_IsRememberedByFolder()
    {
        // A path tool this host does not know would fall back to the tool-wide memory: every folder after one yes.
        var rules = new PermissionConfig
        {
            DefaultAction = PermissionAction.Allow,
            WorkingDirectory = Root,
            Read = [new PermissionRule { Pattern = "secret/**", Action = PermissionAction.Deny }],
            Edit = [new PermissionRule { Pattern = "secret/**", Action = PermissionAction.Deny }],
        };
        var policy = new ToolCallPolicy(rules);
        // Every argument the tool declares points under the folder, whatever the argument is called.
        static Dictionary<string, object?> Under(AIFunction tool, string folder) => tool.JsonSchema
            .GetProperty("properties")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)(folder + "/x"));

        // Judged on a path: refused under the closed folder and not under another. (A shell call whose command is a
        // path is judged as a command, the same under both.)
        var judgedOnAPath = BuiltInTools.GetAll(Root)
            .OfType<AIFunction>()
            .Where(t => policy.Evaluate(t.Name, Under(t, "secret")).Verdict == PermissionAction.Deny
                && policy.Evaluate(t.Name, Under(t, "open")).Verdict != PermissionAction.Deny)
            .ToList();

        judgedOnAPath.Select(t => t.Name).Should().Contain(
            ["ReadFile", "WriteFile", "EditFile", "DeleteFile", "MoveFile", "ListDirectory", "GlobFiles", "GrepFiles"]);
        foreach (var tool in judgedOnAPath)
        {
            var one = ConsoleApprovalService.AlwaysScopeOf(tool.Name, Under(tool, "one/deep"), Root);
            var two = ConsoleApprovalService.AlwaysScopeOf(tool.Name, Under(tool, "two/deep"), Root);
            one.Should().NotBeNull(tool.Name);
            two!.Key.Should().NotBe(one!.Key, tool.Name);
        }
    }

    [Theory]
    [InlineData(@"a::$INDEX_ALLOCATION")]
    [InlineData(@"a\..:s")]
    [InlineData(@"a\one.txt:stream")]
    [InlineData(@"\\?\C:\a")]
    [InlineData(@"\\.\C:\a")]
    [InlineData(@"\??\C:\a")]
    [InlineData("//?/C:/a")]
    [InlineData("//./C:/a")]
    public async Task OnWindows_AStreamOrDeviceSpelling_IsNeverRemembered(string path)
    {
        // The rules rewrite these before judging them, so the folder they are judged as is not the one the text names.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Stream names and device prefixes are Windows spellings.");
        var person = new Person();
        var console = person.Console();
        await IsAskedAsync(console, person, Call("GrepFiles", ("path", "a")));
        await IsAskedAsync(console, person, Call("ReadFile", ("path", "a/one.txt")));

        foreach (var tool in new[] { "GrepFiles", "ReadFile" })
        {
            (await IsAskedAsync(console, person, Call(tool, ("path", path)))).Should().BeTrue(tool);
            (await IsAskedAsync(console, person, Call(tool, ("path", path)))).Should().BeTrue(tool);
            person.Questions[^1].Should().NotContain(c => c.Contains("always", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheCliGivesTheConsoleTheDirectoryTheRulesJudgeAgainst()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIronHiveServices();
        var config = new IronHiveConfig();
        config.Permissions.WorkingDirectory = Root;
        services.AddSingleton(config);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ConsoleApprovalService>().WorkingDirectory.Should().Be(Root);
    }
}
