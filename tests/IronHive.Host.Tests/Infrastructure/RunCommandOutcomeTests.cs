using System.Text.Json;
using AwesomeAssertions;
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

    private static AgentResponse Response(TurnStopReason reason, params ToolCallResult[] calls) => new()
    {
        Content = "answer",
        HasTextOutput = true,
        StopReason = reason,
        ToolCalls = calls,
        Usage = new TokenUsage { InputTokens = 120, OutputTokens = 30 },
    };

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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(Response(reason));

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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(Response(TurnStopReason.Completed,
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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(Response(TurnStopReason.ToolTerminated,
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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(call =>
        {
            caller.Cancel();
            call.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested();
            return Task.FromResult(Response(TurnStopReason.Completed));
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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(1));
            return Response(TurnStopReason.Completed);
        });

        var (code, json) = await RunJsonAsync(command, new RunCommand.Settings { PromptOption = "task", Json = true, TimeoutSeconds = 1 });

        code.Should().Be(2);
        json.GetProperty("stop_reason").GetString().Should().Be("timeout");
        json.GetProperty("duration_ms").GetInt64().Should().BeGreaterThanOrEqualTo(900);
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
    }

    [Fact]
    public async Task MaxIterations_SetsTheTurnsCap_BeforeTheLoopIsCreated()
    {
        var (command, loop, config) = Command();
        int? capWhenCreated = null;
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            capWhenCreated = config.ChatBehavior.MaximumIterationsPerRequest;
            return Response(TurnStopReason.Completed);
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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            limitWhenRun = config.ChatBehavior.ToolCallTimeoutSeconds;
            return Response(TurnStopReason.Completed);
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
        loop.RunAsync("task", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            capWhenRun = config.ChatBehavior.MaxOutputTokens;
            return Response(TurnStopReason.Completed);
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
