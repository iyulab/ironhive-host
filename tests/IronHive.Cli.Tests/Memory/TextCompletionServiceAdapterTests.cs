using IronHive.Cli.Core.Memory;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Cli.Tests.Memory;

public class TextCompletionServiceAdapterTests
{
    [Fact]
    public void Constructor_NullChatClient_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TextCompletionServiceAdapter(null!));
    }

    [Fact]
    public async Task CompleteAsync_ShouldCallGetResponseWithUserMessage()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "response text")));

        var adapter = new TextCompletionServiceAdapter(chatClient);

        var result = await adapter.CompleteAsync("test prompt");

        Assert.Equal("response text", result);
        await chatClient.Received(1).GetResponseAsync(
            Arg.Is<IList<ChatMessage>>(msgs =>
                msgs.Count == 1 && msgs[0].Role == ChatRole.User && msgs[0].Text == "test prompt"),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_NullResponseText_ShouldReturnEmpty()
    {
        var chatClient = Substitute.For<IChatClient>();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null));
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);

        var adapter = new TextCompletionServiceAdapter(chatClient);

        var result = await adapter.CompleteAsync("prompt");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_NullOptions_ShouldPassNullChatOptions()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var adapter = new TextCompletionServiceAdapter(chatClient);

        await adapter.CompleteAsync("prompt", options: null);

        await chatClient.Received(1).GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WithOptions_ShouldMapCorrectly()
    {
        ChatOptions? capturedOptions = null;
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOptions = callInfo.ArgAt<ChatOptions?>(1);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
            });

        var adapter = new TextCompletionServiceAdapter(chatClient);
        var options = new TextCompletionOptions
        {
            Temperature = 0.5f,
            MaxTokens = 100,
            TopP = 0.9f,
            PresencePenalty = 0.1f,
            FrequencyPenalty = 0.2f,
            StopSequences = ["END", "STOP"]
        };

        await adapter.CompleteAsync("prompt", options);

        Assert.NotNull(capturedOptions);
        Assert.Equal(0.5f, capturedOptions!.Temperature);
        Assert.Equal(100, capturedOptions.MaxOutputTokens);
        Assert.Equal(0.9f, capturedOptions.TopP);
        Assert.Equal(0.1f, capturedOptions.PresencePenalty);
        Assert.Equal(0.2f, capturedOptions.FrequencyPenalty);
        Assert.Equal(2, capturedOptions.StopSequences!.Count);
        Assert.Contains("END", capturedOptions.StopSequences);
    }

    [Fact]
    public async Task CompleteBatchAsync_ShouldCallPerPrompt()
    {
        var callCount = 0;
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                var prompt = callInfo.ArgAt<IList<ChatMessage>>(0)[0].Text;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"response-{prompt}"));
            });

        var adapter = new TextCompletionServiceAdapter(chatClient);

        var results = await adapter.CompleteBatchAsync(["p1", "p2", "p3"]);

        Assert.Equal(3, results.Count);
        Assert.Equal("response-p1", results[0]);
        Assert.Equal("response-p2", results[1]);
        Assert.Equal("response-p3", results[2]);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task CompleteBatchAsync_EmptyInput_ShouldReturnEmpty()
    {
        var chatClient = Substitute.For<IChatClient>();
        var adapter = new TextCompletionServiceAdapter(chatClient);

        var results = await adapter.CompleteBatchAsync([]);

        Assert.Empty(results);
        await chatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }
}
