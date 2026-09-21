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

    [Fact]
    public void GetAll_ReturnsAllTools()
    {
        // Act
        var tools = BuiltInTools.GetAll(_testDir);

        // Assert
        Assert.Equal(7, tools.Count); // ReadFile, WriteFile, ListDirectory, GlobFiles, GrepFiles, ExecuteCommand, ManageTodo
    }

    [Fact]
    public void GetAll_WithWebSearchTool_ReturnsNineTools()
    {
        // Arrange
        using var searchClient = new WebLookup.WebSearchClient();
        using var siteExplorer = new WebLookup.SiteExplorer();
        var webSearchTool = new WebSearchTool(searchClient, siteExplorer);

        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: webSearchTool);

        // Assert — 7 base tools + WebSearch + ExploreSite
        Assert.Equal(9, tools.Count);
    }

    [Fact]
    public void GetAll_WithNullWebSearchTool_ReturnsSevenTools()
    {
        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: null);

        // Assert
        Assert.Equal(7, tools.Count);
    }

    [Fact]
    public void GetAll_WithDeepResearchTool_ReturnsTenTools()
    {
        // Arrange
        using var searchClient = new WebLookup.WebSearchClient();
        using var siteExplorer = new WebLookup.SiteExplorer();
        var webSearchTool = new WebSearchTool(searchClient, siteExplorer);
        var mockFactory = Substitute.For<IChatClientFactory>();
        var deepResearchTool = new DeepResearchTool(mockFactory, new DeepResearchConfig());

        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: webSearchTool, deepResearchTool: deepResearchTool);

        // Assert — 7 base + WebSearch + ExploreSite + DeepResearch
        Assert.Equal(10, tools.Count);
    }

    [Fact]
    public void GetAll_WithDeepResearchOnly_ReturnsEightTools()
    {
        // Arrange
        var mockFactory = Substitute.For<IChatClientFactory>();
        var deepResearchTool = new DeepResearchTool(mockFactory, new DeepResearchConfig());

        // Act
        var tools = BuiltInTools.GetAll(_testDir, oopsService: null, webSearchTool: null, deepResearchTool: deepResearchTool);

        // Assert — 7 base + DeepResearch
        Assert.Equal(8, tools.Count);
    }

    [Fact]
    public async Task GetAll_WithOopsService_VersionsWritesMadeThroughTheTool()
    {
        var oops = Substitute.For<IronHive.Host.Oops.IOopsService>();
        oops.IsTracked(Arg.Any<string>()).Returns(true);
        oops.SaveAsync(Arg.Any<string>(), Arg.Any<string?>())
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
