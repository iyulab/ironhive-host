using System.Runtime.CompilerServices;
using AwesomeAssertions;
using IronHive.Agent.Invocation;
using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Host.Config;
using IronHive.Host.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;
using CliServices = IronHive.Cli.Infrastructure.ServiceCollectionExtensions;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The tool invocation pipeline the CLI and <c>run --server</c> put behind every provider client: loop guards, then
/// the permission gate, then the resilient-arguments step directly around the tool.
/// </summary>
public class CliToolInvocationPipelineTests
{
    private const string MarshallerMissingPathMessage =
        "The arguments dictionary is missing a value for the required parameter 'path'.";

    private const string MarshallerParamName = "arguments";

    [Fact]
    public void Pipeline_RunsTheLoopGuardsBeforeTheGate_AndTheResilientStepLast()
    {
        var pipeline = CliServices.CreateToolInvocationPipeline(
            Substitute.For<IModeToolFilter>(), approvalService: null, loggerFactory: null);

        pipeline.InvocationMiddleware.Select(m => m.GetType()).Should().Equal(
            typeof(ArgumentParseFailureMiddleware),
            typeof(RepeatedCallGuardMiddleware),
            typeof(RepeatedErrorGuardMiddleware),
            typeof(ApprovalGateMiddleware),
            typeof(ResilientArgumentsMiddleware));
    }

    [Fact]
    public async Task DecoratedClient_ADeniedCall_NeverReachesTheTool_AndTheModelReadsTheDenial()
    {
        var ran = false;
        var tool = AIFunctionFactory.Create((string path) => { ran = true; return "ok"; }, "write_file");
        var filter = FilterWith(PermissionAction.Deny);
        var inner = new ScriptedChatClient(new FunctionCallContent("c1", "write_file", new Dictionary<string, object?> { ["path"] = "a.txt" }));
        var client = Decorate(inner, filter);

        await client.GetResponseAsync("go", new ChatOptions { Tools = [tool] }, TestContext.Current.CancellationToken);

        ran.Should().BeFalse();
        var result = inner.ToolResults.Should().ContainSingle().Subject;
        result.Result!.ToString().Should().Contain("Permission denied");
        result.Result.ToString().Should().NotContain("rejected the call",
            "a call the gate refused never reaches the resilient step");
    }

    [Fact]
    public async Task DecoratedClient_AnAllowedCallWhoseArgumentsDoNotBind_PassesTheGate_AndReturnsTheDirective()
    {
        var filter = FilterWith(PermissionAction.Allow);
        var inner = new ScriptedChatClient(new FunctionCallContent("c1", "read_file", new Dictionary<string, object?>()));
        var client = Decorate(inner, filter);

        await client.GetResponseAsync("go", new ChatOptions { Tools = [new MissingPathFunction()] }, TestContext.Current.CancellationToken);

        filter.Received(1).AssessRisk("read_file", Arg.Any<IDictionary<string, object?>?>());
        var result = inner.ToolResults.Should().ContainSingle().Subject;
        result.Exception.Should().BeNull("the marshaller error is answered with a directive, not reported as a failure");
        result.Result.Should().BeOfType<string>().Which.Should().Contain("required parameter 'path'");
    }

    [Fact]
    public async Task DecoratedClient_AnUnparseableCall_IsRefusedBeforeTheGateAsks()
    {
        var filter = FilterWith(PermissionAction.Ask);
        var approval = Substitute.For<IHumanApprovalService>();
        var call = new FunctionCallContent("c1", "read_file", new Dictionary<string, object?>())
        {
            Exception = new System.Text.Json.JsonException("Unexpected end of data."),
        };
        var inner = new ScriptedChatClient(call);
        var client = CliServices.DecorateChatClient(
            inner, new ChatBehaviorConfig(), CliServices.CreateToolInvocationPipeline(filter, approval, loggerFactory: null));

        await client.GetResponseAsync("go", new ChatOptions { Tools = [new MissingPathFunction()] }, TestContext.Current.CancellationToken);

        await approval.DidNotReceiveWithAnyArgs().RequestApprovalAsync(default!, TestContext.Current.CancellationToken);
        var result = inner.ToolResults.Should().ContainSingle().Subject;
        result.Result.Should().BeOfType<ToolCallRefusal>().Which.Kind.Should().Be(ToolCallRefusalKind.InvalidArguments);
    }

    private static IChatClient Decorate(IChatClient inner, IModeToolFilter filter) =>
        CliServices.DecorateChatClient(
            inner, new ChatBehaviorConfig(), CliServices.CreateToolInvocationPipeline(filter, approvalService: null, loggerFactory: null));

    private static IModeToolFilter FilterWith(PermissionAction verdict)
    {
        var filter = Substitute.For<IModeToolFilter>();
        filter.AssessRisk(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(verdict == PermissionAction.Allow
                ? RiskAssessment.Safe
                : RiskAssessment.Risky(RiskLevel.High, "not in this test", verdict: verdict));
        return filter;
    }

    /// <summary>Throws what M.E.AI's marshaller throws when a required parameter is missing.</summary>
    private sealed class MissingPathFunction : AIFunction
    {
        public override string Name => "read_file";

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            throw new ArgumentException(MarshallerMissingPathMessage, paramName: MarshallerParamName);
    }

    /// <summary>Answers the first request with one tool call and every later one with text; records tool results.</summary>
    private sealed class ScriptedChatClient(FunctionCallContent call) : IChatClient
    {
        private bool _called;

        public List<FunctionResultContent> ToolResults { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            ToolResults.AddRange(history.SelectMany(m => m.Contents).OfType<FunctionResultContent>());
            if (_called)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
            }

            _called = true;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
