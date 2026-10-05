using IronHive.Agent.Loop;
using IronHive.Host.Extensions;
using IronHive.Host.Session;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using NSubstitute;
using SessionData = IronHive.Host.Session.Session;

namespace IronHive.Host.Tests.Extensions;

/// <summary>
/// Tests for AgentLoopSessionExtensions.
/// Validates session-AgentLoop integration API.
/// </summary>
public class AgentLoopSessionExtensionsTests : IDisposable
{
    private readonly MockChatClient _mockClient;
    private readonly AgentLoop _agentLoop;
    private readonly ISessionManager _mockSessionManager;

    public AgentLoopSessionExtensionsTests()
    {
        _mockClient = new MockChatClient();
        _agentLoop = new AgentLoop(_mockClient, new AgentOptions
        {
            SystemPrompt = "Test system prompt"
        });
        _mockSessionManager = Substitute.For<ISessionManager>();
    }

    public void Dispose()
    {
        _mockClient.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LoadSessionAsync_LoadsAndInitializesHistory()
    {
        // Arrange
        var sessionId = "test-session-123";
        var session = CreateTestSession(sessionId);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!")
        };

        _mockSessionManager.LoadSessionAsync(sessionId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<SessionData?>(session));
        _mockSessionManager.RestoreContextAsync(session, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ChatMessage>>(messages));

        // Act
        var result = await _agentLoop.LoadSessionAsync(_mockSessionManager, sessionId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(sessionId, result.Id);
        Assert.Equal(3, _agentLoop.History.Count); // System + 2 restored messages
        Assert.Equal("Hello", _agentLoop.History[1].Text);
        Assert.Equal("Hi there!", _agentLoop.History[2].Text);
    }

    [Fact]
    public async Task LoadSessionAsync_ThrowsSessionNotFoundException_WhenNotFound()
    {
        // Arrange
        var sessionId = "nonexistent-session";
        _mockSessionManager.LoadSessionAsync(sessionId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<SessionData?>(null));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => _agentLoop.LoadSessionAsync(_mockSessionManager, sessionId, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(sessionId, exception.SessionId);
        Assert.Contains(sessionId, exception.Message);
    }

    [Fact]
    public async Task LoadSessionAsync_ThrowsOnNullSessionId()
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _agentLoop.LoadSessionAsync(_mockSessionManager, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadSessionAsync_ThrowsOnEmptySessionId()
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _agentLoop.LoadSessionAsync(_mockSessionManager, "", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_CreatesNewSession_WhenNoLatestExists()
    {
        // Arrange
        var projectPath = "/test/project";
        var model = "test-model";
        var newSession = CreateTestSession("new-session");

        _mockSessionManager.GetLatestSessionAsync(projectPath, Arg.Any<CancellationToken>()).Returns(Task.FromResult<SessionData?>(null));
        _mockSessionManager.CreateSessionAsync(projectPath, model, Arg.Any<CancellationToken>()).Returns(Task.FromResult(newSession));

        // Act
        var result = await _agentLoop.LoadOrCreateSessionAsync(
            _mockSessionManager, projectPath, model, continueLatest: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("new-session", result.Id);
        await _mockSessionManager.Received(1).CreateSessionAsync(projectPath, model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_ContinuesLatestSession_WhenExists()
    {
        // Arrange
        var projectPath = "/test/project";
        var model = "test-model";
        var latestSession = CreateTestSession("latest-session");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Previous message"),
            new(ChatRole.Assistant, "Previous response")
        };

        _mockSessionManager.GetLatestSessionAsync(projectPath, Arg.Any<CancellationToken>()).Returns(Task.FromResult<SessionData?>(latestSession));
        _mockSessionManager.RestoreContextAsync(latestSession, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ChatMessage>>(messages));

        // Act
        var result = await _agentLoop.LoadOrCreateSessionAsync(
            _mockSessionManager, projectPath, model, continueLatest: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("latest-session", result.Id);
        Assert.Equal(3, _agentLoop.History.Count); // System + 2 restored
        await _mockSessionManager.DidNotReceive().CreateSessionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_CreatesNewSession_WhenContinueLatestIsFalse()
    {
        // Arrange
        var projectPath = "/test/project";
        var model = "test-model";
        var newSession = CreateTestSession("new-session");

        _mockSessionManager.CreateSessionAsync(projectPath, model, Arg.Any<CancellationToken>()).Returns(Task.FromResult(newSession));

        // Act
        var result = await _agentLoop.LoadOrCreateSessionAsync(
            _mockSessionManager, projectPath, model, continueLatest: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("new-session", result.Id);
        await _mockSessionManager.DidNotReceive().GetLatestSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockSessionManager.Received(1).CreateSessionAsync(projectPath, model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveTurnAsync_SavesUserMessageAndResponse()
    {
        // Arrange
        var session = CreateTestSession("test-session");
        var prompt = "Hello!";
        var response = new AgentResponse
        {
            Content = "Hi there!",
            ToolCalls = []
        };

        // Act
        await _mockSessionManager.SaveTurnAsync(session, prompt, response, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await _mockSessionManager.Received(1).SaveUserMessageAsync(session, prompt, Arg.Any<CancellationToken>());
        await _mockSessionManager.Received(1).SaveAssistantMessageAsync(session, response.Content, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveTurnAsync_SavesToolCalls()
    {
        // Arrange
        var session = CreateTestSession("test-session");
        var prompt = "Run a command";
        var response = new AgentResponse
        {
            Content = "Done!",
            ToolCalls =
            [
                new ToolCallResult
                {
                    ToolName = "shell",
                    Arguments = "{\"command\":\"ls\"}",
                    Result = "file1.txt\nfile2.txt",
                    Success = true
                }
            ]
        };

        // Act
        await _mockSessionManager.SaveTurnAsync(session, prompt, response, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await _mockSessionManager.Received(1).SaveUserMessageAsync(session, prompt, Arg.Any<CancellationToken>());
        await _mockSessionManager.Received(1).SaveToolUseAsync(
            session,
            "shell",
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockSessionManager.Received(1).SaveToolResultAsync(
            session,
            Arg.Any<string>(),
            "file1.txt\nfile2.txt",
            false, Arg.Any<CancellationToken>());
        await _mockSessionManager.Received(1).SaveAssistantMessageAsync(session, "Done!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveTurnAsync_SavesFailedToolCalls()
    {
        // Arrange
        var session = CreateTestSession("test-session");
        var prompt = "Run a command";
        var response = new AgentResponse
        {
            Content = "Command failed",
            ToolCalls =
            [
                new ToolCallResult
                {
                    ToolName = "shell",
                    Arguments = "{\"command\":\"invalid\"}",
                    Result = "Error: command not found",
                    Success = false
                }
            ]
        };

        // Act
        await _mockSessionManager.SaveTurnAsync(session, prompt, response, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await _mockSessionManager.Received(1).SaveToolResultAsync(
            session,
            Arg.Any<string>(),
            "Error: command not found",
            true, Arg.Any<CancellationToken>()); // isError = true
    }

    [Fact]
    public async Task SaveTurnAsync_UnknownToolCallOutcome_IsNotReportedAsError()
    {
        // Arrange — Success is null when no function-invocation middleware observed the outcome.
        var session = CreateTestSession("test-session");
        var prompt = "Run a command";
        var response = new AgentResponse
        {
            Content = "Done!",
            ToolCalls =
            [
                new ToolCallResult
                {
                    ToolName = "shell",
                    Arguments = "{\"command\":\"ls\"}",
                    Result = "file1.txt\nfile2.txt",
                    Success = null
                }
            ]
        };

        // Act
        await _mockSessionManager.SaveTurnAsync(session, prompt, response, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — unknown outcome is not treated as an error.
        await _mockSessionManager.Received(1).SaveToolResultAsync(
            session,
            Arg.Any<string>(),
            "file1.txt\nfile2.txt",
            false, Arg.Any<CancellationToken>()); // isError = false
    }

    private static SessionData CreateTestSession(string id)
    {
        return new SessionData
        {
            Id = id,
            ProjectPath = "/test/project",
            TranscriptPath = $"/test/sessions/{id}.jsonl",
            Model = "test-model",
            CreatedAt = DateTimeOffset.UtcNow,
            ProjectHash = "testhash123"
        };
    }
}
