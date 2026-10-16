using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using IronHive.Abstractions.Messages;
using IronHive.Cli.Infrastructure;
using IronHive.Providers.OpenAI.Compatible;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The request the CLI's generic <c>openaiCompatible</c> registration sends: Chat Completions under the configured
/// endpoint (not the Responses API the <c>openai</c> provider speaks), and no <c>Authorization</c> header unless a key
/// is configured — a gateway whose proxy adds the credential must not receive a placeholder one.
/// </summary>
public class OpenAICompatibleRegistrationWireTests
{
    [Fact]
    public async Task WithoutAKey_PostsChatCompletionsAndSendsNoAuthorization()
    {
        await using var server = await OneShotServer.StartAsync();
        var config = ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
            new CliConfig.OpenAICompatibleHostConfig { Endpoint = server.BaseUrl + "/v1/", Model = "mock" });

        await GenerateAsync(config);

        var request = await server.Request;
        request.RequestLine.Should().Be("POST /v1/chat/completions HTTP/1.1");
        request.Headers.Keys.Should().NotContain("authorization");
    }

    [Fact]
    public async Task WithAKey_SendsItAsBearer()
    {
        await using var server = await OneShotServer.StartAsync();
        var config = ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
            new CliConfig.OpenAICompatibleHostConfig { Endpoint = server.BaseUrl + "/v1", ApiKey = "gw-key", Model = "mock" });

        await GenerateAsync(config);

        var request = await server.Request;
        request.RequestLine.Should().Be("POST /v1/chat/completions HTTP/1.1");
        request.Headers["authorization"].Should().Be("Bearer gw-key");
    }

    [Fact]
    public async Task AnEndpointUnderAnotherVersionPath_IsUsedAsGiven()
    {
        // e.g. a cloud provider's compatible endpoint under /v1beta/openai — no /v1 may be appended to it.
        await using var server = await OneShotServer.StartAsync();
        var config = ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
            new CliConfig.OpenAICompatibleHostConfig { Endpoint = server.BaseUrl + "/v1beta/openai/", Model = "mock" });

        await GenerateAsync(config);

        (await server.Request).RequestLine.Should().Be("POST /v1beta/openai/chat/completions HTTP/1.1");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public void CarryToolImages_ReachesTheProviderConfig(bool? configured, bool expected) =>
        ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
                new CliConfig.OpenAICompatibleHostConfig { Endpoint = "http://gw:8000/v1", Model = "m", CarryToolImages = configured })
            .CarryImageToolResultsAsUserMessage.Should().Be(expected);

    [Fact]
    public void StreamIdleTimeout_NonPositive_IsRefusedNamingTheKey()
    {
        var act = () => ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
            new CliConfig.OpenAICompatibleHostConfig { Endpoint = "http://gw:8000/v1", Model = "m", StreamIdleTimeoutSeconds = 0 });

        act.Should().Throw<InvalidOperationException>().WithMessage("openaiCompatible.streamIdleTimeoutSeconds must be a positive*");
    }

    [Fact]
    public void IsConfigured_NeedsEndpointAndModel_NotAKey()
    {
        new CliConfig.OpenAICompatibleHostConfig { Endpoint = "http://gw:8000/v1", Model = "m" }.IsConfigured.Should().BeTrue();
        new CliConfig.OpenAICompatibleHostConfig { Endpoint = "http://gw:8000/v1" }.IsConfigured.Should().BeFalse();
        new CliConfig.OpenAICompatibleHostConfig { Model = "m", ApiKey = "k" }.IsConfigured.Should().BeFalse();
    }

    private static async Task GenerateAsync(OpenAICompatibleConfig config)
    {
        var generator = new OpenAICompatibleMessageGenerator(config);
        try
        {
            await generator.GenerateMessageAsync(
                new MessageGenerationRequest { Model = "mock", MaxTokens = 16, Messages = [Message.User("Hi")] },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            (generator as IDisposable)?.Dispose();
        }
    }

    private sealed record CapturedRequest(string RequestLine, IReadOnlyDictionary<string, string> Headers);

    /// <summary>Answers one HTTP request on 127.0.0.1 with a fixed Chat Completions body and records what it received.</summary>
    private sealed class OneShotServer : IAsyncDisposable
    {
        private const string Body = """
            {"id":"c1","object":"chat.completion","created":0,"model":"mock","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
            """;

        private readonly TcpListener _listener;
        private readonly Task<CapturedRequest> _request;

        private OneShotServer(TcpListener listener)
        {
            _listener = listener;
            _request = ServeOnceAsync();
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public Task<CapturedRequest> Request => _request;

        public static Task<OneShotServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new OneShotServer(listener));
        }

        private async Task<CapturedRequest> ServeOnceAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync() ?? string.Empty;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                var colon = line.IndexOf(':');
                headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
            }

            if (headers.TryGetValue("content-length", out var length))
            {
                var buffer = new char[int.Parse(length, System.Globalization.CultureInfo.InvariantCulture)];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = await reader.ReadAsync(buffer.AsMemory(read));
                    if (n == 0)
                    {
                        break;
                    }

                    read += n;
                }
            }

            var payload = Encoding.UTF8.GetBytes(Body);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head);
            await stream.WriteAsync(payload);
            await stream.FlushAsync();
            return new CapturedRequest(requestLine, headers);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _request;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // The test failed before a request arrived; the assertion already says why.
            }
        }
    }
}
