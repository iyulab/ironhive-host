using IronHive.Agent.Loop;
using IronHive.Host.Extensions;
using IronHive.Host.Session;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Host.Tests.Integration;

/// <summary>
/// Simulates the console-chat sample conversation flow.
/// Tests the full integration of Core library APIs.
/// </summary>
public class ConsoleChatSimulationTests : IDisposable
{
    private readonly string _testDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly MockChatClient _mockClient;

    public ConsoleChatSimulationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ironhive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _mockClient = new MockChatClient();
        var services = new ServiceCollection();
        services.AddIronHive(_mockClient, "You are a helpful assistant.");

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _mockClient.Dispose();

        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SimulateBasicConversation()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();

        _mockClient.EnqueueResponse("Hello! How can I assist you today?");
        _mockClient.EnqueueResponse("I'm doing well, thanks for asking!");
        _mockClient.EnqueueResponse("Goodbye! Have a great day!");

        // Act - Simulate 3-turn conversation
        var response1 = await agentLoop.RunAsync("Hello");
        var response2 = await agentLoop.RunAsync("How are you?");
        var response3 = await agentLoop.RunAsync("Bye");

        // Assert
        Assert.Equal("Hello! How can I assist you today?", response1.Content);
        Assert.Equal("I'm doing well, thanks for asking!", response2.Content);
        Assert.Equal("Goodbye! Have a great day!", response3.Content);

        // History should contain: System + 3x(User + Assistant) = 7 messages
        Assert.Equal(7, agentLoop.History.Count);
    }

    [Fact]
    public async Task SimulateStreamingConversation()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();
        _mockClient.EnqueueResponse("This is a streaming response that comes in chunks.");

        // Act
        var chunks = new List<string>();
        await foreach (var chunk in agentLoop.RunStreamingAsync("Tell me something"))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                chunks.Add(chunk.TextDelta);
            }
        }

        // Assert
        Assert.NotEmpty(chunks);
        var fullResponse = string.Join("", chunks);
        Assert.Contains("streaming", fullResponse);
    }

    [Fact]
    public async Task SimulateSessionContinuation()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();
        var sessionManager = _serviceProvider.GetRequiredService<ISessionManager>();

        // Act - Create session and load it
        var session = await agentLoop.LoadOrCreateSessionAsync(
            sessionManager, _testDir, "test-model", continueLatest: false);

        Assert.NotNull(session);
        Assert.Equal(_testDir, session.ProjectPath);

        // Simulate conversation
        _mockClient.EnqueueResponse("Hello from session!");
        var response = await agentLoop.RunAsync("Hi");

        // Assert
        Assert.Equal("Hello from session!", response.Content);
    }

    [Fact]
    public async Task SimulateNewSessionCommand()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();
        var sessionManager = _serviceProvider.GetRequiredService<ISessionManager>();

        // Create first session
        var session1 = await sessionManager.CreateSessionAsync(_testDir, "model1");

        // Simulate /new command - clear history and create new session
        agentLoop.ClearHistory();
        var session2 = await sessionManager.CreateSessionAsync(_testDir, "model2");

        // Assert - Different session IDs
        Assert.NotEqual(session1.Id, session2.Id);
        Assert.Single(agentLoop.History); // Only system prompt
    }

    [Fact]
    public async Task SimulateSessionsListCommand()
    {
        // Arrange
        var sessionManager = _serviceProvider.GetRequiredService<ISessionManager>();

        // Create multiple sessions
        await sessionManager.CreateSessionAsync(_testDir, "model1");
        await Task.Delay(10); // Ensure different timestamps
        await sessionManager.CreateSessionAsync(_testDir, "model2");
        await Task.Delay(10);
        await sessionManager.CreateSessionAsync(_testDir, "model3");

        // Act - List sessions
        var sessions = await sessionManager.ListSessionsAsync(_testDir, limit: 10);

        // Assert
        Assert.Equal(3, sessions.Count);
        // Sessions should be ordered by creation time (newest first)
        Assert.True(sessions[0].CreatedAt >= sessions[1].CreatedAt);
        Assert.True(sessions[1].CreatedAt >= sessions[2].CreatedAt);
    }

    [Fact]
    public async Task SimulateSessionContinueCommand()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();
        var sessionManager = _serviceProvider.GetRequiredService<ISessionManager>();

        // Create a session with some history
        var originalSession = await sessionManager.CreateSessionAsync(_testDir, "model1");
        await sessionManager.SaveUserMessageAsync(originalSession, "Original message");
        await sessionManager.SaveAssistantMessageAsync(originalSession, "Original response");

        // Clear agent history (simulate starting fresh)
        agentLoop.ClearHistory();
        Assert.Single(agentLoop.History); // Only system prompt

        // Act - Simulate /continue command
        var loadedSession = await agentLoop.LoadSessionAsync(sessionManager, originalSession.Id);

        // Assert
        Assert.Equal(originalSession.Id, loadedSession.Id);
        Assert.Equal(3, agentLoop.History.Count); // System + User + Assistant
    }

    [Fact]
    public async Task SimulateContinueNonExistentSession_ThrowsException()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();
        var sessionManager = _serviceProvider.GetRequiredService<ISessionManager>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => agentLoop.LoadSessionAsync(sessionManager, "nonexistent-session-id"));

        Assert.Equal("nonexistent-session-id", exception.SessionId);
    }

    [Fact]
    public async Task SimulateConversationWithErrors()
    {
        // Arrange
        var agentLoop = _serviceProvider.GetRequiredService<IAgentLoop>();
        _mockClient.EnqueueError(new InvalidOperationException("API Error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agentLoop.RunAsync("Trigger error"));
    }

    [Fact]
    public async Task SimulateProviderHelperIntegration()
    {
        // This test verifies that provider helpers properly configure services
        // Note: We can't actually call OpenAI/Ollama in unit tests, but we can
        // verify the registration pattern

        var services = new ServiceCollection();

        // AddIronHiveWithOllama should not throw during registration
        // (even if Ollama isn't running, registration should succeed)
        services.AddIronHiveWithOllama("llama3.2", "http://localhost:11434/v1");

        using var provider = services.BuildServiceProvider();
        var agentLoop = provider.GetService<IAgentLoop>();
        var sessionManager = provider.GetService<ISessionManager>();

        Assert.NotNull(agentLoop);
        Assert.NotNull(sessionManager);
    }
}
