using System.Net;
using System.Text;
using IronHive.Host.Config;
using IronHive.Host.Providers;

namespace IronHive.Host.Tests.Providers;

/// <summary>
/// The GpuStack providers' failure messages must tell the operator which setting to change (the
/// config.yaml key and its environment variable) or which endpoint answered unexpectedly. These tests
/// pin the handles named in each message, not the full wording, so the text can still be improved.
/// </summary>
public class GpuStackProviderMessagesTests
{
    private static GpuStackConfig Configured(string? embeddingModel = null, string? rerankModel = null) => new()
    {
        Endpoint = "http://gpustack.test:8080",
        ApiKey = "test-key",
        Model = "chat-model",
        EmbeddingModel = embeddingModel,
        RerankModel = rerankModel
    };

    private static HttpClient RespondingWith(string body) =>
        new(new FixedResponseHandler(body));

    [Fact]
    public async Task Embedding_EndpointNotConfigured_NamesEndpointKeysAndEnvironmentVariables()
    {
        // With no endpoint/key/model at all, the embedding model handle is still named so the operator
        // learns the whole set of settings in one message. (A configured endpoint always has a chat
        // model, which doubles as the embedding fallback, so "configured but no embedding model" cannot
        // occur for this provider.)
        using var provider = new GpuStackEmbeddingProvider(new GpuStackConfig());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("text", TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("gpustack.endpoint", ex.Message);
        Assert.Contains("gpustack.api_key", ex.Message);
        Assert.Contains("GPUSTACK_ENDPOINT", ex.Message);
        Assert.Contains("gpustack.embedding_model", ex.Message);
        Assert.Contains("GPUSTACK_EMBEDDING_MODEL", ex.Message);
    }

    [Fact]
    public async Task Embedding_ResponseWithoutData_NamesEndpointModelAndKeys()
    {
        using var provider = new GpuStackEmbeddingProvider(Configured(embeddingModel: "embed-model"), RespondingWith("{}"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("text", TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("/v1/embeddings", ex.Message);
        Assert.Contains("embed-model", ex.Message);
        Assert.Contains("gpustack.endpoint", ex.Message);
        Assert.Contains("gpustack.embedding_model", ex.Message);
    }

    [Fact]
    public async Task Rerank_ModelMissing_NamesRerankModelKeyAndEnvironmentVariable()
    {
        using var provider = new GpuStackRerankProvider(Configured());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RerankAsync("q", ["a"], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("gpustack.rerank_model", ex.Message);
        Assert.Contains("GPUSTACK_RERANK_MODEL", ex.Message);
    }

    [Fact]
    public async Task Rerank_EndpointNotConfigured_NamesEndpointKeys()
    {
        using var provider = new GpuStackRerankProvider(new GpuStackConfig());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RerankAsync("q", ["a"], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("gpustack.endpoint", ex.Message);
        Assert.Contains("GPUSTACK_ENDPOINT", ex.Message);
        Assert.Contains("gpustack.rerank_model", ex.Message);
    }

    [Fact]
    public async Task Rerank_ResponseWithoutResults_NamesEndpointModelAndKeys()
    {
        using var provider = new GpuStackRerankProvider(Configured(rerankModel: "rerank-model"), RespondingWith("{}"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RerankAsync("q", ["a"], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("/v1/rerank", ex.Message);
        Assert.Contains("rerank-model", ex.Message);
        Assert.Contains("gpustack.rerank_model", ex.Message);
    }

    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly string _body;

        public FixedResponseHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }
}
