using AwesomeAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The host's advisor section: <c>advisor.model</c> in config.yaml puts an <c>advisor</c> tool on every loop the host
/// builds, backed by a client for that model; without it no such tool exists.
/// </summary>
public sealed class AdvisorWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "advisor-wiring-" + Guid.NewGuid().ToString("N"));

    public AdvisorWiringTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheAdvisorSection_LoadsFromConfigYaml_AndIsARecognisedKey()
    {
        const string yaml = """
            advisor:
              provider: anthropic
              model: strong-model
              maxCalls: 2
            """;
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), yaml);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.Advisor.Provider.Should().Be("anthropic");
        config.Advisor.Model.Should().Be("strong-model");
        config.Advisor.MaxCalls.Should().Be(2);
        ConfigurationManager.FindUnknownTopLevelKeys(yaml).Should().BeEmpty();
    }

    [Fact]
    public async Task WithAnAdvisorModel_EveryLoop_GetsAnAdvisorTool_OnThatModel()
    {
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IChatClient>());
        factory.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IChatClient>());
        var loops = new AgentLoopFactory(factory, Substitute.For<IThinkingTurnManager>(),
            advisor: new AdvisorConfig { Provider = "anthropic", Model = "strong-model" });

        var created = await loops.CreateWithToolsAsync(
            new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);

        created.Tools.Select(t => t.Name).Should().Contain("advisor");
        await factory.Received(1).CreateAsync("anthropic", "strong-model", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAnAdvisorModel_ThereIsNoAdvisorTool()
    {
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IChatClient>());
        var loops = new AgentLoopFactory(factory, Substitute.For<IThinkingTurnManager>(), advisor: new AdvisorConfig());

        var created = await loops.CreateWithToolsAsync(
            new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);

        created.Tools.Select(t => t.Name).Should().NotContain("advisor");
    }
}
