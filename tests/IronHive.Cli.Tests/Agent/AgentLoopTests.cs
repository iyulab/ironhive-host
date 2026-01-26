using IronHive.Cli.Core.Agent;
using IronHive.Cli.Tests.Mocks;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Tests.Agent;

public class AgentLoopTests
{
    [Fact]
    public async Task RunAsync_WithSimplePrompt_ReturnsResponse()
    {
        // Arrange
        var mockClient = new MockChatClient()
            .EnqueueResponse("Hello! I'm here to help.");

        var agentLoop = new AgentLoop(mockClient);

        // Act
        var response = await agentLoop.RunAsync("Hello");

        // Assert
        Assert.Equal("Hello! I'm here to help.", response.Content);
        Assert.Empty(response.ToolCalls);
    }

    [Fact]
    public async Task RunAsync_WithSystemPrompt_IncludesSystemMessageInHistory()
    {
        // Arrange
        var mockClient = new MockChatClient()
            .EnqueueResponse("I'm a helpful assistant.");

        var options = new AgentOptions
        {
            SystemPrompt = "You are a helpful assistant."
        };

        var agentLoop = new AgentLoop(mockClient, options);

        // Act
        await agentLoop.RunAsync("Who are you?");

        // Assert
        var history = agentLoop.History;
        Assert.Equal(3, history.Count); // System + User + Assistant
        Assert.Equal(ChatRole.System, history[0].Role);
        Assert.Equal("You are a helpful assistant.", history[0].Text);
    }

    [Fact]
    public async Task RunAsync_MaintainsConversationHistory()
    {
        // Arrange
        var mockClient = new MockChatClient()
            .EnqueueResponse("Hello!")
            .EnqueueResponse("Nice to meet you too!");

        var agentLoop = new AgentLoop(mockClient);

        // Act
        await agentLoop.RunAsync("Hello");
        await agentLoop.RunAsync("Nice to meet you");

        // Assert
        var history = agentLoop.History;
        Assert.Equal(4, history.Count); // User1 + Assistant1 + User2 + Assistant2

        // Verify the mock received the full history on second call
        Assert.Equal(2, mockClient.ReceivedMessages.Count);
        Assert.Single(mockClient.ReceivedMessages[0]); // First call: just "Hello"
        Assert.Equal(3, mockClient.ReceivedMessages[1].Count); // Second call: includes previous exchange
    }

    [Fact]
    public async Task RunAsync_WithUsage_ReturnsTokenUsage()
    {
        // Arrange
        var mockClient = new MockChatClient()
            .EnqueueResponse("Test response", new UsageDetails
            {
                InputTokenCount = 10,
                OutputTokenCount = 5
            });

        var agentLoop = new AgentLoop(mockClient);

        // Act
        var response = await agentLoop.RunAsync("Test");

        // Assert
        Assert.NotNull(response.Usage);
        Assert.Equal(10, response.Usage.InputTokens);
        Assert.Equal(5, response.Usage.OutputTokens);
        Assert.Equal(15, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task ClearHistory_RemovesAllMessages()
    {
        // Arrange
        var mockClient = new MockChatClient()
            .EnqueueResponse("Response 1");

        var options = new AgentOptions
        {
            SystemPrompt = "System prompt"
        };

        var agentLoop = new AgentLoop(mockClient, options);

        // Act - Add a message then clear
        await agentLoop.RunAsync("Hello");
        agentLoop.ClearHistory();

        // Assert - Only system prompt remains
        Assert.Single(agentLoop.History);
        Assert.Equal(ChatRole.System, agentLoop.History[0].Role);
    }

    [Fact]
    public async Task RunAsync_WithEmptyPrompt_ThrowsArgumentException()
    {
        // Arrange
        var mockClient = new MockChatClient();
        var agentLoop = new AgentLoop(mockClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => agentLoop.RunAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => agentLoop.RunAsync("   "));
    }

    [Fact]
    public async Task RunAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var mockClient = new MockChatClient()
            .EnqueueResponse("Should not see this");

        var agentLoop = new AgentLoop(mockClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => agentLoop.RunAsync("Test", cts.Token));
    }
}
