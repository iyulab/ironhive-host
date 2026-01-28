using IronHive.Cli.Core.Session;

namespace IronHive.Cli.Tests.Session;

public class SessionManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly SessionManager _sessionManager;

    public SessionManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ironhive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _sessionManager = new SessionManager(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesNewSession()
    {
        // Arrange
        var projectPath = "/test/project";
        var model = "gpt-4o";

        // Act
        var session = await _sessionManager.CreateSessionAsync(projectPath, model);

        // Assert
        Assert.NotNull(session);
        Assert.NotEmpty(session.Id);
        Assert.Equal(projectPath, session.ProjectPath);
        Assert.Equal(model, session.Model);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.True(File.Exists(session.TranscriptPath));
    }

    [Fact]
    public async Task CreateSessionAsync_WritesSessionStartEntry()
    {
        // Arrange
        var projectPath = "/test/project";
        var model = "claude-3-opus";

        // Act
        var session = await _sessionManager.CreateSessionAsync(projectPath, model);

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("session_start", content);
        Assert.Contains(session.Id, content);
        Assert.Contains(model, content);
    }

    [Fact]
    public async Task LoadSessionAsync_LoadsExistingSession()
    {
        // Arrange
        var projectPath = "/test/project";
        var model = "gpt-4o";
        var created = await _sessionManager.CreateSessionAsync(projectPath, model);

        // Act
        var loaded = await _sessionManager.LoadSessionAsync(created.Id);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal(created.ProjectPath, loaded.ProjectPath);
        Assert.Equal(created.Model, loaded.Model);
    }

    [Fact]
    public async Task LoadSessionAsync_ReturnsNullForNonexistent()
    {
        // Act
        var session = await _sessionManager.LoadSessionAsync("nonexistent-id");

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public async Task GetLatestSessionAsync_ReturnsLatestSession()
    {
        // Arrange
        var projectPath = "/test/project";
        var session1 = await _sessionManager.CreateSessionAsync(projectPath, "model1");
        await Task.Delay(10); // Ensure different timestamps
        var session2 = await _sessionManager.CreateSessionAsync(projectPath, "model2");

        // Act
        var latest = await _sessionManager.GetLatestSessionAsync(projectPath);

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(session2.Id, latest.Id);
    }

    [Fact]
    public async Task GetLatestSessionAsync_ReturnsNullForNonexistentProject()
    {
        // Act
        var latest = await _sessionManager.GetLatestSessionAsync("/nonexistent/project");

        // Assert
        Assert.Null(latest);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsSessionsNewestFirst()
    {
        // Arrange
        var projectPath = "/test/project";
        var session1 = await _sessionManager.CreateSessionAsync(projectPath, "model1");
        await Task.Delay(10);
        var session2 = await _sessionManager.CreateSessionAsync(projectPath, "model2");
        await Task.Delay(10);
        var session3 = await _sessionManager.CreateSessionAsync(projectPath, "model3");

        // Act
        var sessions = await _sessionManager.ListSessionsAsync(projectPath);

        // Assert
        Assert.Equal(3, sessions.Count);
        Assert.Equal(session3.Id, sessions[0].Id);
        Assert.Equal(session2.Id, sessions[1].Id);
        Assert.Equal(session1.Id, sessions[2].Id);
    }

    [Fact]
    public async Task ListSessionsAsync_RespectsLimit()
    {
        // Arrange
        var projectPath = "/test/project";
        for (var i = 0; i < 5; i++)
        {
            await _sessionManager.CreateSessionAsync(projectPath, $"model{i}");
            await Task.Delay(10);
        }

        // Act
        var sessions = await _sessionManager.ListSessionsAsync(projectPath, limit: 3);

        // Assert
        Assert.Equal(3, sessions.Count);
    }

    [Fact]
    public async Task SaveUserMessageAsync_AppendsToTranscript()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        var message = "Hello, world!";

        // Act
        await _sessionManager.SaveUserMessageAsync(session, message);

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("user_message", content);
        Assert.Contains(message, content);
    }

    [Fact]
    public async Task SaveAssistantMessageAsync_AppendsToTranscript()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        var message = "Hello! How can I help?";

        // Act
        await _sessionManager.SaveAssistantMessageAsync(session, message);

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("assistant_message", content);
        Assert.Contains(message, content);
    }

    [Fact]
    public async Task SaveToolUseAsync_AppendsToTranscript()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        var tool = "Read";
        var input = new { file_path = "/test/file.txt" };
        var toolUseId = "toolu_01ABC";

        // Act
        await _sessionManager.SaveToolUseAsync(session, tool, input, toolUseId);

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("tool_use", content);
        Assert.Contains(tool, content);
        Assert.Contains(toolUseId, content);
    }

    [Fact]
    public async Task SaveToolResultAsync_AppendsToTranscript()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        var toolUseId = "toolu_01ABC";
        var output = "File contents here...";

        // Act
        await _sessionManager.SaveToolResultAsync(session, toolUseId, output);

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("tool_result", content);
        Assert.Contains(toolUseId, content);
        Assert.Contains(output, content);
    }

    [Fact]
    public async Task SaveToolResultAsync_RecordsErrors()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        var toolUseId = "toolu_01ABC";
        var error = "File not found";

        // Act
        await _sessionManager.SaveToolResultAsync(session, toolUseId, error, isError: true);

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("is_error", content);
        Assert.Contains("true", content);
    }

    [Fact]
    public async Task RestoreContextAsync_RestoresUserAndAssistantMessages()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        await _sessionManager.SaveUserMessageAsync(session, "Hello");
        await _sessionManager.SaveAssistantMessageAsync(session, "Hi there!");
        await _sessionManager.SaveUserMessageAsync(session, "How are you?");
        await _sessionManager.SaveAssistantMessageAsync(session, "I'm doing well!");

        // Act
        var messages = await _sessionManager.RestoreContextAsync(session);

        // Assert
        Assert.Equal(4, messages.Count);
        Assert.Equal(Microsoft.Extensions.AI.ChatRole.User, messages[0].Role);
        Assert.Equal("Hello", messages[0].Text);
        Assert.Equal(Microsoft.Extensions.AI.ChatRole.Assistant, messages[1].Role);
        Assert.Equal("Hi there!", messages[1].Text);
    }

    [Fact]
    public async Task ForkSessionAsync_CreatesNewSessionWithHistory()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        await _sessionManager.SaveUserMessageAsync(session, "Message 1");
        await _sessionManager.SaveAssistantMessageAsync(session, "Response 1");
        await _sessionManager.SaveUserMessageAsync(session, "Message 2");

        // Act
        var forked = await _sessionManager.ForkSessionAsync(session);

        // Assert
        Assert.NotEqual(session.Id, forked.Id);
        Assert.Equal(session.ProjectPath, forked.ProjectPath);
        Assert.Equal(session.Model, forked.Model);
        Assert.True(File.Exists(forked.TranscriptPath));

        // Verify forked session has resume entry
        var content = await File.ReadAllTextAsync(forked.TranscriptPath);
        Assert.Contains("session_resume", content);
        Assert.Contains(session.Id, content); // forked_from reference
    }

    [Fact]
    public async Task EndSessionAsync_WritesEndEntry()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");

        // Act
        await _sessionManager.EndSessionAsync(session, "user_exit");

        // Assert
        var content = await File.ReadAllTextAsync(session.TranscriptPath);
        Assert.Contains("session_end", content);
        Assert.Contains("user_exit", content);
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesTranscript()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        Assert.True(File.Exists(session.TranscriptPath));

        // Act
        await _sessionManager.DeleteSessionAsync(session.Id);

        // Assert
        Assert.False(File.Exists(session.TranscriptPath));
    }

    [Fact]
    public async Task ListSessionsAsync_IncludesMessageCount()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        await _sessionManager.SaveUserMessageAsync(session, "Hello");
        await _sessionManager.SaveAssistantMessageAsync(session, "Hi!");
        await _sessionManager.SaveUserMessageAsync(session, "Bye");

        // Act
        var sessions = await _sessionManager.ListSessionsAsync("/test");

        // Assert
        Assert.Single(sessions);
        Assert.Equal(3, sessions[0].MessageCount);
    }

    [Fact]
    public async Task ListSessionsAsync_IncludesFirstUserMessage()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("/test", "model");
        await _sessionManager.SaveUserMessageAsync(session, "This is my first message");
        await _sessionManager.SaveAssistantMessageAsync(session, "Response");

        // Act
        var sessions = await _sessionManager.ListSessionsAsync("/test");

        // Assert
        Assert.Single(sessions);
        Assert.Equal("This is my first message", sessions[0].FirstUserMessage);
    }

    [Fact]
    public async Task SessionId_IsSortable()
    {
        // Arrange & Act
        var session1 = await _sessionManager.CreateSessionAsync("/test", "model");
        await Task.Delay(10);
        var session2 = await _sessionManager.CreateSessionAsync("/test", "model");

        // Assert
        Assert.True(string.Compare(session1.Id, session2.Id, StringComparison.Ordinal) < 0,
            $"Session IDs should be sortable: {session1.Id} < {session2.Id}");
    }

    [Fact]
    public async Task ProjectHash_IsDeterministic()
    {
        // Arrange
        var projectPath = "/test/project";

        // Act
        var session1 = await _sessionManager.CreateSessionAsync(projectPath, "model");
        var session2 = await _sessionManager.CreateSessionAsync(projectPath, "model");

        // Assert - both should be in the same project directory
        Assert.Equal(session1.ProjectHash, session2.ProjectHash);
        Assert.Equal(
            Path.GetDirectoryName(session1.TranscriptPath),
            Path.GetDirectoryName(session2.TranscriptPath));
    }
}
