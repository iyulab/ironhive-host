using IronHive.Agent.Loop;
using IronHive.Host.Session;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tests.Session;

public class SessionTurnRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ironhive-rec-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SameSessionId_AfterARestart_RestoresTheConversation()
    {
        var ct = TestContext.Current.CancellationToken;

        // First process: one turn with a tool call
        var first = new SessionManager(_dir);
        var session = await first.OpenSessionAsync("worker-7", "/proj", "m", ct);
        var recorder = new SessionTurnRecorder(first, session);
        await Drain(recorder.RecordAsync("list the files", Turn(
            Tool("call-1", "list_directory", """{"path":"."}""", "a.txt b.txt"),
            Text("There are two files.")), ct));

        // Second process: a new manager over the same directory, same ID
        var second = new SessionManager(_dir);
        var reopened = await second.OpenSessionAsync("worker-7", "/elsewhere", "m", ct);
        var history = await second.RestoreContextAsync(reopened, ct);

        Assert.Equal(session.Id, reopened.Id);
        Assert.Equal(ChatRole.User, history[0].Role);
        Assert.Contains("list the files", history[0].Text, StringComparison.Ordinal);
        Assert.Contains(history, m => m.Contents.OfType<FunctionCallContent>().Any(c => c.Name == "list_directory"));
        Assert.Contains(history, m => m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "call-1"));
        Assert.Equal(ChatRole.Assistant, history[^1].Role);
        Assert.Contains("two files", history[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurnThatFails_KeepsItsToolCalls_AndIsMarkedInterrupted()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessions = new SessionManager(_dir);
        var session = await sessions.OpenSessionAsync("crash-1", "/proj", "m", ct);
        var recorder = new SessionTurnRecorder(sessions, session);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Drain(recorder.RecordAsync("do two things", Turn(
            Tool("c1", "write_file", """{"path":"x"}""", "ok"),
            Text("Half done"),
            Throw()), ct)));

        var history = await new SessionManager(_dir).RestoreContextAsync(
            (await new SessionManager(_dir).LoadSessionAsync("crash-1", ct))!, ct);

        Assert.Contains(history, m => m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "c1"));
        Assert.Contains(SessionTurnRecorder.InterruptedMarker, history[^1].Text, StringComparison.Ordinal);
        Assert.Contains("Half done", history[^1].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData("a b")]
    [InlineData("")]
    public async Task SessionIdThatIsNotAFileNameSegment_IsRefused(string id)
    {
        var sessions = new SessionManager(_dir);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sessions.OpenSessionAsync(id, "/proj", "m", TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sessions.LoadSessionAsync(id, TestContext.Current.CancellationToken));
    }

    private static async Task Drain(IAsyncEnumerable<AgentResponseChunk> chunks)
    {
        await foreach (var _ in chunks)
        {
        }
    }

    private static AgentResponseChunk Text(string text) => new() { TextDelta = text };

    private static AgentResponseChunk Tool(string callId, string name, string args, string result) => new()
    {
        ToolResult = new ToolCallResult { CallId = callId, ToolName = name, Arguments = args, Result = result, Success = true },
    };

    // A null entry is the point where the model call fails
    private static AgentResponseChunk? Throw() => null;

    private static async IAsyncEnumerable<AgentResponseChunk> Turn(params AgentResponseChunk?[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk ?? throw new InvalidOperationException("The model call failed.");
        }
    }
}
