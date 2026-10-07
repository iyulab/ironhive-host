using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Server;

public class AgentServerRunnerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // ── ReadNextRequestAsync ──────────────────────────────────────────

    [Fact]
    public async Task ReadNextRequest_UserMessage_ReturnsUserMessageRequest()
    {
        var json = """{"type":"user_message","content":"hello"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<UserMessageRequest>()
            .Which.Content.Should().Be("hello");
    }

    [Fact]
    public async Task ReadNextRequest_Shutdown_ReturnsShutdownRequest()
    {
        var json = """{"type":"shutdown"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<ShutdownRequest>();
    }

    [Fact]
    public async Task ReadNextRequest_HitlApproved_ReturnsHitlResponseRequest()
    {
        var json = """{"type":"hitl_response","approved":true,"reason":"looks good"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        var hitl = result.Should().BeOfType<HitlResponseRequest>().Subject;
        hitl.Approved.Should().BeTrue();
        hitl.Reason.Should().Be("looks good");
    }

    [Fact]
    public async Task ReadNextRequest_EmptyInput_ReturnsShutdownRequest()
    {
        using var reader = new StringReader("");

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<ShutdownRequest>();
    }

    [Theory]
    [InlineData("{not-valid-json}")]
    [InlineData("""{"type":"no_such_request"}""")]
    [InlineData("null")]
    public async Task ReadNextRequest_UnreadableLine_ThrowsJsonException_AndLeavesTheNextLineReadable(string line)
    {
        using var reader = new StringReader(line + "\n" + """{"type":"user_message","content":"after"}""");

        var act = () => AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);
        await act.Should().ThrowAsync<JsonException>();

        var next = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);
        next.Should().BeOfType<UserMessageRequest>().Which.Content.Should().Be("after");
    }

    [Fact]
    public async Task ReadNextRequest_BlankLines_AreSkipped_NotAShutdown()
    {
        using var reader = new StringReader("\n   \n" + """{"type":"user_message","content":"hi"}""");

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<UserMessageRequest>();
    }

    [Fact]
    public async Task ReadNextRequest_OnlyBlankLinesThenEnd_IsShutdown()
    {
        using var reader = new StringReader("   \n\n");

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<ShutdownRequest>();
    }

    [Fact]
    public async Task RunAsync_UnreadableLineAndBlankLine_ReportErrorAndKeepServing()
    {
        var messages = new List<string>();
        var runner = CreateRunner(content =>
        {
            messages.Add(content);
            return SingleEvent(new TextDeltaEvent("ok"));
        });

        var input = BuildInput(
            "{broken",
            "",
            """{"type":"user_message","content":"still here"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        messages.Should().Equal("still here");
        var events = ParseEvents(output);
        events.OfType<ErrorEvent>().Should().ContainSingle()
            .Which.Message.Should().StartWith("Unreadable request skipped");
    }

    [Fact]
    public async Task ReadNextRequest_ContextUpdate_ReturnsContextUpdateRequest()
    {
        var json = """{"type":"context_update","working_path":"/home/user/docs"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<ContextUpdateRequest>()
            .Which.WorkingPath.Should().Be("/home/user/docs");
    }

    [Fact]
    public async Task ReadNextRequest_Cancel_ReturnsCancelRequest()
    {
        var json = """{"type":"cancel"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<CancelRequest>();
    }

    // ── WriteEventAsync ───────────────────────────────────────────────

    [Fact]
    public async Task WriteEventAsync_WritesJsonLine_ToOutput()
    {
        using var writer = new StringWriter();
        var evt = new TextDeltaEvent("chunk");

        await AgentServerRunner.WriteEventAsync(writer, evt, JsonOpts, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString().TrimEnd();
        using var doc = JsonDocument.Parse(output);
        doc.RootElement.GetProperty("type").GetString().Should().Be("text_delta");
        doc.RootElement.GetProperty("content").GetString().Should().Be("chunk");
    }

    // ── RunAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ContextUpdate_DoesNotInvokeMessageDelegate()
    {
        var called = false;
        var runner = CreateRunner(_ =>
        {
            called = true;
            return EmptyEvents();
        });

        var input = BuildInput(
            """{"type":"context_update","working_path":"/tmp"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        called.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_AfterContextUpdate_MessageIncludesWorkingPathPrefix()
    {
        string? receivedContent = null;
        var runner = CreateRunner(content =>
        {
            receivedContent = content;
            return EmptyEvents();
        });

        var input = BuildInput(
            """{"type":"context_update","working_path":"/home/user"}""",
            """{"type":"user_message","content":"list files"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        receivedContent.Should().NotBeNull();
        receivedContent.Should().Contain("[WorkingPath: /home/user]");
        receivedContent.Should().Contain("list files");
    }

    [Fact]
    public async Task RunAsync_NoContext_MessageIsUnprefixed()
    {
        string? receivedContent = null;
        var runner = CreateRunner(content =>
        {
            receivedContent = content;
            return EmptyEvents();
        });

        var input = BuildInput(
            """{"type":"user_message","content":"hello world"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        receivedContent.Should().Be("hello world");
    }

    [Fact]
    public async Task RunAsync_ProcessorThrows_WritesErrorAndTurnEnd()
    {
        var runner = CreateRunner(_ =>
        {
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162 // Unreachable code detected
            return EmptyEvents();
#pragma warning restore CS0162
        });

        var input = BuildInput(
            """{"type":"user_message","content":"fail"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        var events = ParseEvents(output);
        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ErrorEvent>()
            .Which.Message.Should().Be("boom");
        events[1].Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public async Task RunAsync_MultipleMessages_ProcessesEachSequentially()
    {
        var messages = new List<string>();
        var runner = CreateRunner(content =>
        {
            messages.Add(content);
            return SingleEvent(new TextDeltaEvent($"echo: {content}"));
        });

        var input = BuildInput(
            """{"type":"user_message","content":"first"}""",
            """{"type":"user_message","content":"second"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        messages.Should().Equal("first", "second");
    }

    [Fact]
    public async Task RunAsync_AlwaysWritesTurnEnd_EvenOnSuccess()
    {
        var runner = CreateRunner(_ => SingleEvent(new TextDeltaEvent("ok")));

        var input = BuildInput(
            """{"type":"user_message","content":"hi"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        var events = ParseEvents(output);
        events.Should().HaveCount(2);
        events[0].Should().BeOfType<TextDeltaEvent>();
        events[1].Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public async Task RunAsync_ProcessorYieldsOwnTurnEnd_DoesNotAppendFallback()
    {
        // AgentResponseMapper.ToServerEvents (the real _processMessage pipeline) yields a
        // usage-populated TurnEndEvent as its own last event — the runner must forward that
        // one as-is, not append a second bare TurnEndEvent behind it.
        var runner = CreateRunner(_ => SingleEvent(new TurnEndEvent(InputTokens: 42, OutputTokens: 8)));

        var input = BuildInput(
            """{"type":"user_message","content":"hi"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        var events = ParseEvents(output);
        var turnEnd = events.Should().ContainSingle().Which.Should().BeOfType<TurnEndEvent>().Subject;
        turnEnd.InputTokens.Should().Be(42);
        turnEnd.OutputTokens.Should().Be(8);
    }

    [Fact]
    public async Task RunAsync_OnContextUpdate_CallbackInvoked()
    {
        ContextUpdateRequest? received = null;
        var runner = CreateRunner(_ => EmptyEvents());
        runner.OnContextUpdate = ctx => received = ctx;

        var input = BuildInput(
            """{"type":"context_update","working_path":"/docs"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        received.Should().NotBeNull();
        received!.WorkingPath.Should().Be("/docs");
    }

    [Fact]
    public async Task RunAsync_SkipContextEnrichment_MessageIsUnprefixed()
    {
        string? receivedContent = null;
        var runner = CreateRunner(content =>
        {
            receivedContent = content;
            return EmptyEvents();
        });
        runner.SkipContextEnrichment = true;

        var input = BuildInput(
            """{"type":"context_update","working_path":"/home/user"}""",
            """{"type":"user_message","content":"raw message"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        receivedContent.Should().Be("raw message");
    }

    // ── Cancel ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CancelRequest_WithNoActiveMessage_IsIgnoredAndSessionContinues()
    {
        var called = false;
        var runner = CreateRunner(_ =>
        {
            called = true;
            return EmptyEvents();
        });

        var input = BuildInput(
            """{"type":"cancel"}""",
            """{"type":"user_message","content":"hello"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None);

        called.Should().BeTrue("cancel before any message should be ignored and session should continue");
    }

    [Fact]
    public async Task RunAsync_CancelRequest_DuringHandling_StopsStreamingAndWritesTurnEnd()
    {
        var streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<ServerEvent> Handler(string _, [EnumeratorCancellation] CancellationToken ct)
        {
            streamStarted.TrySetResult();
            while (!ct.IsCancellationRequested)
            {
                yield return new TextDeltaEvent("chunk");
                await Task.Yield();
            }
        }

        var runner = CreateCancellableRunner(Handler);
        var input = new BlockingLineReader();
        using var output = new StringWriter();

        input.Enqueue("""{"type":"user_message","content":"start"}""");
        var runTask = runner.RunAsync(input, output, CancellationToken.None);

        await streamStarted.Task;
        input.Enqueue("""{"type":"cancel"}""");
        input.Enqueue("""{"type":"shutdown"}""");
        input.Complete();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var events = ParseEvents(output);
        events.Should().NotBeEmpty();
        events.Last().Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public async Task RunAsync_AfterCancel_SessionRemainsAliveAndProcessesNextMessage()
    {
        var receivedContents = new List<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<ServerEvent> Handler(string content, [EnumeratorCancellation] CancellationToken ct)
        {
            receivedContents.Add(content);
            if (content == "first")
            {
                firstStarted.TrySetResult();
                while (!ct.IsCancellationRequested)
                {
                    yield return new TextDeltaEvent("streaming");
                    await Task.Yield();
                }
            }
            else
            {
                yield return new TextDeltaEvent("done");
            }
        }

        var runner = CreateCancellableRunner(Handler);
        var input = new BlockingLineReader();
        using var output = new StringWriter();

        input.Enqueue("""{"type":"user_message","content":"first"}""");
        var runTask = runner.RunAsync(input, output, CancellationToken.None);

        await firstStarted.Task;
        input.Enqueue("""{"type":"cancel"}""");
        input.Enqueue("""{"type":"user_message","content":"second"}""");
        input.Enqueue("""{"type":"shutdown"}""");
        input.Complete();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        receivedContents.Should().Equal("first", "second");
    }

    // ── UserMessageRequest.Model ──────────────────────────────────────────

    [Fact]
    public async Task ReadNextRequest_UserMessage_WithModel_PreservesModel()
    {
        var json = """{"type":"user_message","content":"hello","model":"claude-opus"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        var msg = result.Should().BeOfType<UserMessageRequest>().Subject;
        msg.Content.Should().Be("hello");
        msg.Model.Should().Be("claude-opus");
    }

    [Fact]
    public async Task ReadNextRequest_UserMessage_WithoutModel_HasNullModel()
    {
        var json = """{"type":"user_message","content":"hello"}""";
        using var reader = new StringReader(json);

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<UserMessageRequest>()
            .Which.Model.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_UserMessage_ForwardsModelToDelegate()
    {
        UserMessageRequest? received = null;
        var logger = Substitute.For<ILogger<AgentServerRunner>>();
        var runner = new AgentServerRunner(
            (msg, ct) => { received = msg; return EmptyEvents(ct); },
            logger, JsonOpts);

        var input = BuildInput(
            """{"type":"user_message","content":"hello","model":"claude-opus"}""",
            """{"type":"shutdown"}""");

        await runner.RunAsync(input, new StringWriter(), CancellationToken.None);

        received.Should().NotBeNull();
        received!.Model.Should().Be("claude-opus");
        received.Content.Should().Contain("hello");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TurnWaiting_LaterRequestsAreStillRead_EvenAfterAnotherMessage()
    {
        // A turn waits for something only a later request delivers (as an approval waits for its hitl_response).
        // A second user_message arrives before that request; the loop must not stop reading behind it.
        var released = new TaskCompletionSource();
        var order = new List<string>();
        var runner = CreateRunner(content => WaitThenEcho(content, content == "first" ? released.Task : Task.CompletedTask, order));
        runner.OnContextUpdate = _ => released.TrySetResult();

        var input = BuildInput(
            """{"type":"user_message","content":"first"}""",
            """{"type":"user_message","content":"second"}""",
            """{"type":"context_update","working_path":"/x"}""",
            """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        order.Should().Equal("first", "second");
        ParseEvents(output).OfType<TurnEndEvent>().Should().HaveCount(2);
    }

    private static async IAsyncEnumerable<ServerEvent> WaitThenEcho(string content, Task gate, List<string> order)
    {
        await gate;
        lock (order)
        {
            order.Add(content);
        }

        yield return new TextDeltaEvent(content);
    }

    private static AgentServerRunner CreateRunner(
        Func<string, IAsyncEnumerable<ServerEvent>> handler)
    {
        var logger = Substitute.For<ILogger<AgentServerRunner>>();
        return new AgentServerRunner(
            (msg, _) => handler(msg.Content),
            logger,
            JsonOpts);
    }

    private static AgentServerRunner CreateCancellableRunner(
        Func<string, CancellationToken, IAsyncEnumerable<ServerEvent>> handler)
    {
        var logger = Substitute.For<ILogger<AgentServerRunner>>();
        return new AgentServerRunner(
            (msg, ct) => handler(msg.Content, ct),
            logger,
            JsonOpts);
    }

    /// <summary>
    /// A TextReader backed by a Channel, allowing test code to inject lines
    /// asynchronously while RunAsync is executing.
    /// </summary>
    private sealed class BlockingLineReader : TextReader
    {
        private readonly Channel<string?> _channel = Channel.CreateUnbounded<string?>();

        public void Enqueue(string line) => _channel.Writer.TryWrite(line);

        public void Complete() => _channel.Writer.TryComplete();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                return null; // channel complete = EOF
            }

            _channel.Reader.TryRead(out var line);
            return line;
        }
    }

    [Fact]
    public async Task ReadNextRequest_TypeAfterTheOtherMembers_IsRead()
    {
        // Member order carries no meaning in JSON; a client writing from a dictionary may put "type" last.
        using var reader = new StringReader("""{"id":"r1","approved":true,"type":"hitl_response"}""");

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, AgentServerRunner.DefaultJsonOpts, CancellationToken.None);

        var hitl = result.Should().BeOfType<HitlResponseRequest>().Which;
        hitl.Id.Should().Be("r1");
        hitl.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ASynchronousReaderLikeConsoleIn_HandlesEachMessageWhileTheInputStaysOpen()
    {
        // Console.In blocks inside ReadLineAsync. Read inline, the loop waited for the next line before it started
        // handling the first, so an interactive client got no answer until it closed its input.
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = CreateRunner(content =>
        {
            handled.TrySetResult(content);
            return EmptyEvents();
        });
        using var input = new SynchronousLineReader();
        input.Enqueue("""{"type":"user_message","content":"hello"}""");

        var run = Task.Run(() => runner.RunAsync(input, TextWriter.Synchronized(new StringWriter()), CancellationToken.None), TestContext.Current.CancellationToken);

        (await handled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Contain("hello");
        input.Enqueue("""{"type":"shutdown"}""");
        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_Cancelled_WhileASynchronousReaderWaits_Returns()
    {
        // A blocked synchronous read cannot be cancelled; ending the run must not wait for the next line.
        var runner = CreateRunner(_ => EmptyEvents());
        using var input = new SynchronousLineReader();
        using var cts = new CancellationTokenSource();

        var run = Task.Run(() => runner.RunAsync(input, TextWriter.Synchronized(new StringWriter()), cts.Token), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await run.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        input.Enqueue("""{"type":"shutdown"}"""); // releases the blocked reader thread
    }

    /// <summary>
    /// Reads like <see cref="Console.In"/>: <see cref="ReadLineAsync(CancellationToken)"/> blocks the calling thread
    /// until a line arrives, and ignores cancellation once it waits.
    /// </summary>
    private sealed class SynchronousLineReader : TextReader
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> _lines = [];

        public void Enqueue(string line) => _lines.Add(line);

        public override string? ReadLine() => _lines.Take();

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            => cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<string?>(cancellationToken)
                : new ValueTask<string?>(ReadLine());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lines.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static StringReader BuildInput(params string[] lines)
        => new StringReader(string.Join('\n', lines));

    private static async IAsyncEnumerable<ServerEvent> EmptyEvents(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        if (ct.IsCancellationRequested)
        {
            yield break;
        }

        yield break;
    }

    private static async IAsyncEnumerable<ServerEvent> SingleEvent(
        ServerEvent evt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        if (ct.IsCancellationRequested)
        {
            yield break;
        }

        yield return evt;
    }

    private static List<ServerEvent> ParseEvents(StringWriter writer)
    {
        var events = new List<ServerEvent>();
        foreach (var line in writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var evt = JsonSerializer.Deserialize<ServerEvent>(line.Trim(), JsonOpts);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }
        return events;
    }
}
