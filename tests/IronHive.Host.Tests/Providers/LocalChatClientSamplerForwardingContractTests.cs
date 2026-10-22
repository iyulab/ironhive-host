using IronHive.Host.Providers;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatOptions = Microsoft.Extensions.AI.ChatOptions;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronHive.Host.Tests.Providers;

/// <summary>
/// Regression coverage for sampler forwarding:
/// the standard M.E.AI <see cref="ChatOptions"/> sampler properties
/// (Temperature/TopP/TopK/FrequencyPenalty/PresencePenalty/Seed/StopSequences)
/// must be forwarded to <see cref="LmGenerationOptions"/> so consumers can
/// probe alternate decoding parameters from the chat-pipeline boundary
/// without having to bypass M.E.AI.
/// </summary>
public class LocalChatClientSamplerForwardingContractTests
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
    public async Task GetResponseAsync_OffersDeclarationOnlyToolsNextToFunctions()
    {
        // A host-executed tool is declared without an implementation; it must reach the model like any other tool.
        using var schema = System.Text.Json.JsonDocument.Parse(
            """{"type":"object","properties":{"tab":{"type":"string"}},"required":["tab"]}""");
        var declaration = Microsoft.Extensions.AI.AIFunctionFactory.CreateDeclaration(
            "read_page", "Reads one open tab's text.", schema.RootElement.Clone());
        var function = Microsoft.Extensions.AI.AIFunctionFactory.Create((string tab) => "tabs", "list_tabs");
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "Hi") },
            new ChatOptions { Tools = [declaration, function] },
            TestContext.Current.CancellationToken);

        var tools = captured[0]!.Tools!;
        Assert.Equal(["read_page", "list_tabs"], tools.Select(t => t.Name));
        Assert.Equal("tab", tools[0].Parameters!.Value.GetProperty("required")[0].GetString());
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsTopPAndTopK()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();
        var options = new ChatOptions { TopP = 0.95f, TopK = 40 };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

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
        var client = generator.AsChatClient();
        var options = new ChatOptions { FrequencyPenalty = 0.3f, PresencePenalty = 0.5f };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

        var sent = captured[0]!;
        Assert.Equal(0.3f, sent.FrequencyPenalty);
        Assert.Equal(0.5f, sent.PresencePenalty);
    }

    [Fact]
    public async Task GetResponseAsync_ForwardsSeedAndStopSequences()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();
        var options = new ChatOptions
        {
            Seed = 42,
            StopSequences = ["</answer>", "###"]
        };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

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
        var client = generator.AsChatClient();
        var options = new ChatOptions { Temperature = 0.7f, MaxOutputTokens = 256 };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

        var sent = captured[0]!;
        Assert.Equal(0.7f, sent.Temperature);
        Assert.Equal(256, sent.MaxNewTokens);
    }

    [Fact]
    public async Task GetResponseAsync_NullOptions_SendsLmDefaults()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options: null, cancellationToken: TestContext.Current.CancellationToken);

        var sent = Assert.Single(captured)!;
        Assert.Null(sent.MaxNewTokens);
        Assert.Null(sent.Tools);
    }

    [Fact]
    public async Task GetResponseAsync_UnsetSamplerProperties_LeaveLmDefaults()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();
        var options = new ChatOptions { Temperature = 0.7f };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

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

    // RepetitionPenalty and MinP have no standard M.E.AI name; LMSupply's chat client takes them from
    // ChatOptions.RawRepresentationFactory returning its GenerationOptions, with the standard options applied over them.
    [Fact]
    public async Task GetResponseAsync_TakesRepetitionPenaltyAndMinPFromTheRawRepresentation()
    {
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();
        var options = new ChatOptions
        {
            Temperature = 0.3f,
            RawRepresentationFactory = _ => new LmGenerationOptions { RepetitionPenalty = 1.0f, MinP = 0.0f },
        };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

        var sent = captured[0]!;
        Assert.Equal(1.0f, sent.RepetitionPenalty);
        Assert.Equal(0.0f, sent.MinP);
        Assert.Equal(0.3f, sent.Temperature);
    }

    [Fact]
    public async Task GetResponseAsync_NoRawRepresentation_LeavesLmDefaults()
    {
        // Without a raw representation the lm-supply defaults (RepetitionPenalty=1.1, MinP=0.05) stand.
        var generator = BuildStubGenerator(out var captured);
        var client = generator.AsChatClient();
        var options = new ChatOptions { Temperature = 0.7f };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hi") }, options, TestContext.Current.CancellationToken);

        var sent = captured[0]!;
        Assert.Equal(1.1f, sent.RepetitionPenalty);
        Assert.Equal(0.05f, sent.MinP);
    }
}
