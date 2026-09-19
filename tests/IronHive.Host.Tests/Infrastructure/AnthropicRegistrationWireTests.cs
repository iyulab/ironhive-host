using System.Net;
using System.Text;
using AwesomeAssertions;
using IronHive.Abstractions.Messages;
using IronHive.Cli.Infrastructure;
using IronHive.Providers.Anthropic;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The request the CLI's Anthropic registration actually sends. The registration once set a base URL ending in
/// <c>/v1/</c>; the SDK appends the versioned path itself, so every call went to <c>/v1/v1/messages</c> and 404ed —
/// a defect that lived in published versions because nothing looked at the wire.
/// </summary>
public class AnthropicRegistrationWireTests
{
    [Fact]
    public async Task TheRegisteredConfig_SendsMessagesToTheApiRoot()
    {
        var handler = new CapturingHandler();
        var config = ServiceCollectionExtensions.CreateAnthropicConfig(
            new IronHive.Host.Config.AnthropicConfig { ApiKey = "test-key", Model = "claude-haiku-4-5" });
        config.HttpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var generator = new AnthropicMessageGenerator(config);

        await generator.GenerateMessageAsync(
            new MessageGenerationRequest { Model = "claude-haiku-4-5", MaxTokens = 16, Messages = [Message.User("Hi")] },
            TestContext.Current.CancellationToken);

        handler.Uri.Should().NotBeNull();
        handler.Uri!.Host.Should().Be("api.anthropic.com");
        handler.Uri.AbsolutePath.Should().Be("/v1/messages");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            const string body = """
                {"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-4-5","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":3,"output_tokens":1}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
