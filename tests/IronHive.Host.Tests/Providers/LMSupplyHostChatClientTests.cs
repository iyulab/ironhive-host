using IronHive.Host.Providers;
using IronHive.Host.Tools;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using NSubstitute;

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
}
