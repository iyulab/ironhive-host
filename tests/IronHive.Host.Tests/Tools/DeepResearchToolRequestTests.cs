using AwesomeAssertions;
using IronHive.DeepResearch.Models.Research;
using IronHive.Host.Config;
using IronHive.Host.Tools;

namespace IronHive.Host.Tests.Tools;

public class DeepResearchToolRequestTests
{
    [Fact]
    public void TheConfiguredIterationLimit_ReachesTheRequest()
    {
        // The research loop reads ResearchRequest.MaxIterations (capped by depth). The config value used to go to
        // DeepResearchOptions.DefaultMaxIterations, which nothing read, so every query ran to the request default of 5.
        var request = DeepResearchTool.CreateRequest("q", "deep", new DeepResearchConfig { MaxIterations = 2 });

        request.MaxIterations.Should().Be(2);
        request.Depth.Should().Be(ResearchDepth.Comprehensive);
        request.Query.Should().Be("q");
    }
}
