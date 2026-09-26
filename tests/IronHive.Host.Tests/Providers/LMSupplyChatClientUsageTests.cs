using System.Runtime.CompilerServices;
using AwesomeAssertions;
using IronHive.Host.Providers;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatStreamChunk = LMSupply.Generator.Models.ChatStreamChunk;
using LmChatTokenUsage = LMSupply.Generator.Models.ChatTokenUsage;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;
using LmGenerationTimings = LMSupply.Generator.Models.GenerationTimings;

namespace IronHive.Host.Tests.Providers;

/// <summary>
/// The backend's token count must reach <see cref="ChatResponse.Usage"/> on both paths — the non-streaming
/// result carries it, and the streaming path carries it on the final chunk (as <see cref="UsageContent"/>).
/// </summary>
public class LMSupplyChatClientUsageTests
{
    private static readonly LmChatTokenUsage Usage = new() { PromptTokens = 12, CompletionTokens = 480, TotalTokens = 492 };

    [Fact]
    public async Task NonStreaming_response_reports_backend_usage_and_model()
    {
        var generator = Substitute.For<ITextGenerator>();
        generator.ModelId.Returns("test:stub");
        generator.GenerateChatWithToolsAsync(Arg.Any<IEnumerable<LmChatMessage>>(), Arg.Any<LmGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LmChatCompletionResult { Content = "ok", FinishReason = "stop", Usage = Usage }));
        using var client = new LMSupplyChatClient(generator);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        response.ModelId.Should().Be("test:stub");
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokenCount.Should().Be(12);
        response.Usage.OutputTokenCount.Should().Be(480);
        response.Usage.TotalTokenCount.Should().Be(492);
    }

    [Fact]
    public async Task Streaming_final_chunk_usage_reaches_the_response()
    {
        using var client = new LMSupplyChatClient(StreamingGenerator(
            new LmChatStreamChunk { Text = "ok" },
            new LmChatStreamChunk { FinishReason = "stop", Usage = Usage }));

        var updates = await Collect(client);
        var response = updates.ToChatResponse();

        response.Text.Should().Be("ok");
        response.FinishReason.Should().Be(ChatFinishReason.Stop);
        response.Usage.Should().NotBeNull();
        response.Usage!.OutputTokenCount.Should().Be(480);
    }

    // Prompt tokens the server reused from its prompt cache are counted in the input but were not evaluated;
    // M.E.AI has a slot for them, and the server reports them in its timings (cache_n).
    [Fact]
    public async Task Cached_prompt_tokens_reach_CachedInputTokenCount_on_both_paths()
    {
        var timings = new LmGenerationTimings { CachedPromptTokens = 9, PromptTokensEvaluated = 3 };
        var generator = Substitute.For<ITextGenerator>();
        generator.ModelId.Returns("test:stub");
        generator.GenerateChatWithToolsAsync(Arg.Any<IEnumerable<LmChatMessage>>(), Arg.Any<LmGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LmChatCompletionResult { Content = "ok", FinishReason = "stop", Usage = Usage, Timings = timings }));
        using var nonStreaming = new LMSupplyChatClient(generator);
        using var streaming = new LMSupplyChatClient(StreamingGenerator(
            new LmChatStreamChunk { Text = "ok" },
            new LmChatStreamChunk { FinishReason = "stop", Usage = Usage, Timings = timings }));

        var response = await nonStreaming.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);
        var streamed = (await Collect(streaming)).ToChatResponse();

        response.Usage!.CachedInputTokenCount.Should().Be(9);
        streamed.Usage!.CachedInputTokenCount.Should().Be(9);
        response.Usage.InputTokenCount.Should().Be(12, "the input count still includes the cached tokens");
    }

    [Fact]
    public async Task Without_server_timings_the_cached_count_is_unknown_not_zero()
    {
        using var client = new LMSupplyChatClient(StreamingGenerator(
            new LmChatStreamChunk { Text = "ok" },
            new LmChatStreamChunk { FinishReason = "stop", Usage = Usage }));

        var response = (await Collect(client)).ToChatResponse();

        response.Usage!.CachedInputTokenCount.Should().BeNull();
    }

    [Fact]
    public async Task Streaming_without_backend_usage_reports_none()
    {
        using var client = new LMSupplyChatClient(StreamingGenerator(
            new LmChatStreamChunk { Text = "ok" },
            new LmChatStreamChunk { FinishReason = "stop" }));

        var updates = await Collect(client);

        updates.ToChatResponse().Usage.Should().BeNull();
        updates[^1].Contents.Should().BeEmpty();
    }

    private static async Task<List<ChatResponseUpdate>> Collect(LMSupplyChatClient client)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(u);
        }
        return updates;
    }

    private static ITextGenerator StreamingGenerator(params LmChatStreamChunk[] chunks)
    {
        var generator = Substitute.For<ITextGenerator>();
        generator.ModelId.Returns("test:stub");
        generator.GenerateChatStreamAsync(Arg.Any<IEnumerable<LmChatMessage>>(), Arg.Any<LmGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Stream(chunks));
        return generator;
    }

    private static async IAsyncEnumerable<LmChatStreamChunk> Stream(
        LmChatStreamChunk[] chunks, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var c in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return c;
        }
        await Task.CompletedTask;
    }
}
