using IronHive.Cli.Core.Integration;

namespace IronHive.Cli.Tests.Integration;

/// <summary>
/// Unit tests for CodeBeakerTools.
/// </summary>
public class CodeBeakerToolsTests
{
    [Fact]
    public void Constructor_WithNullProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CodeBeakerTools((ICodeExecutionProvider)null!));
    }

    [Fact]
    public async Task GetTools_ReturnsAllCodeTools()
    {
        var provider = new InMemoryCodeExecutionProvider();
        await using var tools = new CodeBeakerTools(provider);

        var result = tools.GetTools();

        Assert.Equal(3, result.Count);
        Assert.Contains(result, t => t.Name == "code_execute");
        Assert.Contains(result, t => t.Name == "code_session");
        Assert.Contains(result, t => t.Name == "code_install_packages");
    }

    [Fact]
    public async Task GetTools_HasCorrectDescriptions()
    {
        var provider = new InMemoryCodeExecutionProvider();
        await using var tools = new CodeBeakerTools(provider);

        var result = tools.GetTools();

        var executeTool = result.First(t => t.Name == "code_execute");
        Assert.Contains("Executes", executeTool.Description);
        Assert.Contains("sandboxed", executeTool.Description);

        var sessionTool = result.First(t => t.Name == "code_session");
        Assert.Contains("Sessions", sessionTool.Description);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var provider = new InMemoryCodeExecutionProvider();
        var tools = new CodeBeakerTools(provider);

        await tools.DisposeAsync();
        await tools.DisposeAsync(); // Should not throw
    }
}

/// <summary>
/// Unit tests for InMemoryCodeExecutionProvider.
/// </summary>
public class InMemoryCodeExecutionProviderTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessResult()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.ExecuteAsync(
            "console.log('Hello')",
            "javascript",
            null,
            30);

        Assert.True(result.Success);
        Assert.NotNull(result.Stdout);
    }

    [Fact]
    public async Task ExecuteAsync_JavaScript_ParsesConsoleLog()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.ExecuteAsync(
            "console.log('Hello World')",
            "javascript",
            null,
            30);

        Assert.True(result.Success);
        Assert.Equal("Hello World", result.Stdout);
    }

    [Fact]
    public async Task ExecuteAsync_Python_ParsesPrint()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.ExecuteAsync(
            "print('Hello Python')",
            "python",
            null,
            30);

        Assert.True(result.Success);
        Assert.Equal("Hello Python", result.Stdout);
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedLanguage_ReturnsMessage()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.ExecuteAsync(
            "code",
            "ruby",
            null,
            30);

        Assert.True(result.Success);
        Assert.Contains("Unsupported language", result.Stdout);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsNewSessionId()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.CreateSessionAsync("javascript");

        Assert.True(result.Success);
        Assert.NotNull(result.SessionId);
        Assert.StartsWith("session_", result.SessionId);
        Assert.Equal("javascript", result.Language);
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesMultipleSessions()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var session1 = await provider.CreateSessionAsync("javascript");
        var session2 = await provider.CreateSessionAsync("python");

        Assert.NotEqual(session1.SessionId, session2.SessionId);
    }

    [Fact]
    public async Task DestroySessionAsync_RemovesSession()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var createResult = await provider.CreateSessionAsync("javascript");
        var destroyResult = await provider.DestroySessionAsync(createResult.SessionId!);

        Assert.True(destroyResult.Success);

        // Verify session is gone
        var listResult = await provider.ListSessionsAsync();
        Assert.DoesNotContain(listResult.Sessions!, s => s.SessionId == createResult.SessionId);
    }

    [Fact]
    public async Task DestroySessionAsync_NonexistentSession_ReturnsError()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.DestroySessionAsync("nonexistent");

        Assert.False(result.Success);
        Assert.Equal("Session not found", result.Error);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsAllSessions()
    {
        var provider = new InMemoryCodeExecutionProvider();

        await provider.CreateSessionAsync("javascript");
        await provider.CreateSessionAsync("python");

        var result = await provider.ListSessionsAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.Sessions!.Count);
    }

    [Fact]
    public async Task ListSessionsAsync_EmptyByDefault()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.ListSessionsAsync();

        Assert.True(result.Success);
        Assert.Empty(result.Sessions!);
    }

    [Fact]
    public async Task InstallPackagesAsync_ReturnsInstalledPackages()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var session = await provider.CreateSessionAsync("javascript");
        var result = await provider.InstallPackagesAsync(session.SessionId!, ["lodash", "axios"]);

        Assert.True(result.Success);
        Assert.Equal(2, result.InstalledPackages!.Count);
        Assert.Contains("lodash", result.InstalledPackages);
        Assert.Contains("axios", result.InstalledPackages);
    }

    [Fact]
    public async Task InstallPackagesAsync_NonexistentSession_ReturnsError()
    {
        var provider = new InMemoryCodeExecutionProvider();

        var result = await provider.InstallPackagesAsync("nonexistent", ["lodash"]);

        Assert.False(result.Success);
        Assert.Equal("Session not found", result.Error);
    }
}

/// <summary>
/// Unit tests for ExecutionResult.
/// </summary>
public class ExecutionResultTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new ExecutionResult
        {
            Success = true,
            Stdout = "output",
            Stderr = "error",
            Result = "42",
            Error = null,
            ExecutionTimeMs = 100
        };

        Assert.True(result.Success);
        Assert.Equal("output", result.Stdout);
        Assert.Equal("error", result.Stderr);
        Assert.Equal("42", result.Result);
        Assert.Null(result.Error);
        Assert.Equal(100, result.ExecutionTimeMs);
    }
}

/// <summary>
/// Unit tests for SessionResult.
/// </summary>
public class SessionResultTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new SessionResult
        {
            Success = true,
            SessionId = "session_1",
            Language = "javascript",
            Error = null
        };

        Assert.True(result.Success);
        Assert.Equal("session_1", result.SessionId);
        Assert.Equal("javascript", result.Language);
        Assert.Null(result.Error);
    }
}

/// <summary>
/// Unit tests for SessionListResult.
/// </summary>
public class SessionListResultTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new SessionListResult
        {
            Success = true,
            Sessions =
            [
                new SessionInfo { SessionId = "s1", Language = "js" },
                new SessionInfo { SessionId = "s2", Language = "py" }
            ],
            Error = null
        };

        Assert.True(result.Success);
        Assert.Equal(2, result.Sessions!.Count);
        Assert.Null(result.Error);
    }
}

/// <summary>
/// Unit tests for PackageInstallResult.
/// </summary>
public class PackageInstallResultTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new PackageInstallResult
        {
            Success = true,
            InstalledPackages = ["lodash", "axios"],
            Error = null
        };

        Assert.True(result.Success);
        Assert.Equal(2, result.InstalledPackages!.Count);
        Assert.Null(result.Error);
    }
}
