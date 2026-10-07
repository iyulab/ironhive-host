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
/// A turn waiting for a human approval outlives the process: the wait is on record before the request is sent, a stopped
/// process leaves it unsettled (the turn is suspended, not interrupted), and the next process with the same session offers
/// the same request again, runs the call through the real tool pipeline once the client answers, and finishes the turn.
/// </summary>
public sealed class SuspendedApprovalTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ironhive-suspend-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    /// <summary>Calls write_note until a result for it is in the history, then answers in text.</summary>
    private sealed class NoteTakingModel : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            Requests.Add(history);
            if (history.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "The note is saved.")) { FinishReason = ChatFinishReason.Stop });
            }

            var call = new FunctionCallContent("call-1", "write_note", new Dictionary<string, object?> { ["text"] = "buy milk" });
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls });
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

    /// <summary>One server process: a loop over the CLI's own tool pipeline, gated by the bridge, recorded to the session.</summary>
    private sealed class Process
    {
        public required IAgentLoop Loop { get; init; }
        public required SessionTurnRecorder Recorder { get; init; }
        public required HitlBridge Bridge { get; init; }
        public required IReadOnlyList<AITool> Tools { get; init; }
        public required IronHive.Agent.Invocation.ToolInvocationPipeline Pipeline { get; init; }
        public required IReadOnlyList<ApprovalWaitEntry> Pending { get; init; }
        public required NoteTakingModel Model { get; init; }
    }

    private readonly List<string> _written = [];

    private async Task<Process> StartAsync(CancellationToken ct)
    {
        var sessions = new SessionManager(_dir);
        var session = await sessions.OpenSessionAsync("worker-1", "/proj", "m", ct);
        var bridge = new HitlBridge(TimeSpan.FromMinutes(5));
        var policy = Substitute.For<IToolCallPolicy>();
        policy.Evaluate(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(RiskAssessment.Risky(RiskLevel.Medium, "writes a note", "Allow writing the note?"));
        var pipeline = ServiceCollectionExtensions.CreateToolInvocationPipeline(policy, bridge, loggerFactory: null);
        var model = new NoteTakingModel();
        var client = ServiceCollectionExtensions.DecorateChatClient(model, new ChatBehaviorConfig(), pipeline);
        var tools = new List<AITool> { AIFunctionFactory.Create((string text) => { _written.Add(text); return "saved " + text; }, "write_note") };
        IAgentLoop loop = new AgentLoop(client, new AgentOptions { Tools = tools });

        var history = await sessions.RestoreContextAsync(session, ct);
        if (history.Count > 0)
        {
            await loop.InitializeHistoryAsync(history, ct);
        }

        var recorder = new SessionTurnRecorder(sessions, session);
        bridge.WaitLog = recorder;
        return new Process
        {
            Loop = loop, Recorder = recorder, Bridge = bridge, Tools = tools, Pipeline = pipeline, Model = model,
            Pending = await sessions.GetPendingApprovalsAsync(session, ct),
        };
    }

    private static AgentServerRunner Runner(Process p)
    {
        async IAsyncEnumerable<ServerEvent> ProcessMessage(UserMessageRequest msg, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var evt in p.Recorder.RecordAsync(msg.Content, p.Loop.RunStreamingAsync(msg.Content, token), token).ToServerEvents(ct: token))
            {
                yield return evt;
            }
        }

        var runner = new AgentServerRunner(ProcessMessage, Substitute.For<ILogger<AgentServerRunner>>(), JsonOpts, hitlBridge: p.Bridge);
        if (p.Pending.Count > 0)
        {
            runner.ResumeTurn = token => SuspendedTurnResumer
                .ResumeAsync(p.Loop, p.Pending, p.Bridge, p.Recorder, p.Tools, p.Pipeline, token)
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWaitOutlivesTheProcess_AndTheNextProcessFinishesTheTurnOnTheAnswer(bool approve)
    {
        var ct = TestContext.Current.CancellationToken;

        // First process: the call asks, and the process stops before anyone answers
        var first = await StartAsync(ct);
        first.Pending.Should().BeEmpty();
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var input = new LineInput();
            var output = new LineOutput();
            var run = Runner(first).RunAsync(input, output, stop.Token);
            input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");

            var asked = await NextAsync<HitlRequestEvent>(output, [], ct);
            asked.CallId.Should().Be("call-1");
            await stop.CancelAsync();
            await run.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }

        _written.Should().BeEmpty("nothing ran without an answer");

        // Second process, same session: the wait is offered again with its id, and the answer finishes the turn
        var second = await StartAsync(ct);
        var wait = second.Pending.Should().ContainSingle().Subject;
        wait.ToolUseId.Should().Be("call-1");
        var history = await second.Loop.GetHistoryAsync(ct);
        history[^1].Contents.OfType<FunctionCallContent>().Should().ContainSingle(c => c.CallId == "call-1",
            "the suspended turn's call is restored as the last message, with no interrupted marker after it");

        {
            var input = new LineInput();
            var output = new LineOutput();
            var run = Runner(second).RunAsync(input, output, ct);
            var seen = new List<ServerEvent>();

            var reoffered = await NextAsync<HitlRequestEvent>(output, seen, ct);
            reoffered.Id.Should().Be(wait.RequestId, "the client answers the request it was shown before the restart");
            reoffered.ToolName.Should().Be("write_note");
            input.Enqueue(JsonSerializer.Serialize<ServerRequest>(new HitlResponseRequest(approve, approve ? null : "not now", reoffered.Id), JsonOpts));

            await NextAsync<TurnEndEvent>(output, seen, ct);
            input.Complete();
            await run;

            seen.OfType<HitlRequestEvent>().Should().ContainSingle("the gate heard the answer without asking again");
        }

        _written.Should().Equal(approve ? ["buy milk"] : []);
        var lastRequest = second.Model.Requests[^1];
        var result = lastRequest.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Should().ContainSingle(r => r.CallId == "call-1").Subject;
        (result.Result?.ToString() ?? string.Empty).Should().Contain(approve ? "saved buy milk" : "not now");

        // Third start: nothing is pending, and the transcript holds the whole turn
        var third = await StartAsync(ct);
        third.Pending.Should().BeEmpty();
        var restored = await third.Loop.GetHistoryAsync(ct);
        restored.Should().Contain(m => m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "call-1"));
        restored[^1].Text.Should().Contain("The note is saved.");
        restored.Should().NotContain(m => m.Text.Contains(SessionTurnRecorder.InterruptedMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnsweredWait_IsSettled_AndNothingIsOfferedAfterARestart()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await StartAsync(ct);
        var input = new LineInput();
        var output = new LineOutput();
        var run = Runner(first).RunAsync(input, output, ct);
        input.Enqueue("""{"type":"user_message","content":"note: buy milk"}""");
        var asked = await NextAsync<HitlRequestEvent>(output, [], ct);
        input.Enqueue(JsonSerializer.Serialize<ServerRequest>(new HitlResponseRequest(true, null, asked.Id), JsonOpts));
        await NextAsync<TurnEndEvent>(output, [], ct);
        input.Complete();
        await run;

        var second = await StartAsync(ct);
        second.Pending.Should().BeEmpty();
        var history = await second.Loop.GetHistoryAsync(ct);
        history.Count(m => m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == "call-1"))
            .Should().Be(1, "the call announced with its request is not written again with its result");
        _written.Should().Equal("buy milk");
    }

    [Fact]
    public async Task AWaitPastTheTimeout_ResumesAsARejection_WithoutAsking()
    {
        var ct = TestContext.Current.CancellationToken;
        var bridge = new HitlBridge(TimeSpan.FromSeconds(30));
        var sent = Channel.CreateUnbounded<ServerEvent>();
        using var attachment = bridge.Attach((evt, _) => { sent.Writer.TryWrite(evt); return Task.CompletedTask; });
        var old = new ApprovalWaitEntry
        {
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2), RequestId = "r-1", ToolUseId = "call-1", Tool = "write_note",
        };

        var answer = await bridge.ReofferAsync(old, ct);

        answer.Approved.Should().BeFalse();
        answer.Reason.Should().Contain("no answer to the approval request within 30s");
        sent.Reader.TryRead(out _).Should().BeFalse("an expired wait is not shown again");
    }

    [Fact]
    public async Task APreansweredCall_IsAnsweredWithoutSending_AndAnUnusedPreanswerIsWithdrawn()
    {
        var ct = TestContext.Current.CancellationToken;
        var bridge = new HitlBridge(TimeSpan.FromSeconds(30));
        var sent = Channel.CreateUnbounded<ServerEvent>();
        using var attachment = bridge.Attach((evt, _) => { sent.Writer.TryWrite(evt); return Task.CompletedTask; });
        var request = new ApprovalRequest
        {
            ToolName = "write_note", CallId = "call-9", Arguments = new Dictionary<string, object?>(),
            RiskAssessment = RiskAssessment.Risky(RiskLevel.Medium, "writes", "Allow?"),
        };

        using (bridge.Preanswer("call-9", new HitlResponseRequest(false, "no")))
        {
            (await bridge.RequestApprovalAsync(request, ct)).Approved.Should().BeFalse();
        }

        sent.Reader.TryRead(out _).Should().BeFalse();

        bridge.Preanswer("call-9", new HitlResponseRequest(true)).Dispose();
        var asked = bridge.RequestApprovalAsync(request, ct);
        (await sent.Reader.ReadAsync(ct)).Should().BeOfType<HitlRequestEvent>("a withdrawn answer is not used");
        bridge.Resolve(new HitlResponseRequest(true));
        (await asked).Approved.Should().BeTrue();
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
