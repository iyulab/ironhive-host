using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Server;

/// <summary>
/// The wire approver: an <c>Ask</c> verdict becomes a <c>hitl_request</c>, the matching <c>hitl_response</c> answers it, and
/// every way of not answering (no client, timeout, disconnect) is a rejection the model reads. The runner facts drive
/// <see cref="AgentServerRunner"/> over real JSON lines, so ordering and line integrity are observed where a client sees them.
/// </summary>
public class HitlBridgeTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static ApprovalRequest Request(string tool = "WriteFile", string? callId = "call-1") => new()
    {
        ToolName = tool,
        Arguments = new Dictionary<string, object?> { ["path"] = "app.json", ["content"] = "{}" },
        RiskAssessment = RiskAssessment.Risky(RiskLevel.Medium, "Configuration file", "Allow this operation?"),
        Description = "Allow this operation?",
        CallId = callId
    };

    private static (HitlBridge Bridge, Channel<HitlRequestEvent> Sent, IDisposable Attachment) Attached(TimeSpan? timeout = null)
    {
        var bridge = new HitlBridge(timeout);
        var sent = Channel.CreateUnbounded<HitlRequestEvent>();
        var attachment = bridge.Attach((evt, _) =>
        {
            sent.Writer.TryWrite((HitlRequestEvent)evt);
            return Task.CompletedTask;
        });
        return (bridge, sent, attachment);
    }

    [Fact]
    public async Task WithNoClientAttached_TheRequestIsRejected()
    {
        using var bridge = new HitlBridge();

        var result = await bridge.RequestApprovalAsync(Request(), TestContext.Current.CancellationToken);

        result.Approved.Should().BeFalse();
        result.RejectionReason.Should().Contain("no client");
    }

    [Fact]
    public async Task TheRequestCarriesTheCall_AndTheMatchingAnswerApprovesIt_WithEditedArguments()
    {
        var (bridge, sent, attachment) = Attached();
        using var _ = bridge;
        using var __ = attachment;
        var ct = TestContext.Current.CancellationToken;

        var pending = bridge.RequestApprovalAsync(Request(), ct);
        var evt = await sent.Reader.ReadAsync(ct);

        evt.ToolName.Should().Be("WriteFile");
        evt.CallId.Should().Be("call-1");
        evt.Target.Should().Be("app.json");
        evt.Action.Should().Be("Configuration file");
        evt.Level.Should().Be("medium");
        evt.Arguments!.Value.GetProperty("path").GetString().Should().Be("app.json");

        var edited = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("other.json") };
        bridge.Resolve(new HitlResponseRequest(true, Id: evt.Id, ModifiedArguments: edited, AlwaysApprove: true)).Should().BeTrue();

        var result = await pending;
        result.Approved.Should().BeTrue();
        result.AlwaysApprove.Should().BeTrue();
        ((JsonElement)result.ModifiedArguments!["path"]!).GetString().Should().Be("other.json");
    }

    [Fact]
    public async Task TwoWaitingRequests_AreAnsweredByTheirIds_InEitherOrder()
    {
        var (bridge, sent, attachment) = Attached();
        using var _ = bridge;
        using var __ = attachment;
        var ct = TestContext.Current.CancellationToken;

        var first = bridge.RequestApprovalAsync(Request(callId: "a"), ct);
        var second = bridge.RequestApprovalAsync(Request(callId: "b"), ct);
        var e1 = await sent.Reader.ReadAsync(ct);
        var e2 = await sent.Reader.ReadAsync(ct);
        var byCall = new[] { e1, e2 }.ToDictionary(e => e.CallId!);

        // An answer without an id is ambiguous while two wait: dropped, both keep waiting.
        bridge.Resolve(new HitlResponseRequest(true)).Should().BeFalse();

        bridge.Resolve(new HitlResponseRequest(false, "not b", byCall["b"].Id)).Should().BeTrue();
        bridge.Resolve(new HitlResponseRequest(true, Id: byCall["a"].Id)).Should().BeTrue();

        (await first).Approved.Should().BeTrue();
        var b = await second;
        b.Approved.Should().BeFalse();
        b.RejectionReason.Should().Be("not b");
    }

    [Fact]
    public async Task AnAnswerWithoutAnId_ResolvesTheOnlyWaitingRequest()
    {
        var (bridge, sent, attachment) = Attached();
        using var _ = bridge;
        using var __ = attachment;
        var ct = TestContext.Current.CancellationToken;

        var pending = bridge.RequestApprovalAsync(Request(), ct);
        await sent.Reader.ReadAsync(ct);

        bridge.Resolve(new HitlResponseRequest(true)).Should().BeTrue();
        (await pending).Approved.Should().BeTrue();
    }

    [Fact]
    public async Task NoAnswerWithinTheTimeout_IsARejection()
    {
        var (bridge, _, attachment) = Attached(TimeSpan.FromMilliseconds(100));
        using var b = bridge;
        using var a = attachment;

        var result = await bridge.RequestApprovalAsync(Request(), TestContext.Current.CancellationToken);

        result.Approved.Should().BeFalse();
        result.RejectionReason.Should().Contain("no answer");
    }

    [Fact]
    public async Task CancellingTheTurn_CancelsTheWait()
    {
        var (bridge, sent, attachment) = Attached();
        using var b = bridge;
        using var a = attachment;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var pending = bridge.RequestApprovalAsync(Request(), cts.Token);
        await sent.Reader.ReadAsync(TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DetachingTheClient_RejectsWhatIsStillWaiting()
    {
        var (bridge, sent, attachment) = Attached();
        using var b = bridge;
        var ct = TestContext.Current.CancellationToken;

        var pending = bridge.RequestApprovalAsync(Request(), ct);
        await sent.Reader.ReadAsync(ct);
        attachment.Dispose();

        var result = await pending;
        result.Approved.Should().BeFalse();
        bridge.IsAttached.Should().BeFalse();
    }

    // ── Over the stdio runner ─────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StdioRunner_AnAskedCall_WaitsForTheClientsAnswer_AndRunsOnlyOnApproval(bool approve)
    {
        var ct = TestContext.Current.CancellationToken;
        using var bridge = new HitlBridge(TimeSpan.FromSeconds(30));
        var config = PermissionConfig.CreateDefault();          // *.json edits are an Ask rule
        var gate = new ApprovalGate(new ToolCallPolicy(config), bridge);

        async IAsyncEnumerable<ServerEvent> Process(UserMessageRequest msg, [EnumeratorCancellation] CancellationToken token)
        {
            var args = new Dictionary<string, object?> { ["path"] = "app.json", ["content"] = "{}" };
            yield return new ToolStartEvent("WriteFile", CallId: "call-7");
            var decision = await gate.DecideAsync("WriteFile", args, "call-7", token);
            yield return new ToolEndEvent("WriteFile", decision.ShouldProceed, decision.ShouldProceed ? "wrote" : decision.Refusal!.ToString(), "call-7");
            yield return new TurnEndEvent();
        }

        var input = new LineInput();
        var output = new LineOutput();
        var runner = new AgentServerRunner(Process, Substitute.For<ILogger<AgentServerRunner>>(), JsonOpts, hitlBridge: bridge);
        var run = runner.RunAsync(input, output, ct);

        input.Enqueue("""{"type":"user_message","content":"write it"}""");
        var events = new List<ServerEvent>();
        HitlRequestEvent? request = null;
        while (request is null)
        {
            var evt = JsonSerializer.Deserialize<ServerEvent>(await output.Lines.ReadAsync(ct), JsonOpts)!;
            events.Add(evt);
            request = evt as HitlRequestEvent;
        }

        request.CallId.Should().Be("call-7");
        input.Enqueue(JsonSerializer.Serialize<ServerRequest>(new HitlResponseRequest(approve, approve ? null : "no", request.Id), JsonOpts));

        while (events[^1] is not TurnEndEvent)
        {
            events.Add(JsonSerializer.Deserialize<ServerEvent>(await output.Lines.ReadAsync(ct), JsonOpts)!);
        }

        input.Complete();
        await run;

        events.Select(e => e.GetType().Name).Should().Equal(
            nameof(ToolStartEvent), nameof(HitlRequestEvent), nameof(ToolEndEvent), nameof(TurnEndEvent));
        events.OfType<ToolEndEvent>().Single().Success.Should().Be(approve);
        bridge.IsAttached.Should().BeFalse("the runner detaches when it stops");
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

    // Each event is one WriteLineAsync; a line that is not one whole JSON document fails the deserialization above.
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
