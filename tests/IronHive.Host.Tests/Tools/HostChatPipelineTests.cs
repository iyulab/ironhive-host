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
