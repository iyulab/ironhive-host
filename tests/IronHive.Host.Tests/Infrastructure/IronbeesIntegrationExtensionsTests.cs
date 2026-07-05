using FluentAssertions;
using Ironbees.Core;
using Ironbees.Core.Conversation;
using IronHive.Agent.Ironbees;
using IronHive.Agent.Loop;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// D14-C: host's Ironbees wiring now consumes <c>IronHive.Agent.Ironbees</c> canonical types
/// (dedupe of the previously-forked host copy). This verifies the host-side DI surface
/// (<see cref="IronbeesIntegrationExtensions.AddIronbeesOrchestration(IServiceCollection, Action{IronbeesOptions})"/>)
/// actually wires conversation persistence end-to-end — the capability the old host copy of
/// <c>OrchestratedAgentLoop</c> silently dropped (always returned empty history / no-op clear).
/// </summary>
public class IronbeesIntegrationExtensionsTests : IDisposable
{
    private readonly string _agentsDirectory;
    private readonly string _conversationsDirectory;

    public IronbeesIntegrationExtensionsTests()
    {
        _agentsDirectory = Path.Combine(Path.GetTempPath(), "ironhive-host-tests-agents-" + Guid.NewGuid().ToString("N"));
        _conversationsDirectory = Path.Combine(Path.GetTempPath(), "ironhive-host-tests-conversations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_agentsDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentsDirectory))
        {
            Directory.Delete(_agentsDirectory, recursive: true);
        }

        if (Directory.Exists(_conversationsDirectory))
        {
            Directory.Delete(_conversationsDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OrchestratedAgentLoop_WithConversationsDirectory_PersistsHistoryAcrossInstances()
    {
        var mockClient = new MockChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(mockClient);

        services.AddIronbeesOrchestration(options =>
        {
            options.AgentsDirectory = _agentsDirectory;
            options.ConversationsDirectory = _conversationsDirectory;
            options.ChatClientFactory = _ => mockClient;
        });

        await using var provider = services.BuildServiceProvider();
        var conversationStoreProbe = provider.GetService<IConversationStore>();
        conversationStoreProbe.Should().NotBeNull("AddIronbees registers IConversationStore when ConversationsDirectory is set");

        await conversationStoreProbe!.AppendMessageAsync("probe-id", new ConversationMessage { Role = "user", Content = "probe" });
        var probeCount = await conversationStoreProbe.GetMessageCountAsync("probe-id");
        probeCount.Should().Be(1, "the store itself must persist independent of OrchestratedAgentLoop");

        var loop = provider.GetRequiredKeyedService<IAgentLoop>("orchestrated");
        loop.Should().BeOfType<OrchestratedAgentLoop>();

        var seedMessages = new[]
        {
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "hi there")
        };

        await loop.InitializeHistoryAsync(seedMessages);

        var filesAfterInit = Directory.GetFiles(_conversationsDirectory, "*.json", SearchOption.AllDirectories);
        filesAfterInit.Should().HaveCount(2, "InitializeHistoryAsync should have written a conversation file beyond the probe's");

        var history = await loop.GetHistoryAsync();

        history.Should().HaveCount(2);
        history[0].Role.Should().Be(ChatRole.User);
        history[0].Text.Should().Be("hello");
        history[1].Role.Should().Be(ChatRole.Assistant);
        history[1].Text.Should().Be("hi there");

        // Prove the file-system backing (not just an in-memory field): read the conversation
        // straight from IConversationStore under this loop's own conversation id.
        var storedIds = await conversationStoreProbe.ListAsync();
        var loopConversationId = storedIds.Single(id => id != "probe-id");
        var reloaded = await conversationStoreProbe.LoadAsync(loopConversationId);
        reloaded.Should().NotBeNull();
        reloaded!.Messages.Should().HaveCount(2);

        await loop.ClearHistoryAsync();
        var historyAfterClear = await loop.GetHistoryAsync();
        historyAfterClear.Should().BeEmpty();
    }

    [Fact]
    public async Task OrchestratedAgentLoop_WithoutConversationsDirectory_HistoryIsAlwaysEmpty()
    {
        // Default (no ConversationsDirectory configured): matches the old host behavior
        // (no persistence) rather than throwing — a deliberate opt-in surface, not a regression.
        var mockClient = new MockChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(mockClient);

        services.AddIronbeesOrchestration(options =>
        {
            options.AgentsDirectory = _agentsDirectory;
            options.ChatClientFactory = _ => mockClient;
        });

        await using var provider = services.BuildServiceProvider();
        var loop = provider.GetRequiredKeyedService<IAgentLoop>("orchestrated");

        await loop.InitializeHistoryAsync([new ChatMessage(ChatRole.User, "hello")]);
        var history = await loop.GetHistoryAsync();

        history.Should().BeEmpty();
    }
}
