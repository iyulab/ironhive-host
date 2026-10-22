using IronHive.Host.Providers;
using IronHive.Host.Tools;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using NSubstitute;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronHive.Host.Tests.Providers;

/// <summary>
/// The client the LMSupply provider hands out is LMSupply's own chat client plus the model's context window, which a
/// wrapping <see cref="TokenBudgetChatClient"/> reads to size its budget.
/// </summary>
public class LMSupplyHostChatClientTests
{
    [Fact]
    public void Answers_the_context_size_from_the_loaded_model()
    {
        var model = Substitute.For<IGeneratorModel>();
        model.MaxContextLength.Returns(32768);
        using var client = new LMSupplyHostChatClient(model);

        var provider = client.GetService(typeof(IContextSizeProvider)) as IContextSizeProvider;

        Assert.NotNull(provider);
        Assert.Equal(32768, provider.MaxContextTokens);
    }

    [Fact]
    public void Says_nothing_about_the_context_size_when_the_model_reports_none()
    {
        var model = Substitute.For<IGeneratorModel>();
        model.MaxContextLength.Returns(0);
        using var client = new LMSupplyHostChatClient(model);

        Assert.Null(client.GetService(typeof(IContextSizeProvider)));
    }

    [Fact]
    public void Hands_out_the_model_and_does_not_dispose_it()
    {
        var model = Substitute.For<IGeneratorModel>();
        var client = new LMSupplyHostChatClient(model);

        Assert.Same(model, client.GetService(typeof(IGeneratorModel)));
        client.Dispose();
        _ = model.DidNotReceive().DisposeAsync().AsTask();
    }

    // The CLI sends the id it configured (an alias) as ChatOptions.ModelId; LMSupply's client answers only to the model's
    // own id. Measured: the published-set E2E's adapter fact failed with «This client answers with model 'Gemma 4 E4B
    // Instruct'; the request names 'gguf:gemma4-default'».
    [Fact]
    public async Task A_request_naming_the_id_the_model_was_loaded_under_reaches_the_model()
    {
        var model = Answering("Gemma 4 E4B Instruct");
        using var client = new LMSupplyHostChatClient(model, loadedAs: "gguf:gemma4-default");

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")], new ChatOptions { ModelId = "gguf:gemma4-default" },
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public async Task A_request_naming_another_model_is_still_refused()
    {
        var model = Answering("Gemma 4 E4B Instruct");
        using var client = new LMSupplyHostChatClient(model, loadedAs: "gguf:gemma4-default");

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")], new ChatOptions { ModelId = "gguf:some-other-model" },
            TestContext.Current.CancellationToken));
    }

    private static IGeneratorModel Answering(string modelId)
    {
        var model = Substitute.For<IGeneratorModel>();
        model.ModelId.Returns(modelId);
        model.GenerateChatWithToolsAsync(
                Arg.Any<IEnumerable<LmChatMessage>>(), Arg.Any<LmGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LmChatCompletionResult { Content = "ok", FinishReason = "stop" }));
        return model;
    }
}
