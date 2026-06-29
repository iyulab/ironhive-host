using IronHive.Host.Core.Providers;
using LMSupply.Generator.Abstractions;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatRole = LMSupply.Generator.Models.ChatRole;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronHive.Host.Tests.Providers;

/// <summary>
/// Regression coverage for ISSUE-ironhive-cli-lmsupplychatclient-instructions-dropped-20260429-000000:
/// <see cref="ChatOptions.Instructions"/> must be injected into the LMSupply prompt as a leading
/// System message instead of being silently discarded.
/// </summary>
public class LMSupplyChatClientInstructionsTests
{
    private static ITextGenerator BuildStubGenerator(out List<List<LmChatMessage>> capturedMessages)
    {
        var captured = new List<List<LmChatMessage>>();
        capturedMessages = captured;

        var generator = Substitute.For<ITextGenerator>();
        generator.ModelId.Returns("test:stub");
        generator.GenerateChatWithToolsAsync(
                Arg.Any<IEnumerable<LmChatMessage>>(),
                Arg.Any<LmGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var messages = call.Arg<IEnumerable<LmChatMessage>>();
                captured.Add(messages.ToList());
                return Task.FromResult(new LmChatCompletionResult
                {
                    Content = "ok",
                    FinishReason = "stop"
                });
            });
        return generator;
    }

    [Fact]
    public async Task GetResponseAsync_WithInstructions_PrependsSystemMessage()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { Instructions = "You are a terse assistant." };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        Assert.Single(captured);
        var sent = captured[0];
        Assert.True(sent.Count >= 2, "Expected at least the injected System and the User message");
        Assert.Equal(LmChatRole.System, sent[0].Role);
        Assert.Equal("You are a terse assistant.", sent[0].Content);
    }

    [Fact]
    public async Task GetResponseAsync_WithoutInstructions_DoesNotInjectSystem()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions();

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        Assert.Single(captured);
        var sent = captured[0];
        Assert.Single(sent);
        Assert.Equal(LmChatRole.User, sent[0].Role);
    }

    [Fact]
    public async Task GetResponseAsync_NullOptions_DoesNotInjectSystem()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options: null);

        Assert.Single(captured);
        var sent = captured[0];
        Assert.Single(sent);
        Assert.Equal(LmChatRole.User, sent[0].Role);
    }

    [Fact]
    public async Task GetResponseAsync_InstructionsAndExplicitSystem_BothEmitted()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { Instructions = "Persona prompt" };
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "Background context"),
            new ChatMessage(ChatRole.User, "Hi")
        };

        await client.GetResponseAsync(messages, options);

        var sent = captured[0];
        Assert.Equal(3, sent.Count);
        Assert.Equal(LmChatRole.System, sent[0].Role);
        Assert.Equal("Persona prompt", sent[0].Content);
        Assert.Equal(LmChatRole.System, sent[1].Role);
        Assert.Equal("Background context", sent[1].Content);
        Assert.Equal(LmChatRole.User, sent[2].Role);
    }
}
