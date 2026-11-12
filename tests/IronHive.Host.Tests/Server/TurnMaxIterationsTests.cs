using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using IronHive.Agent.Loop;
using IronHive.Host.Config;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using IronHive.Host.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Server;

/// <summary>
/// <see cref="TurnOptions.MaxIterations"/>: a client bounds one turn's tool rounds below the process default without touching
/// the shared chat client - the agent's configured cap stays the ceiling and the next turn is back on it.
/// </summary>
public class TurnMaxIterationsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public void MaxIterations_MapsToTheTurnsCap_AndUnsetLeavesNone()
    {
        var capped = TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: 3), [])!;
        var uncapped = TurnOptionsMapper.ToChatOptions(new TurnOptions(Temperature: 0.2f), [])!;

        TurnLimits.MaxIterationsOf(capped).Should().Be(3);
        TurnLimits.MaxIterationsOf(uncapped).Should().BeNull("an unset field keeps the loop's value");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AValueBelowOne_IsAnError_NotASilentDrop(int value)
    {
        var act = () => TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: value), []);

        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("max_iterations").And.Contain("at least 1");
    }

    [Fact]
    public void MaxIterations_TravelsOnTheWire()
    {
        const string json = "{\"type\":\"user_message\",\"content\":\"quick lookup\",\"options\":{\"max_iterations\":2}}";

        var request = JsonSerializer.Deserialize<ServerRequest>(json, Wire);

        request.Should().BeOfType<UserMessageRequest>().Which.Options!.MaxIterations.Should().Be(2);
        JsonSerializer.Serialize(new TurnOptions(), Wire).Should().Contain("\"max_iterations\":null",
            "the record is the wire form; a request that omits the field reads null");
    }

    /// <summary>Calls <c>write_note</c> (with fresh arguments, so no loop guard fires) whenever it is offered; with no tools it answers
    /// - or, when <paramref name="textCallAfterTheCap"/>, writes a call to it as text, the way a local model whose server missed the call does.</summary>
    private sealed class NoteTaker(bool textCallAfterTheCap = false) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (options?.Tools?.Any(t => t.Name == "write_note") == true)
            {
                var call = new FunctionCallContent(Guid.NewGuid().ToString("N"), "write_note", new Dictionary<string, object?> { ["text"] = Guid.NewGuid().ToString("N") });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls });
            }

            var text = textCallAfterTheCap
                ? "One more note.\n<tool_call>\n{\"name\": \"write_note\", \"arguments\": {\"text\": \"late\"}}\n</tool_call>"
                : "out of rounds";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static (AgentLoop Loop, List<AITool> Registered, Func<int> Runs) Host(bool textCallAfterTheCap = false, int processCap = 10)
    {
        var runs = 0;
        List<AITool> registered = [AIFunctionFactory.Create((string text) => { Interlocked.Increment(ref runs); return "noted " + text; }, "write_note")];
        var client = HostChatPipeline.Decorate(
            new NoteTaker(textCallAfterTheCap),
            new ChatBehaviorConfig { MaximumIterationsPerRequest = processCap },
            HostChatPipeline.CreateToolInvocationPipeline(policy: null));
        return (new AgentLoop(client, new AgentOptions { Tools = registered }), registered, () => Volatile.Read(ref runs));
    }

    [Fact]
    public async Task ATurnWithMaxIterations_EndsAtItsCap_AndTheNextTurnRunsToTheProcessCap()
    {
        var (loop, registered, runs) = Host();

        var capped = await loop.RunAsync("quick lookup", TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: 2), registered), Ct);
        var runsAfterCapped = runs();
        var uncapped = await loop.RunAsync("long task", TurnOptionsMapper.ToChatOptions(null, registered), Ct);

        capped.ToolCalls.Should().HaveCount(2);
        capped.StopReason.Should().Be(TurnStopReason.StepLimit);
        runsAfterCapped.Should().Be(2);
        uncapped.ToolCalls.Should().HaveCount(10);
        uncapped.StopReason.Should().Be(TurnStopReason.StepLimit);
    }

    [Fact]
    public async Task AValueAboveTheProcessCap_DoesNotRaiseIt()
    {
        var (loop, registered, _) = Host(processCap: 3);

        var turn = await loop.RunAsync("task", TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: 50), registered), Ct);

        turn.ToolCalls.Should().HaveCount(3, "the agent's configured cap is the ceiling");
        turn.StopReason.Should().Be(TurnStopReason.StepLimit);
    }

    [Fact]
    public async Task TwoTurnsAtTheSameTime_KeepTheirOwnCaps()
    {
        var runs = 0;
        List<AITool> registered = [AIFunctionFactory.Create((string text) => { Interlocked.Increment(ref runs); return "noted " + text; }, "write_note")];
        var client = HostChatPipeline.Decorate(new NoteTaker(), new ChatBehaviorConfig { MaximumIterationsPerRequest = 10 },
            HostChatPipeline.CreateToolInvocationPipeline(policy: null));
        var first = new AgentLoop(client, new AgentOptions { Tools = registered });
        var second = new AgentLoop(client, new AgentOptions { Tools = registered });

        for (var i = 0; i < 10; i++)
        {
            var turns = await Task.WhenAll(
                first.RunAsync("quick", TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: 1), registered), Ct),
                second.RunAsync("long", TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: 4), registered), Ct));

            turns[0].ToolCalls.Should().HaveCount(1);
            turns[1].ToolCalls.Should().HaveCount(4);
        }
    }

    // The server runner end to end: the request line as a client writes it, the handler taking the three steps `run --server`
    // takes (map the options, run the turn streamed, turn the chunks into events), the events as the client reads them.
    private static async Task<List<ServerEvent>> Serve(AgentLoop loop, List<AITool> registered, params string[] requestLines)
    {
        async IAsyncEnumerable<ServerEvent> Process(UserMessageRequest message, [EnumeratorCancellation] CancellationToken ct)
        {
            var overrideOptions = TurnOptionsMapper.ToChatOptions(message.Options, registered);
            await foreach (var evt in loop.RunStreamingAsync(message.Content, overrideOptions, ct).ToServerEvents(ct: ct))
            {
                yield return evt;
            }
        }

        var runner = new AgentServerRunner(Process, Substitute.For<ILogger<AgentServerRunner>>(), Wire);
        using var input = new StringReader(string.Join('\n', requestLines.Append("{\"type\":\"shutdown\"}")));
        using var output = new StringWriter();

        await runner.RunAsync(input, output, Ct);

        return [.. output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonSerializer.Deserialize<ServerEvent>(l, Wire)!)];
    }

    [Fact]
    public async Task OverTheWire_MaxIterationsEndsTheTurnAtItsCap_AndTheNextRequestIsBackOnTheDefault()
    {
        var (loop, registered, runs) = Host();

        var events = await Serve(loop, registered,
            "{\"type\":\"user_message\",\"content\":\"quick lookup\",\"options\":{\"max_iterations\":2}}",
            "{\"type\":\"user_message\",\"content\":\"long task\"}");

        var ends = events.OfType<TurnEndEvent>().ToList();
        ends.Should().HaveCount(2);
        ends[0].StopReason.Should().Be("step_limit");
        ends[1].StopReason.Should().Be("step_limit");
        runs().Should().Be(2 + 10, "the first request ran two rounds, the second the agent's cap of ten");
        events.OfType<ErrorEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task OverTheWire_AMaxIterationsBelowOne_IsAnErrorEvent_AndNothingRuns()
    {
        var (loop, registered, runs) = Host();

        var events = await Serve(loop, registered,
            "{\"type\":\"user_message\",\"content\":\"quick lookup\",\"options\":{\"max_iterations\":0}}");

        events.OfType<ErrorEvent>().Should().ContainSingle().Which.Message.Should().Contain("max_iterations");
        events.Should().ContainSingle(e => e is TurnEndEvent);
        runs().Should().Be(0);
    }

    // The cap withdraws the tools for the request that follows its last round. If the turn-limits step sat below the
    // text-call recovery step, recovery would still see the tools and turn this late call into a real one, a round past the cap.
    [Fact]
    public async Task ACallWrittenAsTextAfterTheCap_IsNotRecoveredIntoARound()
    {
        var (loop, registered, runs) = Host(textCallAfterTheCap: true);

        var turn = await loop.RunAsync("quick lookup", TurnOptionsMapper.ToChatOptions(new TurnOptions(MaxIterations: 2), registered), Ct);

        runs().Should().Be(2);
        turn.ToolCalls.Should().HaveCount(2);
        turn.StopReason.Should().Be(TurnStopReason.StepLimit);
    }
}
