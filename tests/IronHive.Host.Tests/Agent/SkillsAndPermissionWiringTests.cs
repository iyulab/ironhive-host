using AwesomeAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Loop;
using IronHive.Agent.Permissions;
using IronHive.Agent.Skills;
using IronHive.Agent.Tools;
using IronHive.Host.Config;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Agent;

/// <summary>
/// The assembly layer is where three agent-level decisions either reach a running loop or do not: the
/// skills loader's tool, a host's <see cref="FileToolOptions"/>, and the permission rules' working
/// directory. Each of these is a declaration somewhere else; these facts pin that the factory honours it.
/// </summary>
public class SkillsAndPermissionWiringTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"host-wiring-{Guid.NewGuid():N}");

    public SkillsAndPermissionWiringTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASkillsLoaderWithSkills_PutsLoadSkillAmongTheLoopsTools()
    {
        var loader = Loader(withSkill: true);

        var created = await Factory(skills: loader).CreateWithToolsAsync(new AgentLoopFactoryOptions { Model = "gpt-4o" }, TestContext.Current.CancellationToken);

        created.Tools.OfType<AIFunction>().Select(f => f.Name).Should().ContainSingle(n => n == SkillsLoader.LoadToolName);
    }

    [Fact]
    public async Task ALoaderThatFoundNothing_AddsNoTool()
    {
        var loader = Loader(withSkill: false);

        var created = await Factory(skills: loader).CreateWithToolsAsync(new AgentLoopFactoryOptions { Model = "gpt-4o" }, TestContext.Current.CancellationToken);

        created.Tools.OfType<AIFunction>().Select(f => f.Name).Should().NotContain(SkillsLoader.LoadToolName);
    }

    [Fact]
    public async Task FileToolOptionsFromTheContainer_ReachTheFileTools()
    {
        var inside = Path.Combine(_dir, "inside");
        Directory.CreateDirectory(inside);
        await File.WriteAllTextAsync(Path.Combine(_dir, "outside.txt"), "s3cr3t", TestContext.Current.CancellationToken);
        var options = new FileToolOptions { AllowedRoots = [inside] };

        var created = await Factory(fileToolOptions: options).CreateWithToolsAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = inside }, TestContext.Current.CancellationToken);
        var readFile = (AIFunction)created.Tools.Single(t => (t as AIFunction)?.Name == "ReadFile");
        var result = (await readFile.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "../outside.txt" }), TestContext.Current.CancellationToken))!.ToString()!;

        result.Should().NotContain("s3cr3t");
        result.Should().Contain("outside");
    }

    [Fact]
    public void HostBuiltInTools_KeepOopsVersioning_WhenTheOptionsBringOnlyRoots()
    {
        var oops = Substitute.For<IronHive.Host.Oops.IOopsService>();

        var tools = IronHive.Host.Tools.BuiltInTools.GetAll(_dir, oops, webSearchTool: null, deepResearchTool: null, new FileToolOptions { AllowedRoots = [_dir] });

        tools.Should().HaveCount(7, "roots do not remove or add tools");
    }

    [Fact]
    public async Task PermissionsJudgingAnotherDirectory_AreRefused_BeforeALoopExists()
    {
        var toolsDir = Path.Combine(_dir, "project");
        var rulesDir = Path.Combine(_dir, "elsewhere");
        Directory.CreateDirectory(toolsDir);
        Directory.CreateDirectory(rulesDir);
        var permissions = PermissionConfig.CreateDefault();
        permissions.WorkingDirectory = rulesDir;

        var act = () => Factory(permissions: permissions).CreateWithToolsAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = toolsDir }, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain(toolsDir).And.Contain(rulesDir);
    }

    [Fact]
    public async Task PermissionsJudgingTheSameDirectory_BuildTheLoop()
    {
        var dir = Path.Combine(_dir, "project");
        Directory.CreateDirectory(dir);
        var permissions = PermissionConfig.CreateDefault();
        permissions.WorkingDirectory = dir + Path.DirectorySeparatorChar;

        var created = await Factory(permissions: permissions).CreateWithToolsAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = dir }, TestContext.Current.CancellationToken);

        created.Loop.Should().NotBeNull();
    }

    [Fact]
    public async Task PermissionsWithoutAWorkingDirectory_AreNotJudged()
    {
        var created = await Factory(permissions: PermissionConfig.CreateDefault()).CreateWithToolsAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = _dir }, TestContext.Current.CancellationToken);

        created.Loop.Should().NotBeNull();
    }

    [Fact]
    public void TheSkillsSection_MapsOntoTheAgentsConfig_WithRootsResolvedAgainstTheBase()
    {
        var section = new SkillsHostConfig
        {
            Roots = ["skills", Path.Combine(_dir, "abs")],
            Enabled = ["b", "a"],
            Exclude = ["c"],
            MaxMetadataCharacters = 500,
            AcceptUnknownFields = true
        };

        var config = section.ToSkillsConfig(_dir);

        config.Roots.Should().Equal(Path.Combine(_dir, "skills"), Path.Combine(_dir, "abs"));
        config.Enabled.Should().Equal("b", "a");
        config.Exclude.Should().Equal("c");
        config.MaxMetadataCharacters.Should().Be(500);
        config.UnknownFields.Should().Be(UnknownFieldPolicy.Accept);
        new SkillsHostConfig().ToSkillsConfig(_dir).UnknownFields.Should().Be(UnknownFieldPolicy.Reject);
    }

    private SkillsLoader Loader(bool withSkill)
    {
        var root = Path.Combine(_dir, "skills");
        Directory.CreateDirectory(root);
        if (withSkill)
        {
            var d = Path.Combine(root, "pdf-processing");
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "SKILL.md"), "---\nname: pdf-processing\ndescription: Extract PDF text.\n---\nbody");
        }

        return SkillsLoader.Create(new SkillsConfig { Roots = [root] });
    }

    private static IronHive.Cli.Infrastructure.AgentLoopFactory Factory(
        SkillsLoader? skills = null, FileToolOptions? fileToolOptions = null, PermissionConfig? permissions = null)
    {
        var clientFactory = Substitute.For<IronHive.Agent.Providers.IChatClientFactory>();
        clientFactory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IChatClient>(new MockChatClient()));
        var turnManager = Substitute.For<IThinkingTurnManager>();
        return new IronHive.Cli.Infrastructure.AgentLoopFactory(
            clientFactory, turnManager, skills: skills, fileToolOptions: fileToolOptions, permissions: permissions);
    }
}
