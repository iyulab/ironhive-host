using AwesomeAssertions;
using IronHive.Agent.Context;
using IronHive.Agent.Invocation;
using IronHive.Agent.Mode;
using IronHive.Host.Config;
using IronHive.Host.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Tools;

/// <summary>The public runtime composition an embedding host calls to run as the CLI does.</summary>
public sealed class HostChatPipelineTests
{
    [Fact]
    public void ThePipeline_HasTheApprovalGate_OnlyWhenGivenAPolicy()
    {
        var gated = HostChatPipeline.CreateToolInvocationPipeline(Substitute.For<IToolCallPolicy>());
        var unattended = HostChatPipeline.CreateToolInvocationPipeline(policy: null);

        gated.InvocationMiddleware.Select(m => m.GetType()).Should().Equal(
            typeof(ArgumentParseFailureMiddleware), typeof(RepeatedCallGuardMiddleware), typeof(RepeatedResultGuardMiddleware),
            typeof(RepeatedErrorGuardMiddleware), typeof(ApprovalGateMiddleware), typeof(ResilientArgumentsMiddleware));
        unattended.InvocationMiddleware.Select(m => m.GetType()).Should().Equal(
            typeof(ArgumentParseFailureMiddleware), typeof(RepeatedCallGuardMiddleware), typeof(RepeatedResultGuardMiddleware),
            typeof(RepeatedErrorGuardMiddleware), typeof(ResilientArgumentsMiddleware));
    }

    // compaction.maxContextTokens: the window a user declares for a model whose provider reports none (a gateway). The
    // budget guard uses it; with none declared it has no budget and the server enforces its window.
    [Theory]
    [InlineData(1000, false)]
    [InlineData(0, true)]
    public async Task TheBudgetGuard_UsesTheDeclaredWindow_WhenTheProviderReportsNone(int declaredContextWindow, bool reachesTheModel)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = HostChatPipeline.Decorate(inner, new ChatBehaviorConfig(), HostChatPipeline.CreateToolInvocationPipeline(policy: null),
            telemetrySourceName: null, declaredContextWindow);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, new string('x', 60000))],
            cancellationToken: TestContext.Current.CancellationToken);

        await inner.Received(reachesTheModel ? 1 : 0).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        response.FinishReason.Should().Be(reachesTheModel ? null : ChatFinishReason.Length);
    }

    [Fact]
    public void TheDecoratedClient_CarriesTheCliDecorators_AndTelemetryOnlyWhenNamed()
    {
        var tools = HostChatPipeline.CreateToolInvocationPipeline(policy: null);
        var behavior = new ChatBehaviorConfig { MaximumIterationsPerRequest = 20 };

        var plain = HostChatPipeline.Decorate(Substitute.For<IChatClient>(), behavior, tools);
        var traced = HostChatPipeline.Decorate(Substitute.For<IChatClient>(), behavior, tools, telemetrySourceName: "test.chat");

        plain.GetService<FunctionInvokingChatClient>()!.MaximumIterationsPerRequest.Should().Be(20);
        plain.GetService<ToolRoundContextChatClient>().Should().NotBeNull();
        plain.GetService<TokenBudgetChatClient>().Should().NotBeNull();
        plain.GetService<OpenTelemetryChatClient>().Should().BeNull();
        traced.GetService<OpenTelemetryChatClient>().Should().NotBeNull();
    }
}
