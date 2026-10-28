using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using IronHive.Agent.Mode;
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
        host.PostedEvents.Should().Contain(e => e.Contains("Unreadable request skipped", StringComparison.Ordinal)
            && e.Contains("\"code\":\"unreadable_request\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownInboxRequestType_IsReportedAsUnreadableAndSkipped()
    {
        var host = new FakeHost(Inbox(
            """data: {"type":"a_request_from_a_newer_protocol","id":"r1"}""",
            """data: {"type":"user_message","content":"ok"}""",
            """data: {"type":"shutdown"}"""));
        var messages = new ConcurrentQueue<string>();
        using var runner = CreateRunner(host, messages);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        messages.Should().Equal("ok");
        host.PostedEvents.Should().Contain(e => e.Contains("\"code\":\"unreadable_request\"", StringComparison.Ordinal));
    }

    // A provider call's timeout surfaces as a cancellation the client never asked for; it was posted like a cancel —
    // no error, a bare turn end.
    [Fact]
    public async Task UnrequestedCancellation_IsPostedAsATimeoutError()
    {
        var host = new FakeHost(Inbox(
            """data: {"type":"user_message","content":"slow"}""",
            """data: {"type":"shutdown"}"""));
        using var runner = new AgentHttpRunner(
            "http://agent-host.test", "s1",
            (_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()),
            NullLogger<AgentHttpRunner>.Instance, httpHandler: host);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        host.PostedEvents.Should().Contain(e => e.Contains("\"type\":\"error\"", StringComparison.Ordinal)
            && e.Contains("\"code\":\"timeout\"", StringComparison.Ordinal));
    }

    // A handler that takes the context gets the message as sent and the context read before it.
    [Fact]
    public async Task ContextHandler_ReceivesRawContentAndTheContext()
    {
        var host = new FakeHost(Inbox(
            """data: {"type":"context_update","working_path":"/work","selected_items":["a.txt"]}""",
            """data: {"type":"user_message","content":"hello"}""",
            """data: {"type":"shutdown"}"""));
        string? content = null;
        ContextUpdateRequest? received = null;
        using var runner = new AgentHttpRunner(
            "http://agent-host.test", "s1",
            (msg, context, _) =>
            {
                content = msg.Content;
                received = context;
                return AsyncEnumerable.Empty<ServerEvent>();
            },
            NullLogger<AgentHttpRunner>.Instance, httpHandler: host);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        content.Should().Be("hello");
        received!.WorkingPath.Should().Be("/work");
        ContextEnrichment.Apply(received, content!).Should().Be(
            string.Join(Environment.NewLine, "[WorkingPath: /work]", "[Selected: a.txt]", "", "hello"));
    }

    // A turn a previous process left waiting for approval resumes before the inbox's messages, once
    [Fact]
    public async Task ResumeTurn_RunsBeforeInboxMessages_AndOnlyOnce()
    {
        var host = new FakeHost(
            Inbox("id: 1", """data: {"type":"user_message","content":"first"}"""),
            Inbox("id: 2", """data: {"type":"shutdown"}"""));
        var messages = new ConcurrentQueue<string>();
        using var runner = CreateRunner(host, messages);
        runner.ResumeTurn = ct => Resumed(messages, ct);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        messages.Should().Equal("resumed", "first");
        runner.ResumeTurn.Should().BeNull("a second run must not offer the settled waits again");
    }

    // A shutdown that arrives while (or before) a turn waits for approval: a session that keeps the wait suspends the
    // turn; without one the wait is denied at once. Either way the runner stops promptly, not at the approval timeout.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shutdown_WhileATurnWaitsForApproval_SuspendsWithASession_OrDeniesAtOnce(bool recorded)
    {
        var host = new FakeHost(Inbox(
            """data: {"type":"user_message","content":"write it"}""",
            """data: {"type":"shutdown"}"""));
        using var bridge = new HitlBridge(TimeSpan.FromMinutes(5));
        if (recorded)
        {
            bridge.WaitLog = new MemoryWaitLog();
        }

        ApprovalResult? heard = null;
        async IAsyncEnumerable<ServerEvent> Ask(UserMessageRequest msg, [EnumeratorCancellation] CancellationToken ct)
        {
            heard = await bridge.RequestApprovalAsync(new ApprovalRequest
            {
                ToolName = "write_file",
                CallId = "call-1",
                RiskAssessment = RiskAssessment.Risky(RiskLevel.Medium, "writes", "Allow?"),
            }, ct);
            yield return new TurnEndEvent();
        }

        using var runner = new AgentHttpRunner("http://agent-host.test", "s1", Ask, NullLogger<AgentHttpRunner>.Instance, httpHandler: host, hitlBridge: bridge);

        await runner.RunAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        if (recorded)
        {
            heard.Should().BeNull("the turn was cancelled inside the wait");
            host.PostedEvents.Should().Contain(e => e.Contains("\"stop_reason\":\"suspended\"", StringComparison.Ordinal));
        }
        else
        {
            heard!.Approved.Should().BeFalse();
            heard.RejectionReason.Should().Contain("server stopped");
        }
    }

    private sealed class MemoryWaitLog : IApprovalWaitLog
    {
        public Task WaitingAsync(ApprovalRequest request, HitlRequestEvent sent, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SettledAsync(string requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ServerEvent> Resumed(
        ConcurrentQueue<string> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(50, ct);
        messages.Enqueue("resumed");
        yield return new TurnEndEvent();
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
