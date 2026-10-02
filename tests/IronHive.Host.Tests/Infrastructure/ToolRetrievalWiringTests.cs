using AwesomeAssertions;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// CLI / server loops send the model every registered tool unless <c>toolRetrieval.enabled</c> is <c>true</c>; then each
/// request carries only the tools the retriever selects for the user's request, up to <c>maxTools</c> plus the pinned ones.
/// </summary>
public sealed class ToolRetrievalWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toolretrieval-wiring-" + Guid.NewGuid().ToString("N"));

    public ToolRetrievalWiringTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheToolRetrievalSection_LoadsFromConfigYaml_AndIsARecognisedKey()
    {
        const string yaml = """
            toolRetrieval:
              enabled: true
              maxTools: 4
              minRelevanceScore: 0.2
              minScoredSlots: 2
              alwaysInclude: [read_file]
              stickyToolLimit: 24
            """;
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), yaml);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.ToolRetrieval.Enabled.Should().BeTrue();
        config.ToolRetrieval.MaxTools.Should().Be(4);
        config.ToolRetrieval.MinRelevanceScore.Should().Be(0.2f);
        config.ToolRetrieval.MinScoredSlots.Should().Be(2);
        config.ToolRetrieval.AlwaysInclude.Should().Equal("read_file");
        config.ToolRetrieval.StickyToolLimit.Should().Be(24);
        ConfigurationManager.FindUnknownTopLevelKeys(yaml).Should().BeEmpty();
    }

    [Fact]
    public async Task ByDefault_EveryToolIsSent()
    {
        var (factory, toolCounts) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager());

        var created = await loops.CreateWithToolsAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await created.Loop.RunAsync("read the file notes.txt", TestContext.Current.CancellationToken);

        toolCounts.Should().ContainSingle().Which.Should().Be(created.Tools.Count);
        created.Tools.Count.Should().BeGreaterThan(3, "the built-in tool set is what retrieval narrows");
    }

    [Fact]
    public async Task Enabled_EachRequestCarriesAtMostMaxToolsPlusPins()
    {
        var (factory, toolCounts) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(),
            toolRetrieval: new ToolRetrievalHostConfig { Enabled = true, MaxTools = 2, MinRelevanceScore = 0f });

        var created = await loops.CreateWithToolsAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await created.Loop.RunAsync("read the file notes.txt", TestContext.Current.CancellationToken);

        toolCounts.Should().ContainSingle().Which.Should().BeInRange(1, 2);
    }

    [Fact]
    public async Task StickyToolLimit_KeepsWhatTheConversationAlreadySent_FirstInTheNextRequest()
    {
        var (factory, sent) = RecordingNamesFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(),
            toolRetrieval: new ToolRetrievalHostConfig { Enabled = true, MaxTools = 2, MinRelevanceScore = 0f, StickyToolLimit = 40 });

        var created = await loops.CreateWithToolsAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await created.Loop.RunAsync("read the file notes.txt", TestContext.Current.CancellationToken);
        await created.Loop.RunAsync("run a shell command that lists processes", TestContext.Current.CancellationToken);

        sent.Should().HaveCount(2);
        sent[0].Should().NotBeEmpty();
        sent[1].Take(sent[0].Count).Should().Equal(sent[0], "the second request keeps the first one's tools as its prefix");
    }

    [Fact]
    public void Disabled_ProducesNoLibraryOptions_AndEnabledMapsEveryField()
    {
        AgentLoopFactory.ToolRetrievalOptionsFrom(null).Should().BeNull();
        AgentLoopFactory.ToolRetrievalOptionsFrom(new ToolRetrievalHostConfig { MaxTools = 3 }).Should().BeNull();

        var options = AgentLoopFactory.ToolRetrievalOptionsFrom(new ToolRetrievalHostConfig
        {
            Enabled = true,
            MaxTools = 7,
            MinRelevanceScore = 0.5f,
            MinScoredSlots = 2,
            AlwaysInclude = ["shell"],
            StickyToolLimit = 24,
        })!;

        options.MaxTools.Should().Be(7);
        options.MinRelevanceScore.Should().Be(0.5f);
        options.MinScoredSlots.Should().Be(2);
        options.AlwaysInclude.Should().Equal("shell");
        options.StickyToolLimit.Should().Be(24);

        var defaults = AgentLoopFactory.ToolRetrievalOptionsFrom(new ToolRetrievalHostConfig { Enabled = true })!;
        defaults.Should().Be(new IronHive.Agent.Context.ToolRetrievalOptions());
    }

    private static IThinkingTurnManager TurnManager()
    {
        var services = new ServiceCollection();
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();
        return services.BuildServiceProvider().GetRequiredService<IThinkingTurnManager>();
    }

    /// <summary>A chat client factory whose clients answer "ok" and record the tool names each request carried, in order.</summary>
    private static (IChatClientFactory Factory, List<List<string>> Sent) RecordingNamesFactory()
    {
        var sent = new List<List<string>>();
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var client = Substitute.For<IChatClient>();
            client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    sent.Add([.. call.Arg<ChatOptions?>()?.Tools?.Select(tool => tool.Name) ?? []]);
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
                });
            return client;
        });
        return (factory, sent);
    }

    /// <summary>A chat client factory whose clients answer "ok" and record how many tools each request carried.</summary>
    private static (IChatClientFactory Factory, List<int> ToolCounts) RecordingFactory()
    {
        var counts = new List<int>();
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var client = Substitute.For<IChatClient>();
            client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    counts.Add(call.Arg<ChatOptions?>()?.Tools?.Count ?? 0);
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
                });
            return client;
        });
        return (factory, counts);
    }
}
