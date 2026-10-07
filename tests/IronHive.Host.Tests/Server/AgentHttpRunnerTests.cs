using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace IronHive.Host.Tests.Server;

public class AgentHttpRunnerTests
{
    [Fact]
    public async Task InboxClosesWithoutShutdown_RunnerReconnects_AndKeepsServing()
    {
        var host = new FakeHost(
            Inbox("id: 1", """data: {"type":"user_message","content":"first"}"""),
            Inbox("id: 2", """data: {"type":"user_message","content":"second"}""", """data: {"type":"shutdown"}"""));
        var messages = new ConcurrentQueue<string>();
        using var runner = CreateRunner(host, messages);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        messages.Should().Equal("first", "second");
        host.InboxRequests.Should().Be(2);
        host.LastEventIds.Should().Equal(null, "1");
    }

    [Fact]
    public async Task InboxUnreachable_RetriesWithBackoff_ThenServes()
    {
        var host = new FakeHost(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.BadGateway,
            Inbox("""data: {"type":"user_message","content":"after outage"}""", """data: {"type":"shutdown"}"""));
        var messages = new ConcurrentQueue<string>();
        using var runner = CreateRunner(host, messages);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        messages.Should().Equal("after outage");
        host.InboxRequests.Should().Be(3);
    }

    [Fact]
    public async Task ReconnectLimitReached_FailsInsteadOfEndingQuietly()
    {
        var host = new FakeHost(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);
        using var runner = CreateRunner(host, new ConcurrentQueue<string>());
        runner.MaxReconnectAttempts = 2;

        var act = () => runner.RunAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*2 reconnect attempt(s) failed*");
        host.InboxRequests.Should().Be(3);
    }

    [Fact]
    public async Task HostRefusesTheSession_FailsWithoutRetrying()
    {
        var host = new FakeHost(HttpStatusCode.NotFound);
        using var runner = CreateRunner(host, new ConcurrentQueue<string>());

        var act = () => runner.RunAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
        host.InboxRequests.Should().Be(1);
    }

    [Fact]
    public async Task UnreadableInboxMessage_IsReportedAndSkipped()
    {
        var host = new FakeHost(Inbox(
            "data: {broken",
            """data: {"type":"user_message","content":"ok"}""",
            """data: {"type":"shutdown"}"""));
        var messages = new ConcurrentQueue<string>();
        using var runner = CreateRunner(host, messages);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        messages.Should().Equal("ok");
        host.PostedEvents.Should().Contain(e => e.Contains("Unreadable request skipped", StringComparison.Ordinal));
    }

    private static AgentHttpRunner CreateRunner(FakeHost host, ConcurrentQueue<string> messages)
        => new("http://agent-host.test", "s1", (msg, ct) => Echo(msg, messages, ct), NullLogger<AgentHttpRunner>.Instance,
            httpHandler: host)
        {
            InitialReconnectDelay = TimeSpan.FromMilliseconds(1),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(5),
        };

    private static async IAsyncEnumerable<ServerEvent> Echo(
        UserMessageRequest msg, ConcurrentQueue<string> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        messages.Enqueue(msg.Content);
        await Task.Yield();
        yield return new TurnEndEvent();
    }

    private static object Inbox(params string[] lines) => string.Join("\n", lines) + "\n\n";

    /// <summary>
    /// Answers each inbox GET with the next scripted reply (an SSE body, or a status code); accepts every POST.
    /// </summary>
    private sealed class FakeHost(params object[] inboxReplies) : HttpMessageHandler
    {
        private int _next;

        public int InboxRequests => _next;

        public List<string?> LastEventIds { get; } = [];

        public ConcurrentQueue<string> PostedEvents { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                if (request.Content is not null)
                {
                    PostedEvents.Enqueue(await request.Content.ReadAsStringAsync(cancellationToken));
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            LastEventIds.Add(request.Headers.TryGetValues("Last-Event-ID", out var ids) ? ids.Single() : null);
            var index = Interlocked.Increment(ref _next) - 1;
            if (index >= inboxReplies.Length)
            {
                return new HttpResponseMessage(HttpStatusCode.Gone);
            }

            return inboxReplies[index] switch
            {
                HttpStatusCode code => new HttpResponseMessage(code),
                string body => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
                },
                var other => throw new InvalidOperationException($"Unexpected reply {other}"),
            };
        }
    }
}
