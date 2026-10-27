using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using IronHive.Host.Session;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Server;

/// <summary>
/// A client that re-attaches a user lists the stored sessions and reads one in order through the protocol: read-only,
/// answered while a turn runs, and shaped by the protocol (not by the transcript file).
/// </summary>
public sealed class StoredSessionReadTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ironhive-read-{Guid.NewGuid():N}");
    private readonly SessionManager _manager;
    private readonly StoredSessionReader _reader;
    private const string Project = "/test/project";

    public StoredSessionReadTests()
    {
        Directory.CreateDirectory(_dir);
        _manager = new SessionManager(_dir);
        _reader = new StoredSessionReader(_manager, Project);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<IronHive.Host.Session.Session> StoredConversation()
    {
        var session = await _manager.CreateSessionAsync(Project, "gpt-4o", Ct);
        await _manager.SaveUserMessageAsync(session, "List the files in the project root and tell me what you find.", Ct);
        await _manager.SaveToolUseAsync(session, "ListDirectory", new { path = "." }, "call-1", Ct);
        await _manager.SaveToolResultAsync(session, "call-1", "a.txt\nb.txt", cancellationToken: Ct);
        await _manager.SaveToolResultAsync(session, "call-2", "boom", isError: true, cancellationToken: Ct);
        await _manager.SaveAssistantMessageAsync(session, "Two files: a.txt and b.txt.", Ct);
        return session;
    }

    [Fact]
    public async Task ReadSession_ReturnsTheConversationInOrder_InTheProtocolsOwnShape()
    {
        var session = await StoredConversation();

        var page = (SessionPageEvent)await _reader.ReadAsync(new ReadSessionRequest(session.Id, Id: "r1"), Ct);

        page.Id.Should().Be("r1");
        page.SessionId.Should().Be(session.Id);
        page.NextCursor.Should().BeNull();
        page.Entries.Select(e => e.Kind).Should().Equal("user", "tool_use", "tool_result", "tool_result", "assistant");
        page.Entries[0].Text.Should().StartWith("List the files");
        page.Entries[1].Tool.Should().Be("ListDirectory");
        page.Entries[1].CallId.Should().Be("call-1");
        page.Entries[1].Input!.Value.GetProperty("path").GetString().Should().Be(".");
        page.Entries[2].Text.Should().Be("a.txt\nb.txt");
        page.Entries[2].IsError.Should().BeFalse();
        page.Entries[3].IsError.Should().BeTrue();
        page.Entries[4].Text.Should().Be("Two files: a.txt and b.txt.");
    }

    [Fact]
    public async Task ReadSession_PagesByCursor_UntilTheLastEntry()
    {
        var session = await StoredConversation();

        var first = (SessionPageEvent)await _reader.ReadAsync(new ReadSessionRequest(session.Id, Limit: 2), Ct);
        var second = (SessionPageEvent)await _reader.ReadAsync(new ReadSessionRequest(session.Id, Cursor: first.NextCursor, Limit: 2), Ct);
        var third = (SessionPageEvent)await _reader.ReadAsync(new ReadSessionRequest(session.Id, Cursor: second.NextCursor, Limit: 2), Ct);

        first.Entries.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNull();
        second.Entries.Should().HaveCount(2);
        third.Entries.Should().ContainSingle().Which.Kind.Should().Be("assistant");
        third.NextCursor.Should().BeNull("the last page says there is no next one");
        first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.Kind)
            .Should().Equal("user", "tool_use", "tool_result", "tool_result", "assistant");
    }

    [Fact]
    public async Task ReadSession_OfAnUnknownId_AnswersASessionNotFoundError_NotAHang()
    {
        var answer = await _reader.ReadAsync(new ReadSessionRequest("no-such-session"), Ct);

        var error = answer.Should().BeOfType<ErrorEvent>().Subject;
        error.Code.Should().Be(ErrorCodes.SessionNotFound);
        error.Message.Should().Contain("no-such-session");
    }

    [Fact]
    public async Task ReadSession_WithACursorThisServerNeverIssued_AnswersAnError()
    {
        var session = await StoredConversation();

        var answer = await _reader.ReadAsync(new ReadSessionRequest(session.Id, Cursor: "not-a-cursor"), Ct);

        answer.Should().BeOfType<ErrorEvent>().Which.Code.Should().Be(ErrorCodes.UnreadableRequest);
    }

    [Fact]
    public async Task ListSessions_ReturnsTheStoredSessionsNewestFirst_WithAPreviewAndPaging()
    {
        var older = await StoredConversation();
        await Task.Delay(20, Ct);
        var newer = await _manager.CreateSessionAsync(Project, "gpt-4o", Ct);
        await _manager.SaveUserMessageAsync(newer, "Second   conversation\nwith a   long first line " + new string('x', 200), Ct);

        var first = (SessionListEvent)await _reader.ListAsync(new ListSessionsRequest(Id: "l1", Limit: 1), Ct);
        var second = (SessionListEvent)await _reader.ListAsync(new ListSessionsRequest(Limit: 1, Cursor: first.NextCursor), Ct);

        first.Id.Should().Be("l1");
        first.Sessions.Should().ContainSingle().Which.Id.Should().Be(newer.Id);
        // The store cuts a first message at 100 characters and marks the cut; whitespace is collapsed on top of that.
        first.Sessions[0].Preview.Should().StartWith("Second conversation with a long first line").And.EndWith("...");
        first.Sessions[0].Preview!.Length.Should().BeLessThanOrEqualTo(123);
        first.Sessions[0].Status.Should().Be("active");
        first.NextCursor.Should().NotBeNull();
        second.Sessions.Should().ContainSingle().Which.Id.Should().Be(older.Id);
        second.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task TheWireEntries_AreTheProtocolsOwn_NotTheTranscriptRecords()
    {
        var session = await StoredConversation();
        var page = await _reader.ReadAsync(new ReadSessionRequest(session.Id), Ct);

        var json = JsonSerializer.Serialize(page, JsonOpts);

        json.Should().Contain("\"type\":\"session_page\"").And.Contain("\"kind\":\"user\"").And.Contain("\"call_id\":\"call-1\"");
        json.Should().NotContain("tool_use_id", "the stored record's field names stay internal");
        json.Should().NotContain("timestamp");
    }

    // ---- through the stdio runner ----

    [Fact]
    public async Task Runner_AnswersSessionReads_WhileATurnIsRunning_AndWritesNothingToTheSession()
    {
        var session = await StoredConversation();
        var transcriptBefore = await File.ReadAllTextAsync(session.TranscriptPath, Ct);
        var turnMayEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<ServerEvent> Process(UserMessageRequest msg, [EnumeratorCancellation] CancellationToken token)
        {
            yield return new TextDeltaEvent("working");
            await turnMayEnd.Task.WaitAsync(token);
            yield return new TurnEndEvent();
        }

        var input = new LineInput();
        var output = new LineOutput();
        var runner = new AgentServerRunner(Process, Substitute.For<ILogger<AgentServerRunner>>(), JsonOpts) { SessionReader = _reader };
        var run = runner.RunAsync(input, output, Ct);

        input.Enqueue("""{"type":"user_message","content":"go"}""");
        (await Next(output)).Should().BeOfType<TextDeltaEvent>();

        input.Enqueue($$"""{"type":"read_session","session_id":"{{session.Id}}","id":"r9","limit":2}""");
        input.Enqueue("""{"type":"list_sessions","id":"l9"}""");
        var read = (SessionPageEvent)await Next(output);
        var list = (SessionListEvent)await Next(output);

        read.Id.Should().Be("r9");
        read.Entries.Should().HaveCount(2);
        list.Id.Should().Be("l9");
        list.Sessions.Should().Contain(s => s.Id == session.Id);
        turnMayEnd.Task.IsCompleted.Should().BeFalse("the turn was still running when the reads were answered");

        turnMayEnd.SetResult();
        (await Next(output)).Should().BeOfType<TurnEndEvent>();
        input.Complete();
        await run;

        (await File.ReadAllTextAsync(session.TranscriptPath, Ct)).Should().Be(transcriptBefore, "reading never writes");
    }

    [Fact]
    public async Task Runner_WithoutASessionStore_AnswersNotSupported_NotSilence()
    {
        var input = new LineInput();
        var output = new LineOutput();
        var runner = new AgentServerRunner(
            (msg, token) => AsyncEnumerable.Empty<ServerEvent>(), Substitute.For<ILogger<AgentServerRunner>>(), JsonOpts);
        var run = runner.RunAsync(input, output, Ct);

        input.Enqueue("""{"type":"list_sessions"}""");
        var answer = await Next(output);
        input.Complete();
        await run;

        answer.Should().BeOfType<ErrorEvent>().Which.Code.Should().Be(ErrorCodes.NotSupported);
    }

    private static async Task<ServerEvent> Next(LineOutput output) =>
        JsonSerializer.Deserialize<ServerEvent>(await output.Lines.ReadAsync(Ct), JsonOpts)!;

    private sealed class LineInput : TextReader
    {
        private readonly Channel<string?> _channel = Channel.CreateUnbounded<string?>();

        public void Enqueue(string line) => _channel.Writer.TryWrite(line);

        public void Complete() => _channel.Writer.TryComplete();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                return null;
            }

            _channel.Reader.TryRead(out var line);
            return line;
        }
    }

    private sealed class LineOutput : TextWriter
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

        public ChannelReader<string> Lines => _lines.Reader;

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            _lines.Writer.TryWrite(value ?? string.Empty);
            return Task.CompletedTask;
        }
    }
}
