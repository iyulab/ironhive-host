using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using IronHive.Host.Session;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Session;

/// <summary>
/// An approval wait need not hold the session: past <see cref="HitlBridge.DeferAfter"/> the turn goes on without the call,
/// the next message is answered, and the request's answer (or its expiry) opens a turn that runs the call once and tells the
/// model. A server that stops while a turn waits keeps the wait (suspended) when the session is recorded, and denies it at
/// once when it is not. Every model request the facts observe has a result for every call in its history.
/// </summary>
public sealed class DeferredApprovalTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ironhive-defer-{Guid.NewGuid():N}");
    private readonly List<string> _written = [];

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    /// <summary>
    /// «note: X» asks for write_note once; any other message gets text naming what it answered. A provider refuses a history
    /// with a call that has no result - this model records any such request.
    /// </summary>
    private sealed class Model : IChatClient
    {
        private int _calls;

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public List<string> DanglingCalls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            Requests.Add(history);
            var results = history.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => r.CallId).ToHashSet();
            DanglingCalls.AddRange(history.SelectMany(m => m.Contents.OfType<FunctionCallContent>()).Select(c => c.CallId).Where(id => !results.Contains(id)));

            var lastUser = history.Last(m => m.Role == ChatRole.User).Text;
            var afterUser = history.Skip(history.FindLastIndex(m => m.Role == ChatRole.User) + 1).ToList();
            if (lastUser.StartsWith("note:", StringComparison.Ordinal) && !afterUser.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
            {
                var call = new FunctionCallContent($"call-{++_calls}", "write_note", new Dictionary<string, object?> { ["text"] = lastUser["note:".Length..].Trim() });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls });
            }

            var answer = lastUser.StartsWith("[Approval outcome]", StringComparison.Ordinal) ? "Noted the outcome."
                : afterUser.Any(m => m.Contents.OfType<FunctionResultContent>().Any()) ? "Asked for approval."
                : "Hello back.";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { FinishReason = ChatFinishReason.Stop });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class Process
    {
        public required IAgentLoop Loop { get; init; }
        public required SessionTurnRecorder? Recorder { get; init; }
        public required HitlBridge Bridge { get; init; }
        public required IReadOnlyList<AITool> Tools { get; init; }
        public required IronHive.Agent.Invocation.ToolInvocationPipeline Pipeline { get; init; }
        public required IReadOnlyList<ApprovalWaitEntry> Pending { get; init; }
        public required Model Model { get; init; }
    }

    private async Task<Process> StartAsync(TimeSpan? deferAfter, TimeSpan timeout, bool recorded, CancellationToken ct)
    {
        var bridge = new HitlBridge(timeout, deferAfter: deferAfter);
        var policy = Substitute.For<IToolCallPolicy>();
        policy.Evaluate(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(RiskAssessment.Risky(RiskLevel.Medium, "writes a note", "Allow writing the note?"));
        var pipeline = ServiceCollectionExtensions.CreateToolInvocationPipeline(policy, bridge, loggerFactory: null);
        var model = new Model();
        var client = ServiceCollectionExtensions.DecorateChatClient(model, new ChatBehaviorConfig(), pipeline);
        var tools = new List<AITool> { AIFunctionFactory.Create((string text) => { lock (_written) { _written.Add(text); } return "saved " + text; }, "write_note") };
        IAgentLoop loop = new AgentLoop(client, new AgentOptions { Tools = tools });

        SessionTurnRecorder? recorder = null;
        IReadOnlyList<ApprovalWaitEntry> pending = [];
        if (recorded)
        {
            var sessions = new SessionManager(_dir);
            var session = await sessions.OpenSessionAsync("worker-1", "/proj", "m", ct);
            var history = await sessions.RestoreContextAsync(session, ct);
            if (history.Count > 0)
            {
                await loop.InitializeHistoryAsync(history, ct);
            }

            recorder = new SessionTurnRecorder(sessions, session);
            bridge.WaitLog = recorder;
            pending = await sessions.GetPendingApprovalsAsync(session, ct);
        }

        return new Process { Loop = loop, Recorder = recorder, Bridge = bridge, Tools = tools, Pipeline = pipeline, Model = model, Pending = pending };
    }

    // The CLI's own wiring (RunCommand.RunServerModeAsync), over test I/O
    private static AgentServerRunner Runner(Process p)
    {
        async IAsyncEnumerable<ServerEvent> ProcessMessage(UserMessageRequest msg, [EnumeratorCancellation] CancellationToken token)
        {
            var turn = p.Loop.RunStreamingAsync(msg.Content, token);
            if (p.Recorder is not null)
            {
                turn = p.Recorder.RecordAsync(msg.Content, turn, token);
            }

            await foreach (var evt in turn.ToServerEvents(ct: token))
            {
                yield return evt;
            }
        }

        var runner = new AgentServerRunner(ProcessMessage, Substitute.For<ILogger<AgentServerRunner>>(), JsonOpts, hitlBridge: p.Bridge)
        {
            DeferredAnswerTurn = (answer, token) => DeferredApprovalTurn
                .RunAsync(p.Loop, answer, p.Bridge, p.Recorder, p.Tools, p.Pipeline, token)
                .ToServerEvents(ct: token),
        };

        foreach (var deferred in p.Pending.Where(w => w.Deferred))
        {
            p.Bridge.RestoreDeferred(deferred);
        }

        var inTurn = p.Pending.Where(w => !w.Deferred).ToList();
        if (inTurn.Count > 0)
        {
            runner.ResumeTurn = token => SuspendedTurnResumer
                .ResumeAsync(p.Loop, inTurn, p.Bridge, p.Recorder!, p.Tools, p.Pipeline, token)
                .ToServerEvents(ct: token);
        }

        return runner;
    }

    private static async Task<T> NextAsync<T>(LineOutput output, List<ServerEvent> seen, CancellationToken ct) where T : ServerEvent
    {
        while (true)
        {
            var evt = JsonSerializer.Deserialize<ServerEvent>(await output.Lines.ReadAsync(ct), JsonOpts)!;
            seen.Add(evt);
            if (evt is T match)
            {
                return match;
            }
        }
    }

    private static string Send(ServerRequest request) => JsonSerializer.Serialize(request, JsonOpts);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADeferredWait_LetsTheNextMessageRun_AndItsAnswerRunsTheCallOnce_InALaterTurn(bool approve)
    {
        var ct = TestContext.Current.CancellationToken;
        var p = await StartAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(5), recorded: true, ct);
        var input = new LineInput();
        var output = new LineOutput();
        var run = Runner(p).RunAsync(input, output, ct);
        var seen = new List<ServerEvent>();

        input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
        var asked = await NextAsync<HitlRequestEvent>(output, seen, ct);
        var waiting = await NextAsync<ToolEndEvent>(output, seen, ct);
        waiting.Refusal.Should().Be(ToolRefusalCodes.AwaitingApproval);
        waiting.CallId.Should().Be("call-1");
        (await NextAsync<TurnEndEvent>(output, seen, ct)).StopReason.Should().Be(StopReasons.Completed, "the turn ends without the call");
        _written.Should().BeEmpty("nothing runs before the answer");

        // The session is not held: the next message is answered while the request is open
        input.Enqueue("""{"type":"user_message","content":"hello"}""");
        await NextAsync<TurnEndEvent>(output, seen, ct);
        seen.OfType<TextDeltaEvent>().Select(t => t.Content).Should().Contain(d => d.Contains("Hello back.", StringComparison.Ordinal));

        input.Enqueue(Send(new HitlResponseRequest(approve, approve ? null : "not now", asked.Id)));
        var outcome = await NextAsync<ToolEndEvent>(output, seen, ct);
        outcome.CallId.Should().Be("call-1", "the outcome is a second tool_end for the same call");
        outcome.Success.Should().Be(approve);
        await NextAsync<TurnEndEvent>(output, seen, ct);
        input.Complete();
        await run;

        _written.Should().Equal(approve ? ["buy milk"] : []);
        seen.OfType<HitlRequestEvent>().Should().ContainSingle("the gate heard the answer without asking again");
        var note = p.Model.Requests[^1].Last(m => m.Role == ChatRole.User).Text;
        note.Should().StartWith("[Approval outcome]").And.Contain("call-1").And.Contain(approve ? "saved buy milk" : "not now");
        p.Model.DanglingCalls.Should().BeEmpty("every model request had a result for every call");

        var restarted = await StartAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(5), recorded: true, ct);
        restarted.Pending.Should().BeEmpty("the answer settled the request");
    }

    [Fact]
    public async Task ADeferredWait_Expires_FromItsFirstSend_AsOneRejectionTurn()
    {
        var ct = TestContext.Current.CancellationToken;
        var p = await StartAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), recorded: false, ct);
        var input = new LineInput();
        var output = new LineOutput();
        var run = Runner(p).RunAsync(input, output, ct);
        var seen = new List<ServerEvent>();

        input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
        await NextAsync<TurnEndEvent>(output, seen, ct);
        var outcome = await NextAsync<ToolEndEvent>(output, seen, ct);
        outcome.Success.Should().BeFalse();
        outcome.Output.Should().Contain("no answer to the approval request within 1s");
        await NextAsync<TurnEndEvent>(output, seen, ct);

        // A late answer finds nothing to settle and opens no turn
        input.Enqueue(Send(new HitlResponseRequest(true, null, seen.OfType<HitlRequestEvent>().Single().Id)));
        input.Complete();
        await run;

        _written.Should().BeEmpty();
        seen.OfType<ToolEndEvent>().Count(e => e.CallId == "call-1").Should().Be(2, "one awaiting, one outcome - the expiry is told once");
        p.Model.Requests[^1].Last(m => m.Role == ChatRole.User).Text.Should().Contain("No answer came in time");
        p.Model.DanglingCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ADeferredWait_OutlivesTheProcess_IsSentAgain_AndHoldsNoTurnAfterTheRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        string requestId;
        {
            var first = await StartAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(5), recorded: true, ct);
            var input = new LineInput();
            var output = new LineOutput();
            var run = Runner(first).RunAsync(input, output, ct);
            input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
            requestId = (await NextAsync<HitlRequestEvent>(output, [], ct)).Id;
            await NextAsync<TurnEndEvent>(output, [], ct);
            input.Complete();
            await run;
        }

        var second = await StartAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(5), recorded: true, ct);
        second.Pending.Should().ContainSingle().Which.Deferred.Should().BeTrue("the call already has its awaiting result");
        {
            var input = new LineInput();
            var output = new LineOutput();
            var run = Runner(second).RunAsync(input, output, ct);
            var seen = new List<ServerEvent>();

            (await NextAsync<HitlRequestEvent>(output, seen, ct)).Id.Should().Be(requestId, "sent again with its id");
            input.Enqueue("""{"type":"user_message","content":"hello"}""");
            await NextAsync<TurnEndEvent>(output, seen, ct);
            input.Enqueue(Send(new HitlResponseRequest(true, null, requestId)));
            (await NextAsync<ToolEndEvent>(output, seen, ct)).Success.Should().BeTrue();
            await NextAsync<TurnEndEvent>(output, seen, ct);
            input.Complete();
            await run;
        }

        _written.Should().Equal("buy milk");
        second.Model.DanglingCalls.Should().BeEmpty("the restored history carries the awaiting result");
    }

    [Fact]
    public async Task AServerStoppedWhileATurnWaits_SuspendsIt_DropsTheMessagesBehindIt_AndTheNextProcessFinishesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        {
            var first = await StartAsync(deferAfter: null, TimeSpan.FromMinutes(5), recorded: true, ct);
            var input = new LineInput();
            var output = new LineOutput();
            var run = Runner(first).RunAsync(input, output, ct);
            var seen = new List<ServerEvent>();
            input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
            await NextAsync<HitlRequestEvent>(output, seen, ct);
            input.Enqueue("""{"type":"user_message","content":"hello"}""");
            input.Complete(); // EOF = graceful shutdown

            (await NextAsync<TurnEndEvent>(output, seen, ct)).StopReason.Should().Be(StopReasons.Suspended);
            (await NextAsync<ErrorEvent>(output, seen, ct)).Code.Should().Be(ErrorCodes.NotProcessed, "the queued message did not run");
            await run.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }

        var second = await StartAsync(deferAfter: null, TimeSpan.FromMinutes(5), recorded: true, ct);
        second.Pending.Should().ContainSingle().Which.Deferred.Should().BeFalse("the turn was suspended inside the wait");
        {
            var input = new LineInput();
            var output = new LineOutput();
            var run = Runner(second).RunAsync(input, output, ct);
            var asked = await NextAsync<HitlRequestEvent>(output, [], ct);
            input.Enqueue(Send(new HitlResponseRequest(true, null, asked.Id)));
            await NextAsync<TurnEndEvent>(output, [], ct);
            input.Complete();
            await run;
        }

        _written.Should().Equal("buy milk");
        second.Model.DanglingCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AServerStoppedWhileATurnWaits_WithoutASession_DeniesTheWaitAtOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var p = await StartAsync(deferAfter: null, TimeSpan.FromMinutes(5), recorded: false, ct);
        var input = new LineInput();
        var output = new LineOutput();
        var run = Runner(p).RunAsync(input, output, ct);
        var seen = new List<ServerEvent>();
        input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
        await NextAsync<HitlRequestEvent>(output, seen, ct);
        input.Complete();

        await run.WaitAsync(TimeSpan.FromSeconds(10), ct); // not the five-minute approval timeout
        (await NextAsync<ToolEndEvent>(output, seen, ct)).Output.Should().Contain("server stopped");
        _written.Should().BeEmpty();
    }

    [Fact]
    public async Task AWaitThatBeginsAfterTheInputEnded_IsDeniedAtOnce_WithoutASession()
    {
        var ct = TestContext.Current.CancellationToken;
        var p = await StartAsync(deferAfter: null, TimeSpan.FromMinutes(5), recorded: false, ct);
        var input = new LineInput();
        var output = new LineOutput();
        input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
        input.Complete(); // the end of the input is read before the turn asks

        await Runner(p).RunAsync(input, output, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        _written.Should().BeEmpty();
        var seen = new List<ServerEvent>();
        (await NextAsync<ToolEndEvent>(output, seen, ct)).Output.Should().Contain("server stopped");
    }

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
