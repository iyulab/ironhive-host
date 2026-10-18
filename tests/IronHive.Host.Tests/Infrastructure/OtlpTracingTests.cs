using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using Xunit;

namespace IronHive.Host.Tests.Infrastructure;

public class OtlpTracingTests
{
    [Fact]
    public void WithoutAnOtlpEndpoint_NothingIsStarted()
    {
        OtlpTracing.StartFromEnvironment("1.0.0", _ => null).Should().BeNull();
    }

    [Fact]
    public void WithTheSdkDisabled_NothingIsStarted()
    {
        var environment = new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:4317",
            ["OTEL_SDK_DISABLED"] = "true",
        };

        OtlpTracing.StartFromEnvironment("1.0.0", environment.GetValueOrDefault).Should().BeNull();
    }

    [Fact]
    public async Task OverHttp_AModelCallSpan_ReachesTheCollectorAtTheTracesPath()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var request = (context.Request.HttpMethod, context.Request.Url!.AbsolutePath, context.Request.ContentType);
            context.Response.StatusCode = 200;
            context.Response.Close();
            return request;
        });

        var environment = new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://127.0.0.1:{port}",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        };
        using (var provider = OtlpTracing.StartFromEnvironment("1.0.0", environment.GetValueOrDefault))
        {
            provider.Should().NotBeNull();
            using var source = new ActivitySource(OtlpTracing.ChatSourceName);
            using (var activity = source.StartActivity("chat test-model"))
            {
                activity.Should().NotBeNull("the provider listens to the CLI's chat source");
            }
        } // dispose flushes

        var completed = await Task.WhenAny(received, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        completed.Should().BeSameAs(received, "the exporter posts the span on flush");
        var (method, path, contentType) = await received;
        method.Should().Be("POST");
        path.Should().Be("/v1/traces");
        contentType.Should().Be("application/x-protobuf");
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
