using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

using FluentAssertions;

using IronHive.Host.Core.Server;
using IronHive.Host.Protocol;

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

    [Fact]
    public async Task ReadNextRequest_MalformedJson_ReturnsShutdownRequest()
    {
        using var reader = new StringReader("{not-valid-json}");

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<ShutdownRequest>();
    }

    [Fact]
    public async Task ReadNextRequest_WhitespaceLine_ReturnsShutdownRequest()
    {
        using var reader = new StringReader("   ");

        var result = await AgentServerRunner.ReadNextRequestAsync(reader, JsonOpts, CancellationToken.None);

        result.Should().BeOfType<ShutdownRequest>();
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

        await AgentServerRunner.WriteEventAsync(writer, evt, JsonOpts);

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

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

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

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

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
