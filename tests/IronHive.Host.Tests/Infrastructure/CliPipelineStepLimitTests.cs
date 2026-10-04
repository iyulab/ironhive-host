using AwesomeAssertions;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The CLI's own chat pipeline at its iteration cap: the turn reports the tool calls that ran and
/// <see cref="TurnStopReason.StepLimit"/> — what <c>ironhive run --json</c> turns into <c>step_limit</c> and exit code 2.
/// </summary>
public sealed class CliPipelineStepLimitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Calls the tool whenever tools are offered; plain text otherwise.</summary>
    private sealed class ToolHappyModel : IChatClient
    {
        private int _n;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if ((options?.Tools?.Count ?? 0) > 0)
            {
                _n++;
                var call = new FunctionCallContent($"call-{_n}", "write_note", new Dictionary<string, object?> { ["text"] = $"n{_n}" });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls });
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "<tool_call>as text</tool_call>")) { FinishReason = ChatFinishReason.Stop });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task AtTheCap_TheTurnReportsTheCallsThatRan_AndStepLimit()
    {
        var policy = Substitute.For<IToolCallPolicy>();
        policy.Evaluate(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new RiskAssessment { Verdict = IronHive.Agent.Permissions.PermissionAction.Allow });
        var pipeline = ServiceCollectionExtensions.CreateToolInvocationPipeline(policy, approvalService: null, loggerFactory: null);
        var client = ServiceCollectionExtensions.DecorateChatClient(
            new ToolHappyModel(), new ChatBehaviorConfig { MaximumIterationsPerRequest = 1 }, pipeline);
        var loop = new AgentLoop(client);
        var tools = new ChatOptions { Tools = [AIFunctionFactory.Create((string text) => "noted " + text, "write_note")] };

        var response = await loop.RunAsync("take notes", tools, Ct);

        response.ToolCalls.Should().HaveCount(1, "the first round's call ran");
        response.StopReason.Should().Be(TurnStopReason.StepLimit);
    }
}
