using IronHive.Host.Providers;
using LMSupply.Generator.Abstractions;
using NSubstitute;
using AdditionalPropertiesDictionary = Microsoft.Extensions.AI.AdditionalPropertiesDictionary;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronHive.Host.Tests.Providers;

/// <summary>
/// Regression coverage for the Surface B sampler-probe path
/// (ecosystem ISSUE 2026-05-01 silent-emit floor):
/// the standard M.E.AI <see cref="ChatOptions"/> sampler properties
/// (Temperature/TopP/TopK/FrequencyPenalty/PresencePenalty/Seed/StopSequences)
/// must be forwarded to <see cref="LmGenerationOptions"/> so consumers can
/// probe alternate decoding parameters from the chat-pipeline boundary
/// without having to bypass M.E.AI.
/// </summary>
public class LMSupplyChatClientSamplerForwardingTests
{
    private static ITextGenerator BuildStubGenerator(out List<LmGenerationOptions?> capturedOptions)
    {
        var captured = new List<LmGenerationOptions?>();
        capturedOptions = captured;

        var generator = Substitute.For<ITextGenerator>();
        generator.ModelId.Returns("test:stub");
        generator.GenerateChatWithToolsAsync(
                Arg.Any<IEnumerable<LmChatMessage>>(),
                Arg.Any<LmGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured.Add(call.Arg<LmGenerationOptions?>());
                return Task.FromResult(new LmChatCompletionResult
                {
                    Content = "ok",
                    FinishReason = "stop"
                });
            });
        return generator;
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsTopPAndTopK()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { TopP = 0.95f, TopK = 40 };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        Assert.Single(captured);
        var sent = captured[0];
        Assert.NotNull(sent);
        Assert.Equal(0.95f, sent!.TopP);
        Assert.Equal(40, sent.TopK);
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsFrequencyAndPresencePenalties()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { FrequencyPenalty = 0.3f, PresencePenalty = 0.5f };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(0.3f, sent.FrequencyPenalty);
        Assert.Equal(0.5f, sent.PresencePenalty);
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsSeedAndStopSequences()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions
        {
            Seed = 42,
            StopSequences = ["</answer>", "###"]
        };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(42, sent.Seed);
        Assert.NotNull(sent.StopSequences);
        Assert.Equal(2, sent.StopSequences!.Count);
        Assert.Equal("</answer>", sent.StopSequences[0]);
        Assert.Equal("###", sent.StopSequences[1]);
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsTemperatureAndMaxTokens()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { Temperature = 0.7f, MaxOutputTokens = 256 };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(0.7f, sent.Temperature);
        Assert.Equal(256, sent.MaxTokens);
    }

    [Fact]
    public async Task GetResponseAsync_NullOptions_DoesNotPopulateGenOptions()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options: null);

        Assert.Single(captured);
        Assert.Null(captured[0]);
    }

    [Fact]
    public async Task GetResponseAsync_UnsetSamplerProperties_LeaveLmDefaults()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { Temperature = 0.7f };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        // Temperature was set; everything else stays at LmGenerationOptions defaults
        // so the wider lm-supply default sampler config keeps applying server-side.
        Assert.Equal(0.7f, sent.Temperature);
        Assert.Equal(0.9f, sent.TopP);
        Assert.Equal(50, sent.TopK);
        Assert.Equal(0f, sent.FrequencyPenalty);
        Assert.Equal(0f, sent.PresencePenalty);
        Assert.Equal(-1, sent.Seed);
        Assert.Null(sent.StopSequences);
    }

    // ---------------------------------------------------------------
    // AdditionalProperties opt-in plumbing (cli 0.10.7) — RepetitionPenalty
    // and MinP are llama.cpp / hf-tgi family sampler params with no standard
    // M.E.AI surface, so cli forwards them via the AdditionalProperties bag
    // using snake_case keys ("repetition_penalty", "min_p") that match
    // lm-supply native property names.
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetResponseAsync_ForwardsRepetitionPenaltyFromAdditionalProperties()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["repetition_penalty"] = 1.0f
            }
        };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(1.0f, sent.RepetitionPenalty);
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsMinPFromAdditionalProperties()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["min_p"] = 0.0f
            }
        };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(0.0f, sent.MinP);
    }

    [Fact]
    public async Task GetResponseAsync_AcceptsRepetitionPenaltyAsDouble()
    {
        // JSON deserialization typically yields double, not float;
        // helper must coerce safely.
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["repetition_penalty"] = 1.0d,
                ["min_p"] = 0.02d
            }
        };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(1.0f, sent.RepetitionPenalty);
        Assert.Equal(0.02f, sent.MinP, precision: 4);
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsBothRepetitionPenaltyAndMinPJointly()
    {
        // Joint coverage: independent forwarding is exercised above, but the
        // typical Surface B probe sets both keys in a single ChatOptions call.
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["repetition_penalty"] = 1.0f,
                ["min_p"] = 0.0f
            }
        };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(1.0f, sent.RepetitionPenalty);
        Assert.Equal(0.0f, sent.MinP);
    }

    [Fact]
    public async Task GetResponseAsync_EmptyAdditionalProperties_LeaveLmDefaults()
    {
        // When the bag is absent or empty the cli must not overwrite
        // lm-supply native defaults (RepetitionPenalty=1.1, MinP=0.05),
        // so existing consumers see no behavior change from cli 0.10.6.
        var generator = BuildStubGenerator(out var captured);
        var client = new LMSupplyChatClient(generator);
        var options = new ChatOptions { Temperature = 0.7f };

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            options);

        var sent = captured[0]!;
        Assert.Equal(1.1f, sent.RepetitionPenalty);
        Assert.Equal(0.05f, sent.MinP);
    }
}
