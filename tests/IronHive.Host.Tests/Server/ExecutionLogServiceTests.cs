using System.Text.Json;
using AwesomeAssertions;
using IronHive.Agent.Loop;
using IronHive.Host.Server;
using Xunit;

namespace IronHive.Host.Tests.Server;

public class ExecutionLogServiceTests : IDisposable
{
    private readonly string _testDir;

    public ExecutionLogServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "execlog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string LogPath(string name = "test") => Path.Combine(_testDir, $"{name}.execlog.jsonl");

    [Fact]
    public async Task Initialize_CreatesLogFile()
    {
        var path = LogPath();
        await using var sut = new ExecutionLogService();
        sut.Initialize(path);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task BeginTurn_WritesTurnStartEntry()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        await sut.BeginTurnAsync("Hello, world!", cancellationToken: TestContext.Current.CancellationToken);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        lines.Should().HaveCount(1);

        var entry = JsonDocument.Parse(lines[0]);
        entry.RootElement.GetProperty("type").GetString().Should().Be("turn_start");
        entry.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        entry.RootElement.GetProperty("prompt").GetString().Should().Be("Hello, world!");
    }

    [Fact]
    public async Task ProcessChunk_WithToolCall_RecordsToolCall()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        await sut.BeginTurnAsync("test", cancellationToken: TestContext.Current.CancellationToken);

        // Simulate tool call streaming: name first, then arguments
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk
            {
                Id = "tc-001",
                NameDelta = "ReadFile"
            }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk
            {
                Id = "tc-001",
                ArgumentsDelta = "{\"filePath\":"
            }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk
            {
                Id = "tc-001",
                ArgumentsDelta = "\"test.txt\"}"
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        // turn_start + tool_call + turn_end = 3 entries
        lines.Should().HaveCount(3);

        var toolEntry = JsonDocument.Parse(lines[1]);
        toolEntry.RootElement.GetProperty("type").GetString().Should().Be("tool_call");
        toolEntry.RootElement.GetProperty("tool").GetString().Should().Be("ReadFile");
        toolEntry.RootElement.GetProperty("arguments").GetString().Should().Be("{\"filePath\":\"test.txt\"}");
        toolEntry.RootElement.GetProperty("step").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ProcessChunk_MultipleToolCalls_RecordsAll()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        await sut.BeginTurnAsync("analyze files", cancellationToken: TestContext.Current.CancellationToken);

        // First tool call
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "GlobFiles" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", ArgumentsDelta = "{\"pattern\":\"*.cs\"}" }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Second tool call (triggers flush of first)
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-002", NameDelta = "ReadFile" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-002", ArgumentsDelta = "{\"filePath\":\"Program.cs\"}" }
        }, cancellationToken: TestContext.Current.CancellationToken);

        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);

        sut.TotalSteps.Should().Be(2);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        // turn_start + tool_call(GlobFiles) + tool_call(ReadFile) + turn_end = 4
        lines.Should().HaveCount(4);

        var tool1 = JsonDocument.Parse(lines[1]);
        tool1.RootElement.GetProperty("tool").GetString().Should().Be("GlobFiles");
        tool1.RootElement.GetProperty("step").GetInt32().Should().Be(1);

        var tool2 = JsonDocument.Parse(lines[2]);
        tool2.RootElement.GetProperty("tool").GetString().Should().Be("ReadFile");
        tool2.RootElement.GetProperty("step").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ProcessChunk_TextOnlyChunks_NoToolCallRecorded()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        await sut.BeginTurnAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        await sut.ProcessChunkAsync(new AgentResponseChunk { TextDelta = "Response text" }, cancellationToken: TestContext.Current.CancellationToken);

        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);

        sut.TotalSteps.Should().Be(0);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        // turn_start only -- no turn_end because stepCount is 0
        lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task MultipleTurns_IncrementsCounters()
    {
        var sut = new ExecutionLogService();
        var path = LogPath();
        sut.Initialize(path);

        // Turn 1
        await sut.BeginTurnAsync("first", cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "ReadFile" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Turn 2
        await sut.BeginTurnAsync("second", cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-002", NameDelta = "WriteFile" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);

        sut.TurnCount.Should().Be(2);
        sut.TotalSteps.Should().Be(2);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task BeginTurn_TruncatesLongPrompt()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        var longPrompt = new string('x', 1000);
        await sut.BeginTurnAsync(longPrompt, cancellationToken: TestContext.Current.CancellationToken);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        var entry = JsonDocument.Parse(lines[0]);
        var prompt = entry.RootElement.GetProperty("prompt").GetString();
        prompt!.Length.Should().BeLessThanOrEqualTo(504); // 500 + "..."
    }

    [Fact]
    public async Task EndTurn_IncludesResponseLength()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        await sut.BeginTurnAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "ListDirectory" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.EndTurnAsync(responseLength: 1500, cancellationToken: TestContext.Current.CancellationToken);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        var endEntry = JsonDocument.Parse(lines[^1]);
        endEntry.RootElement.GetProperty("responseChars").GetInt32().Should().Be(1500);
    }

    [Fact]
    public async Task Initialize_CreatesDirectoryIfNotExists()
    {
        var nestedPath = Path.Combine(_testDir, "deep", "nested", "test.execlog.jsonl");
        await using var sut = new ExecutionLogService();
        sut.Initialize(nestedPath);

        Directory.Exists(Path.Combine(_testDir, "deep", "nested")).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessChunk_NullToolCallDelta_IsIgnored()
    {
        var sut = new ExecutionLogService();
        var path = LogPath();
        sut.Initialize(path);

        await sut.BeginTurnAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk { TextDelta = "hello" }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk { ThinkingDelta = "thinking..." }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);

        sut.TotalSteps.Should().Be(0);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task ToolCallEntry_IncludesTimestamp()
    {
        var path = LogPath();
        var sut = new ExecutionLogService();
        sut.Initialize(path);

        await sut.BeginTurnAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        await sut.ProcessChunkAsync(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "ReadFile" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        await sut.EndTurnAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sut.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        var toolEntry = JsonDocument.Parse(lines[1]);
        toolEntry.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }
}
