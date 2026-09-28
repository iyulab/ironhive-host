using System.Reflection;
using AwesomeAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Loop;
using IronHive.Host.Context;
using IronHive.Host.Extensions;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using HostCompactionConfig = IronHive.Agent.Context.CompactionConfig;

namespace IronHive.Host.Tests.Context;

/// <summary>
/// Regression tests for the M1-4 compaction dead-config fix: host loop-construction paths must
/// wire a <c>ContextManager</c> from <see cref="HostCompactionConfig"/> so long sessions actually
/// compact. Before the fix, host loops were built with a null ContextManager and the config was inert.
/// </summary>
public class HostCompactionWiringTests
{
    [Fact]
    public void HostContextManagerFactory_Create_ReturnsModelAwareManager()
    {
        var manager = HostContextManagerFactory.Create(new HostCompactionConfig(), "gpt-4o");

        manager.Should().NotBeNull();
        // gpt-4o resolves to its catalog context window, not the 8192 fallback.
        manager.MaxContextTokens.Should().BeGreaterThan(8192);
    }

    private static List<ChatMessage> OneTurnReading(int rounds)
    {
        var history = new List<ChatMessage> { new(ChatRole.User, "Read every section.") };
        for (var r = 1; r <= rounds; r++)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"c{r}", "read_section")]));
            history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"c{r}", new string('x', 2_000))]));
        }
        return history;
    }

    private static string ResultOf(IReadOnlyList<ChatMessage> history, string callId)
        => history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(r => r.CallId == callId).Result!.ToString()!;

    [Fact]
    public void HostContextManagerFactory_Create_AppliesObservationMaskingFromConfig()
    {
        // Before, the host built its ContextManager without a masker: EnableObservationMasking was accepted and inert.
        var manager = HostContextManagerFactory.Create(
            new HostCompactionConfig { EnableObservationMasking = true, ObservationMaskingProtectedRounds = 2, EnableToolResultCompaction = false },
            "gpt-4o");

        var reduced = manager.ReduceToolResults(OneTurnReading(4));

        ResultOf(reduced, "c1").Should().StartWith("[Masked:");
        ResultOf(reduced, "c4").Should().HaveLength(2_000);
    }

    [Fact]
    public void HostContextManagerFactory_Create_AppliesToolResultCompactionFromConfig()
    {
        var manager = HostContextManagerFactory.Create(
            new HostCompactionConfig { EnableToolResultCompaction = true, MaxToolResultChars = 500, EnableObservationMasking = false },
            "gpt-4o");

        var reduced = manager.ReduceToolResults(OneTurnReading(1));

        ResultOf(reduced, "c1").Length.Should().BeLessThan(2_000);
    }

    [Fact]
    public void HostContextManagerFactory_Create_LeavesResultsAloneWhenBothAreOff()
    {
        var manager = HostContextManagerFactory.Create(
            new HostCompactionConfig { EnableToolResultCompaction = false, EnableObservationMasking = false },
            "gpt-4o");
        var history = OneTurnReading(4);

        manager.ReduceToolResults(history).Should().BeSameAs(history);
    }

    [Fact]
    public void CliPipeline_PutsAnUnboundToolRoundContextInsideFunctionInvocation_AndTheLoopBindsIt()
    {
        // The CLI builds its chat clients before a loop's ContextManager exists; the loop binds its own manager to the
        // ToolRoundContextChatClient inside function invocation, so every tool round of a turn is reduced.
        var client = IronHive.Cli.Infrastructure.ServiceCollectionExtensions.DecorateChatClient(
            Substitute.For<IChatClient>(), new IronHive.Host.Config.ChatBehaviorConfig(),
            Substitute.For<IronHive.Agent.Mode.IModeToolFilter>(), approvalService: null, gateLogger: null);
        var roundContext = client.GetService<IronHive.Agent.Context.ToolRoundContextChatClient>();

        client.Should().BeOfType<FunctionInvokingChatClient>();
        roundContext.Should().NotBeNull();
        roundContext!.ContextManager.Should().BeNull();

        var manager = HostContextManagerFactory.Create(new HostCompactionConfig(), "gpt-4o");
        _ = new AgentLoop(client, contextManager: manager);

        roundContext.ContextManager.Should().BeSameAs(manager);
    }

    [Fact]
    public async Task HostContextManagerFactory_Create_CarriesTheGoalReminderOptions()
    {
        var manager = HostContextManagerFactory.Create(
            new HostCompactionConfig { GoalReminder = new IronHive.Agent.Context.GoalReminderOptions { Enabled = false } }, "gpt-4o");
        var history = OneTurnReading(4);
        manager.SetGoalFromHistory(history);

        var prepared = await manager.PrepareHistoryAsync(history, TestContext.Current.CancellationToken);

        prepared.Should().NotContain(m => m.Text != null && m.Text.StartsWith("[REMINDER]", StringComparison.Ordinal));
    }

    [Fact]
    public void HostContextManagerFactory_Create_NullConfig_UsesDefaults()
    {
        var manager = HostContextManagerFactory.Create(null, modelName: null);

        manager.Should().NotBeNull();
        manager.MaxContextTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void HostContextManagerFactory_Create_TriggersCompactionOnOversizedHistory()
    {
        // Small window so the threshold is easy to exceed deterministically.
        var manager = HostContextManagerFactory.Create(
            new HostCompactionConfig { ProtectRecentTokens = 1000, MinimumPruneTokens = 500 },
            "gpt-4");

        var maxTokens = manager.MaxContextTokens;
        var hugeMessage = new string('x', maxTokens * 4 * 2); // ~2x the window in chars (~4 chars/token)
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, hugeMessage)
        };

        manager.ShouldCompact(history).Should().BeTrue(
            "a history larger than the model context window must trigger compaction");
    }

    [Fact]
    public void AddIronHive_EmbedPath_WiresContextManagerIntoAgentLoop()
    {
        var services = new ServiceCollection();
        services.AddIronHive(options =>
        {
            options.UseChatClient(new MockChatClient());
            options.SystemPrompt = "test";
            options.DefaultModel = "gpt-4o";
        });

        using var provider = services.BuildServiceProvider();
        var loop = provider.GetRequiredService<IAgentLoop>();

        loop.Should().BeOfType<AgentLoop>();
        ((AgentLoop)loop).ContextManager.Should().NotBeNull(
            "the embed path must wire compaction so the config is not inert");
    }

    [Fact]
    public void AddIronHive_EmbedPath_PassesRegisteredInstructionContributorsToTheLoop()
    {
        // A constructor that accepts contributors proves nothing about the container handing them over.
        var services = new ServiceCollection();
        services.AddSingleton<IronHive.Agent.Context.ISystemInstructionContributor>(new HouseRules());
        services.AddIronHive(options =>
        {
            options.UseChatClient(new MockChatClient());
            options.SystemPrompt = "test";
            options.DefaultModel = "gpt-4o";
        });

        using var provider = services.BuildServiceProvider();
        var loop = (AgentLoop)provider.GetRequiredService<IAgentLoop>();

        loop.ContextManager!.InstructionContributors.Select(c => c.Name).Should().Equal("house-rules");
    }

    [Fact]
    public void AddIronHive_EmbedPath_WithoutContributors_HasNone()
    {
        var services = new ServiceCollection();
        services.AddIronHive(options =>
        {
            options.UseChatClient(new MockChatClient());
            options.DefaultModel = "gpt-4o";
        });

        using var provider = services.BuildServiceProvider();
        var loop = (AgentLoop)provider.GetRequiredService<IAgentLoop>();

        loop.ContextManager!.InstructionContributors.Should().BeEmpty();
    }

    private sealed class HouseRules : IronHive.Agent.Context.ISystemInstructionContributor
    {
        public string Name => "house-rules";

        public string? GetInstructions() => "Never touch the archive folder.";
    }

    [Fact]
    public async Task AgentLoopFactory_CliPath_BuildsLoopWithNonNullContextManager()
    {
        // ThinkingAgentLoop exposes no public ContextManager getter; assert the private field via
        // reflection. This is the primary (CLI/server) path where long sessions actually run.
        var clientFactory = Substitute.For<IronHive.Agent.Providers.IChatClientFactory>();
        clientFactory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IChatClient>(new MockChatClient()));
        var turnManager = Substitute.For<IThinkingTurnManager>();

        var factory = new IronHive.Cli.Infrastructure.AgentLoopFactory(
            clientFactory,
            turnManager,
            compactionConfig: new HostCompactionConfig());

        var loop = await factory.CreateAsync(new AgentLoopFactoryOptions { Model = "gpt-4o" }, TestContext.Current.CancellationToken);

        var field = typeof(ThinkingAgentLoop).GetField("_contextManager", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.GetValue(loop).Should().NotBeNull(
            "the CLI factory path must wire a ContextManager so host sessions compact");
    }
}
