using IronHive.Agent.Providers;
using IronHive.Host.Config;
using IronHive.Host.Tools;
using NSubstitute;

namespace IronHive.Host.Tests.Tools;

/// <summary>
/// What the host adds to the agent's tool list. The file tools themselves are the agent's and are
/// tested there; these facts cover the list the host composes and that a write made through it is
/// versioned.
/// </summary>
public class BuiltInToolsTests : IDisposable
{
    private readonly string _testDir;

    public BuiltInToolsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ironhive-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    // The host offers the agent's built-in tools as they are and adds only its own optional ones; the base count is the
    // agent's, so a tool the agent adds (EditFile in 0.38.0) does not need a second edit here.
    private int AgentToolCount => IronHive.Agent.Tools.BuiltInTools.GetAll(_testDir).Count;

    [Fact]
    public void GetAll_ReturnsTheAgentsTools()
    {
        // Act
        var tools = BuiltInTools.GetAll(_testDir);

        // Assert
        Assert.Equal(IronHive.Agent.Tools.BuiltInTools.GetAll(_testDir).Select(t => t.Name), tools.Select(t => t.Name));
    }

    [Fact]
    public void GetAll_WithWebSearchTool_AddsWebSearchAndExploreSite()
    {
        // Arrange
        using var searchClient = new WebLookup.WebSearchClient();
        using var siteExplorer = new WebLookup.SiteExplorer();
        var webSearchTool = new WebSearchTool(searchClient, siteExplorer);

        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: webSearchTool);

        // Assert — the agent's tools + WebSearch + ExploreSite
        Assert.Equal(AgentToolCount + 2, tools.Count);
    }

    [Fact]
    public void GetAll_WithNullWebSearchTool_AddsNothing()
    {
        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: null);

        // Assert
        Assert.Equal(AgentToolCount, tools.Count);
    }

    [Fact]
    public void GetAll_WithDeepResearchTool_AddsThree()
    {
        // Arrange
        using var searchClient = new WebLookup.WebSearchClient();
        using var siteExplorer = new WebLookup.SiteExplorer();
        var webSearchTool = new WebSearchTool(searchClient, siteExplorer);
        var mockFactory = Substitute.For<IChatClientFactory>();
        var deepResearchTool = new DeepResearchTool(mockFactory, new DeepResearchConfig());

        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: webSearchTool, deepResearchTool: deepResearchTool);

        // Assert — the agent's tools + WebSearch + ExploreSite + DeepResearch
        Assert.Equal(AgentToolCount + 3, tools.Count);
    }

    [Fact]
    public void GetAll_WithDeepResearchOnly_AddsOne()
    {
        // Arrange
        var mockFactory = Substitute.For<IChatClientFactory>();
        var deepResearchTool = new DeepResearchTool(mockFactory, new DeepResearchConfig());

        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: null, deepResearchTool: deepResearchTool);

        // Assert — the agent's tools + DeepResearch
        Assert.Equal(AgentToolCount + 1, tools.Count);
    }

    [Fact]
    public async Task GetAll_WithOopsService_VersionsWritesMadeThroughTheTool()
    {
        var oops = Substitute.For<IronHive.Host.Oops.IOopsService>();
        oops.IsTracked(Arg.Any<string>()).Returns(true);
        oops.SaveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IronHive.Host.Oops.OopsResult { Success = true, Output = "" });

        var write = BuiltInTools.GetAll(_testDir, oops)
            .OfType<Microsoft.Extensions.AI.AIFunction>()
            .Single(t => t.Name == "WriteFile");

        var result = await write.InvokeAsync(
            new Microsoft.Extensions.AI.AIFunctionArguments { ["path"] = "notes.md", ["content"] = "x" },
            TestContext.Current.CancellationToken);

        Assert.Equal("Successfully wrote to file: notes.md (oops snapshot saved)", result?.ToString());
        Assert.True(File.Exists(Path.Combine(_testDir, "notes.md")));
    }

    [Fact]
    public void TheHost_DefinesNoToolProviderOfItsOwn()
    {
        // The file tools have one implementation, in IronHive.Agent. A second one here would let the two drift.
        var hostTypes = typeof(BuiltInTools).Assembly.GetTypes().Select(t => t.FullName);

        Assert.DoesNotContain("IronHive.Host.Tools.ToolProvider", hostTypes);
    }
}
