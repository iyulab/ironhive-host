using System.Reflection;
using FluentAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Loop;
using IronHive.Host.Core.Context;
using IronHive.Host.Core.Extensions;
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
    public async Task AgentLoopFactory_CliPath_BuildsLoopWithNonNullContextManager()
    {
        // ThinkingAgentLoop exposes no public ContextManager getter; assert the private field via
        // reflection. This is the primary (CLI/server) path where long sessions actually run.
        var clientFactory = Substitute.For<IronHive.Agent.Providers.IChatClientFactory>();
        clientFactory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IChatClient>(new MockChatClient()));
        var turnManager = Substitute.For<IThinkingTurnManager>();

        var factory = new IronHive.Host.Infrastructure.AgentLoopFactory(
            clientFactory,
            turnManager,
            compactionConfig: new HostCompactionConfig());

        var loop = await factory.CreateAsync(new AgentLoopFactoryOptions { Model = "gpt-4o" });

        var field = typeof(ThinkingAgentLoop).GetField("_contextManager", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.GetValue(loop).Should().NotBeNull(
            "the CLI factory path must wire a ContextManager so host sessions compact");
    }
}
