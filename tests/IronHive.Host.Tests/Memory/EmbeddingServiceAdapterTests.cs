using IronHive.Agent.Memory;
using NSubstitute;

namespace IronHive.Host.Tests.Memory;

public class EmbeddingServiceAdapterTests
{
    private static readonly float[] SingleEmbedding = [1.0f];

    [Fact]
    public void Constructor_NullProvider_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EmbeddingServiceAdapter(null!));
    }

    [Fact]
    public void Dimensions_ShouldDelegateToProvider()
    {
        var provider = Substitute.For<IAgentEmbeddingProvider>();
        provider.Dimensions.Returns(384);

        var adapter = new EmbeddingServiceAdapter(provider);

        Assert.Equal(384, adapter.Dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ShouldReturnWrappedResult()
    {
        var expected = new float[] { 0.1f, 0.2f, 0.3f };
        var provider = Substitute.For<IAgentEmbeddingProvider>();
        provider.EmbedAsync("test text", Arg.Any<CancellationToken>())
            .Returns(expected);

        var adapter = new EmbeddingServiceAdapter(provider);

        var result = await adapter.GenerateEmbeddingAsync("test text");

        Assert.Equal(3, result.Length);
        Assert.Equal(0.1f, result.Span[0]);
        Assert.Equal(0.2f, result.Span[1]);
        Assert.Equal(0.3f, result.Span[2]);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ShouldCallProviderEmbedAsync()
    {
        var provider = Substitute.For<IAgentEmbeddingProvider>();
        provider.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleEmbedding);

        var adapter = new EmbeddingServiceAdapter(provider);

        await adapter.GenerateEmbeddingAsync("query text");

        await provider.Received(1).EmbedAsync("query text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_ShouldReturnWrappedResults()
    {
        IReadOnlyList<float[]> batch =
        [
            [0.1f, 0.2f],
            [0.3f, 0.4f],
            [0.5f, 0.6f]
        ];
        var provider = Substitute.For<IAgentEmbeddingProvider>();
        provider.EmbedBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(batch);

        var adapter = new EmbeddingServiceAdapter(provider);

        var results = await adapter.GenerateBatchEmbeddingsAsync(["text1", "text2", "text3"]);

        Assert.Equal(3, results.Count);
        Assert.Equal(0.1f, results[0].Span[0]);
        Assert.Equal(0.3f, results[1].Span[0]);
        Assert.Equal(0.5f, results[2].Span[0]);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_ShouldPreserveOrder()
    {
        IReadOnlyList<float[]> batch = [[1.0f], [2.0f], [3.0f]];
        var provider = Substitute.For<IAgentEmbeddingProvider>();
        provider.EmbedBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(batch);

        var adapter = new EmbeddingServiceAdapter(provider);

        var results = await adapter.GenerateBatchEmbeddingsAsync(["a", "b", "c"]);

        Assert.Equal(1.0f, results[0].Span[0]);
        Assert.Equal(2.0f, results[1].Span[0]);
        Assert.Equal(3.0f, results[2].Span[0]);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_ShouldCallProviderWithTexts()
    {
        IReadOnlyList<float[]> batch = [[1.0f], [2.0f]];
        var provider = Substitute.For<IAgentEmbeddingProvider>();
        provider.EmbedBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(batch);

        var adapter = new EmbeddingServiceAdapter(provider);

        await adapter.GenerateBatchEmbeddingsAsync(["text1", "text2"]);

        await provider.Received(1).EmbedBatchAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<CancellationToken>());
    }
}
