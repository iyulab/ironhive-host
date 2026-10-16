using AwesomeAssertions;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using IronHive.Host.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using HostCompactionConfig = IronHive.Agent.Context.CompactionConfig;

namespace IronHive.Host.Tests.Context;

/// <summary>
/// The context options the host wires into its loops: the history summarizer is opt-in (with one, a compaction asks it for
/// the summary of the dropped messages; without one — the default — they become an omission note and no model is called),
/// and so is the tool that reads back a masked or shortened tool result.
/// </summary>
public class LoopContextWiringTests
{
    private static List<ChatMessage> LongHistory()
    {
        var history = new List<ChatMessage>();
        for (var i = 0; i < 40; i++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"question {i} " + new string('q', 400)));
            history.Add(new ChatMessage(ChatRole.Assistant, $"answer {i} " + new string('a', 400)));
        }

        return history;
    }

    // The default protects the last 40 000 tokens — more than this whole history — so nothing would be dropped.
    private static HostCompactionConfig SmallProtection() => new() { ProtectRecentTokens = 500, MinimumPruneTokens = 100 };

    private static IChatClient SummarizerAnswering(string text)
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return client;
    }

    [Fact]
    public async Task WithASummarizer_TheCompactionAsksItForTheSummary()
    {
        var summarizer = SummarizerAnswering("SUMMARY-OF-EARLIER-TURNS");
        var manager = HostContextManagerFactory.Create(SmallProtection(), "gpt-4o", summarizer: summarizer);

        var result = await manager.CompactAsync(LongHistory(), targetTokens: 1_000, TestContext.Current.CancellationToken);

        await summarizer.Received().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        result.CompactedHistory.SelectMany(m => m.Contents).OfType<TextContent>()
            .Should().Contain(t => t.Text.Contains("SUMMARY-OF-EARLIER-TURNS"));
    }

    [Fact]
    public async Task WithoutASummarizer_TheDroppedMessagesBecomeAnOmissionNote()
    {
        var manager = HostContextManagerFactory.Create(SmallProtection(), "gpt-4o");

        var result = await manager.CompactAsync(LongHistory(), targetTokens: 1_000, TestContext.Current.CancellationToken);

        result.CompactedHistory.Count.Should().BeLessThan(80, "the compaction did drop messages");
        result.CompactedHistory.SelectMany(m => m.Contents).OfType<TextContent>()
            .Should().Contain(t => t.Text.Contains("omitted"));
    }

    [Fact]
    public async Task TheLoopFactory_CreatesTheConfiguredSummarizer_FromThePlainClients()
    {
        var summarizer = SummarizerAnswering("s");
        var plain = Substitute.For<IChatClientFactory>();
        plain.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(summarizer));

        await CreateLoopAsync(plain, new HistorySummaryConfig { Provider = "gpustack", Model = "small-model" });

        await plain.Received(1).CreateAsync("gpustack", "small-model", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLoopFactory_WithoutAModel_CreatesNoSummarizer()
    {
        var plain = Substitute.For<IChatClientFactory>();

        await CreateLoopAsync(plain, new HistorySummaryConfig { Provider = "gpustack" });

        await plain.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await plain.DidNotReceive().CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetrieveToolResults_OffersTheRetrieveTool_OnlyWhenOn(bool on)
    {
        var loop = await CreateLoopAsync(Substitute.For<IChatClientFactory>(), new HistorySummaryConfig(),
            new ChatBehaviorConfig { RetrieveToolResults = on });

        loop.Tools.Any(t => t.Name == IronHive.Agent.Context.CompactedResultTool.ToolName).Should().Be(on);
    }

    [Fact]
    public void TheSection_LoadsFromConfigYaml()
    {
        ConfigurationManager.FindUnknownKeys("historySummary:\n  provider: gpustack\n  model: small-model\n").Should().BeEmpty();
    }

    private static async Task<IAgentLoop> CreateLoopAsync(
        IChatClientFactory plain, HistorySummaryConfig historySummary, ChatBehaviorConfig? chatBehavior = null)
    {
        var session = Substitute.For<IChatClient>();
        var sessionFactory = Substitute.For<IChatClientFactory>();
        sessionFactory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));
        var services = new ServiceCollection();
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();
        var loops = new AgentLoopFactory(sessionFactory, services.BuildServiceProvider().GetRequiredService<IThinkingTurnManager>(),
            plainClients: plain, historySummary: historySummary, chatBehavior: chatBehavior);

        return await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = Path.GetTempPath() }, TestContext.Current.CancellationToken);
    }
}
