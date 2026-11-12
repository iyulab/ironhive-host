using AwesomeAssertions;
using IronHive.Agent.Invocation;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Host.Config;
using IronHive.Host.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// An MCP tool whose name a provider refuses (<c>message.send</c>) through the host's own chat pipeline: the provider is
/// offered a safe alias — the request that used to answer HTTP 400 — while the approval gate, the loop and the reported tool
/// calls keep the real name.
/// </summary>
public sealed class DottedToolNamePipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Refuses a request that offers a function name outside the providers' pattern, as an OpenAI-compatible endpoint does.</summary>
    private sealed class StrictProvider : IChatClient
    {
        public List<string> OfferedNames { get; } = [];

        public List<string> HistoryCallNames { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var offered = (options?.Tools ?? []).OfType<AIFunctionDeclaration>().Select(t => t.Name).ToList();
            OfferedNames.AddRange(offered);
            foreach (var name in offered.Where(n => !System.Text.RegularExpressions.Regex.IsMatch(n, "^[a-zA-Z0-9_-]{1,64}$")))
            {
                throw new HttpRequestException($"400: invalid 'tools[0].function.name': '{name}'");
            }

            var calls = messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
            HistoryCallNames.AddRange(calls.Select(c => c.Name));
            if (calls.Count == 0 && offered.Count > 0)
            {
                var call = new FunctionCallContent("call-1", offered[0], new Dictionary<string, object?> { ["to"] = "ann" });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls });
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Sent.")) { FinishReason = ChatFinishReason.Stop });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate(message.Role, message.Contents.ToList()) { FinishReason = response.FinishReason };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task ADottedTool_IsUsable_AndTheGateAndTheLoopSeeItsRealName()
    {
        var evaluated = new List<string>();
        var policy = Substitute.For<IToolCallPolicy>();
        policy.Evaluate(Arg.Do<string>(evaluated.Add), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new RiskAssessment { Verdict = PermissionAction.Allow });
        var ran = new List<string>();
        var provider = new StrictProvider();
        var client = HostChatPipeline.Decorate(
            provider,
            new ChatBehaviorConfig(),
            HostChatPipeline.CreateToolInvocationPipeline(policy, approvalService: null, loggerFactory: null));
        var loop = new AgentLoop(client);
        var tools = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string to) => { ran.Add(to); return "sent to " + to; }, "message.send")],
        };

        var response = await loop.RunAsync("tell ann", tools, Ct);

        var alias = ToolNameAliasChatClient.AliasFor("message.send");
        provider.OfferedNames.Should().OnlyContain(n => n == alias, "the provider never saw the dotted name");
        provider.HistoryCallNames.Should().Equal(alias);
        ran.Should().Equal("ann");
        evaluated.Should().Contain("message.send").And.NotContain(alias);
        response.ToolCalls.Should().ContainSingle().Which.ToolName.Should().Be("message.send");
        response.Content.Should().Contain("Sent.");
    }

    [Fact]
    public async Task ADottedTool_IsUsable_WhenStreaming_AndTheChunksCarryTheRealName()
    {
        var evaluated = new List<string>();
        var policy = Substitute.For<IToolCallPolicy>();
        policy.Evaluate(Arg.Do<string>(evaluated.Add), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new RiskAssessment { Verdict = PermissionAction.Allow });
        var ran = new List<string>();
        var provider = new StrictProvider();
        var client = HostChatPipeline.Decorate(
            provider,
            new ChatBehaviorConfig(),
            HostChatPipeline.CreateToolInvocationPipeline(policy, approvalService: null, loggerFactory: null));
        var loop = new AgentLoop(client);
        var tools = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string to) => { ran.Add(to); return "sent to " + to; }, "message.send")],
        };

        var chunks = new List<AgentResponseChunk>();
        await foreach (var chunk in loop.RunStreamingAsync("tell ann", tools, Ct))
        {
            chunks.Add(chunk);
        }

        var alias = ToolNameAliasChatClient.AliasFor("message.send");
        provider.OfferedNames.Should().OnlyContain(n => n == alias);
        provider.HistoryCallNames.Should().Equal([alias]);
        ran.Should().Equal("ann");
        evaluated.Should().Contain("message.send").And.NotContain(alias);
        chunks.Select(c => c.ToolCallDelta?.NameDelta).Where(n => n is not null).Should().OnlyContain(n => n == "message.send");
        chunks.Select(c => c.ToolResult?.ToolName).Where(n => n is not null).Should().ContainSingle().Which.Should().Be("message.send");
        string.Concat(chunks.Select(c => c.TextDelta)).Should().Contain("Sent.");
    }
}
