using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Agent.Loop;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tests.Server;

/// <summary>
/// <see cref="TurnOptions.Instructions"/> is a second text channel beside the user's content: a client's own directives
/// reach the model as system text for one turn, never as user text and never in the conversation history.
/// </summary>
public class TurnInstructionsTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Instructions_MapToTheTurnsChatOptions()
    {
        var chat = TurnOptionsMapper.ToChatOptions(new TurnOptions(Instructions: "Answer in Korean."), [])!;

        Assert.Equal("Answer in Korean.", chat.Instructions);
        Assert.Null(chat.Tools);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoOrBlankInstructions_KeepTheLoopsValue(string? instructions)
    {
        var chat = TurnOptionsMapper.ToChatOptions(new TurnOptions(Instructions: instructions), [])!;

        Assert.Null(chat.Instructions);
    }

    [Fact]
    public void Instructions_TravelOnTheWire()
    {
        const string json = "{\"type\":\"user_message\",\"content\":\"new mail\",\"options\":{\"instructions\":\"Never move files out of the inbox.\"}}";

        var request = JsonSerializer.Deserialize<ServerRequest>(json, Wire);

        var message = Assert.IsType<UserMessageRequest>(request);
        Assert.Equal("new mail", message.Content);
        Assert.Equal("Never move files out of the inbox.", message.Options?.Instructions);
    }

    [Fact]
    public async Task Instructions_ReachTheModelForOneTurn_AndNeverTheHistory()
    {
        var model = new RecordingModel();
        var loop = new AgentLoop(model, new AgentOptions { SystemPrompt = "You are a file assistant." });
        var instructions = TurnOptionsMapper.ToChatOptions(new TurnOptions(Instructions: "Never move files out of the inbox."), []);

        await foreach (var _ in loop.RunStreamingAsync("Sort the new mail.", instructions, TestContext.Current.CancellationToken))
        {
        }

        await foreach (var _ in loop.RunStreamingAsync("Anything else?", null, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(2, model.Calls.Count);
        Assert.Equal("Never move files out of the inbox.", model.Calls[0].Instructions);
        Assert.Null(model.Calls[1].Instructions);
        Assert.All(model.Calls, call => Assert.DoesNotContain(call.Messages, m => m.Text.Contains("inbox", StringComparison.Ordinal)));
        Assert.Equal("You are a file assistant.", model.Calls[0].Messages.Single(m => m.Role == ChatRole.System).Text);
        Assert.DoesNotContain(loop.History, m => m.Text.Contains("inbox", StringComparison.Ordinal));
        Assert.Equal(["Sort the new mail.", "Anything else?"], loop.History.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
    }

    private sealed class RecordingModel : IChatClient
    {
        public List<(string? Instructions, List<ChatMessage> Messages)> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((options?.Instructions, messages.ToList()));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add((options?.Instructions, messages.ToList()));
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Done.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
