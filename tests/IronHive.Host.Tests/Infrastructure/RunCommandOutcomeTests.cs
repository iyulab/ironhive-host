using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using IronHive.Abstractions.Exceptions;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Cli.Commands;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>ironhive run</c> for a caller that is not a person: the <c>--json</c> document and the exit code say whether the
/// turn completed and why not, so a script or a benchmark harness never reads prose to tell "done" from "gave up".
/// </summary>
public sealed class RunCommandOutcomeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TokenUsage Usage = new() { InputTokens = 120, OutputTokens = 30 };

    /// <summary>The final chunk of a turn that ended for <paramref name="reason"/> — what the loop always emits last.</summary>
    private static AgentResponseChunk Final(TurnStopReason reason, params ToolCallResult[] calls) => new()
    {
        Usage = Usage,
        Turn = new TurnRecord { Content = "answer", StopReason = reason, ToolCalls = calls, Usage = Usage },
    };

    /// <summary>The loop streams <paramref name="chunks"/> for the prompt <c>task</c>, honouring the run's token between chunks.</summary>
    private static void Streams(IAgentLoop loop, params AgentResponseChunk[] chunks) =>
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call => Replay(chunks, call.ArgAt<CancellationToken>(1)));

    private static async IAsyncEnumerable<AgentResponseChunk> Replay(
        IEnumerable<AgentResponseChunk> chunks, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }

    /// <summary>A loop that streams <paramref name="before"/>, then stalls until the run's token is cancelled.</summary>
    private static void StallsAfter(IAgentLoop loop, params AgentResponseChunk[] before) =>
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call => Stall(before, call.ArgAt<CancellationToken>(1)));

    private static async IAsyncEnumerable<AgentResponseChunk> Stall(
        IEnumerable<AgentResponseChunk> before, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in before)
        {
            yield return chunk;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private static AgentResponseChunk Started(string id, string tool, string arguments) =>
        new() { ToolCallDelta = new ToolCallChunk { Id = id, NameDelta = tool, ArgumentsDelta = arguments } };

    private static AgentResponseChunk Finished(string id, string tool, bool success) =>
        new() { ToolResult = new ToolCallResult { CallId = id, ToolName = tool, Arguments = "{}", Result = "r", Success = success } };

    private static ToolCallResult Call(bool? success, ToolCallRefusalKind? refusal = null) => new()
    {
        CallId = Guid.NewGuid().ToString("N"),
        ToolName = "write_file",
        Arguments = "{}",
        Result = "r",
        Success = success,
        RefusalKind = refusal,
    };

    private static (RunCommand Command, IAgentLoop Loop, IronHiveConfig Config) Command()
    {
        var loop = Substitute.For<IAgentLoop>();
        var factory = Substitute.For<IHostAgentLoopFactory>();
        factory.CreateAsync(Arg.Any<AgentLoopFactoryOptions>(), Arg.Any<CancellationToken>()).Returns(loop);
        var config = new IronHiveConfig();
        return (new RunCommand(factory, config: config), loop, config);
    }

    private static async Task<(int Exit, JsonElement Json)> RunJsonAsync(RunCommand command, RunCommand.Settings settings)
    {
        using var stdout = new StringWriter();
        var exit = await command.RunOnceAsync(settings, stdout, Ct);
        return (exit, JsonDocument.Parse(stdout.ToString()).RootElement.Clone());
    }

    [Theory]
    [InlineData(TurnStopReason.Completed, 0, "completed")]
    [InlineData(TurnStopReason.StepLimit, 2, "step_limit")]
    [InlineData(TurnStopReason.OutputLimit, 2, "output_limit")]
    [InlineData(TurnStopReason.ToolTerminated, 2, "tool_terminated")]
    [InlineData(TurnStopReason.ContentFilter, 3, "content_filter")]
    public async Task TheExitCodeAndStopReason_SayHowTheTurnEnded(TurnStopReason reason, int exit, string text)
    {
        var (command, loop, _) = Command();
        Streams(loop, Final(reason));

        var (code, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        code.Should().Be(exit);
        json.GetProperty("stop_reason").GetString().Should().Be(text);
        json.GetProperty("content").GetString().Should().Be("answer");
        json.GetProperty("duration_ms").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        json.GetProperty("usage").GetProperty("output_tokens").GetInt64().Should().Be(30);
    }

    [Fact]
    public async Task ToolCalls_AreCounted_RefusalsApartFromFailures()
    {
        var (command, loop, _) = Command();
        Streams(loop, Final(TurnStopReason.Completed,
            Call(true), Call(false, ToolCallRefusalKind.ApprovalUnavailable), Call(false, ToolCallRefusalKind.Denied), Call(false)));

        var (_, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        json.GetProperty("tool_calls").GetInt32().Should().Be(4);
        json.GetProperty("refused_tool_calls").GetInt32().Should().Be(2);
        json.GetProperty("failed_tool_calls").GetInt32().Should().Be(1, "a tool that ran and failed is not a refusal");
    }

    [Fact]
    public async Task EachCall_IsListedInOrder_WithItsOutcome_AndARefusalSaysWhy()
    {
        var (command, loop, _) = Command();
        var longArguments = "{\"content\":\"" + new string('x', 1000) + "\"}";
        Streams(loop, Final(TurnStopReason.ToolTerminated,
            Call(true) with { ToolName = "describe_image", Arguments = "{\"path\":\"a.jpg\"}", Result = "a beach" },
            Call(false) with { Arguments = longArguments, Result = "disk full" },
            Call(false, ToolCallRefusalKind.RepeatedCall) with { ToolName = "describe_image", Result = "'describe_image' already ran 3 times" }));

        var (_, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        var calls = json.GetProperty("calls").EnumerateArray().ToList();
        calls.Select(c => c.GetProperty("outcome").GetString()).Should().Equal("ok", "failed", "refused");
        calls[0].GetProperty("tool").GetString().Should().Be("describe_image");
        calls[0].GetProperty("arguments").GetString().Should().Be("{\"path\":\"a.jpg\"}");
        calls[0].GetProperty("refusal").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "a tool's own result is not reported");
        calls[1].GetProperty("arguments").GetString().Should().HaveLength(RunOutcome.CallTextLimit + 1).And.EndWith("…");
        calls[2].GetProperty("refusal_kind").GetString().Should().Be("repeated_call");
        calls[2].GetProperty("refusal").GetString().Should().Be("'describe_image' already ran 3 times");
    }

    [Fact]
    public async Task ARunTheCallerCancels_StopsWithCancelled_AndExitCode130_NotAnError()
    {
        var (command, loop, _) = Command();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call =>
        {
            caller.Cancel();
            return Replay([Final(TurnStopReason.Completed)], call.ArgAt<CancellationToken>(1));
        });

        using var stdout = new StringWriter();
        var code = await command.RunOnceAsync(
            new RunCommand.Settings { PromptOption = "task", Json = true, TimeoutSeconds = 60 }, stdout, caller.Token);
        var json = JsonDocument.Parse(stdout.ToString()).RootElement;

        code.Should().Be(130);
        json.GetProperty("stop_reason").GetString().Should().Be("cancelled");
    }

    [Fact]
    public async Task ARunPastItsTimeout_StopsWithTimeout_AndExitCode2()
    {
        var (command, loop, _) = Command();
        StallsAfter(loop);


        var (code, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true, TimeoutSeconds = 1 });

        code.Should().Be(2);
        json.GetProperty("stop_reason").GetString().Should().Be("timeout");
        json.GetProperty("duration_ms").GetInt64().Should().BeGreaterThanOrEqualTo(900);
    }

    [Fact]
    public async Task ARunPastItsTimeout_ReportsTheWorkItDidBeforeTheCut()
    {
        // Two calls finish, a third starts and never reports back, then the model stalls past the timeout. Before, the
        // document held only the error — every step taken before the cut was lost.
        var (command, loop, _) = Command();
        StallsAfter(loop,
            new AgentResponseChunk { TextDelta = "Reading the files. " },
            Started("c1", "read_file", "{\"path\":\"a.txt\"}"),
            Finished("c1", "read_file", success: true),
            Started("c2", "run_command", "{\"command\":\"make\"}"),
            Finished("c2", "run_command", success: false),
            new AgentResponseChunk { TextDelta = "Now writing the fix." },
            Started("c3", "write_file", "{\"path\":\"b.txt\"}"));

        var (code, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true, TimeoutSeconds = 1 });

        code.Should().Be(2);
        json.GetProperty("stop_reason").GetString().Should().Be("timeout");
        json.GetProperty("error").GetString().Should().Contain("did not finish");
        json.GetProperty("content").GetString().Should().Be("Reading the files. Now writing the fix.");
        json.GetProperty("tool_calls").GetInt32().Should().Be(3);
        json.GetProperty("failed_tool_calls").GetInt32().Should().Be(1);
        var calls = json.GetProperty("calls").EnumerateArray().ToList();
        calls.Select(c => c.GetProperty("tool").GetString()).Should().Equal("read_file", "run_command", "write_file");
        calls.Select(c => c.GetProperty("outcome").GetString()).Should().Equal("ok", "failed", "unfinished");
        calls[2].GetProperty("arguments").GetString().Should().Be("{\"path\":\"b.txt\"}");
    }

    [Fact]
    public async Task ARunTheCallerCancels_AlsoReportsTheWorkItDid()
    {
        var (command, loop, _) = Command();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call => CancelAfterOneCall(caller, call.ArgAt<CancellationToken>(1)));

        using var stdout = new StringWriter();
        var code = await command.RunOnceAsync(new RunCommand.Settings { PromptOption = "task", Json = true }, stdout, caller.Token);
        var json = JsonDocument.Parse(stdout.ToString()).RootElement;

        code.Should().Be(130);
        json.GetProperty("stop_reason").GetString().Should().Be("cancelled");
        json.GetProperty("calls").EnumerateArray().Single().GetProperty("outcome").GetString().Should().Be("ok");

        static async IAsyncEnumerable<AgentResponseChunk> CancelAfterOneCall(
            CancellationTokenSource caller, [EnumeratorCancellation] CancellationToken token)
        {
            yield return Started("c1", "read_file", "{}");
            yield return Finished("c1", "read_file", success: true);
            await caller.CancelAsync();
            token.ThrowIfCancellationRequested();
        }
    }

    [Fact]
    public async Task ACompletedTurn_ReportsTheFinalRecord_NotTheStreamedPieces()
    {
        // The final chunk's record is the authority: an observer's addendum rides on it, after the model's text.
        var (command, loop, _) = Command();
        Streams(loop,
            new AgentResponseChunk { TextDelta = "partial text the record supersedes" },
            Final(TurnStopReason.Completed, Call(true)) with { Addendum = "note" });

        var (_, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        json.GetProperty("content").GetString().Should().Be("answer\n\nnote");
        json.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("tool_calls").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AProviderThatCannotBeCreated_IsAJsonError_WithExitCode1()
    {
        var factory = Substitute.For<IHostAgentLoopFactory>();
        factory.CreateAsync(Arg.Any<AgentLoopFactoryOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No provider configured."));
        var command = new RunCommand(factory);

        var (code, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        code.Should().Be(1);
        json.GetProperty("stop_reason").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Contain("No provider configured");
        json.GetProperty("error_kind").GetString().Should().Be(RunOutcome.RunErrorKind, "a configuration error is the run's, not the provider's");
    }

    public static TheoryData<Exception, string> Errors() => new()
    {
        { new HttpRequestException("The SSL connection could not be established, see inner exception."), RunOutcome.ProviderErrorKind },
        { new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely."), RunOutcome.ProviderErrorKind },
        { new ProviderHttpException("Chat completion request failed with status 530 (<none>).", (HttpStatusCode)530), RunOutcome.ProviderErrorKind },
        { new ProviderHttpException("busy", HttpStatusCode.TooManyRequests), RunOutcome.ProviderErrorKind },
        { new RateLimitException("slow down"), RunOutcome.ProviderErrorKind },
        { new ProviderResponseException("the model crashed"), RunOutcome.ProviderErrorKind },
        { new InvalidOperationException("a step failed", new HttpRequestException("Connection reset by peer")), RunOutcome.ProviderErrorKind },
        { new ProviderHttpException("invalid request", HttpStatusCode.BadRequest), RunOutcome.RunErrorKind },
        { new ContextOverflowException("request exceeds the context size"), RunOutcome.RunErrorKind },
        { new InvalidOperationException("a defect"), RunOutcome.RunErrorKind },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task AnError_SaysWhetherTheProviderOrTheRunFailed(Exception error, string kind)
    {
        // A benchmark harness counts a provider failure as the environment's, not the agent's — it needs the class, and the
        // message alone ("The SSL connection could not be established") is prose.
        var (command, loop, _) = Command();
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(_ => Throws(error));

        var (code, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        code.Should().Be(1);
        json.GetProperty("stop_reason").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be(error.Message);
        json.GetProperty("error_kind").GetString().Should().Be(kind);

        static async IAsyncEnumerable<AgentResponseChunk> Throws(Exception error)
        {
            await Task.Yield();
            yield return new AgentResponseChunk { TextDelta = "started" };
            throw error;
        }
    }

    [Fact]
    public async Task ACompletedOrTimedOutRun_HasNoErrorKind()
    {
        var (command, loop, _) = Command();
        Streams(loop, Final(TurnStopReason.Completed));

        var (_, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true });

        json.GetProperty("error_kind").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task MaxIterations_SetsTheTurnsCap_BeforeTheLoopIsCreated()
    {
        var (command, loop, config) = Command();
        int? capWhenCreated = null;
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call =>
        {
            capWhenCreated = config.ChatBehavior.MaximumIterationsPerRequest;
            return Replay([Final(TurnStopReason.Completed)], call.ArgAt<CancellationToken>(1));
        });

        await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true, MaxIterations = 40 });

        capWhenCreated.Should().Be(40);
        new IronHiveConfig().ChatBehavior.MaximumIterationsPerRequest.Should().NotBe(40, "the positive control: 40 is not the default");
    }

    [Fact]
    public async Task ToolTimeout_SetsThePerCallToolLimit_BeforeTheLoopIsCreated()
    {
        var (command, loop, config) = Command();
        int? limitWhenRun = null;
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call =>
        {
            limitWhenRun = config.ChatBehavior.ToolCallTimeoutSeconds;
            return Replay([Final(TurnStopReason.Completed)], call.ArgAt<CancellationToken>(1));
        });

        await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true, ToolTimeoutSeconds = 45 });

        limitWhenRun.Should().Be(45);
        new IronHiveConfig().ChatBehavior.ToolCallTimeoutSeconds.Should().BeNull("the positive control: no limit by default");
    }

    [Fact]
    public void ANonPositiveToolTimeout_IsRefused()
    {
        new RunCommand.Settings { ToolTimeoutSeconds = 0 }.Validate().Successful.Should().BeFalse();
    }

    [Fact]
    public async Task MaxOutputTokens_SetsThePerCallCap_BeforeTheLoopIsCreated()
    {
        var (command, loop, config) = Command();
        int? capWhenRun = null;
        loop.RunStreamingAsync("task", Arg.Any<CancellationToken>()).Returns(call =>
        {
            capWhenRun = config.ChatBehavior.MaxOutputTokens;
            return Replay([Final(TurnStopReason.Completed)], call.ArgAt<CancellationToken>(1));
        });

        await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true, MaxOutputTokens = 16384 });

        capWhenRun.Should().Be(16384);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    public void NonPositiveLimits_AreRefused(int? maxIterations, int? timeout)
    {
        new RunCommand.Settings { MaxIterations = maxIterations, TimeoutSeconds = timeout }.Validate().Successful.Should().BeFalse();
    }
}
