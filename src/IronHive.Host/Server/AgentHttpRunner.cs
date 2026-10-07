using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using IronHive.Host.Protocol;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Server;

/// <summary>
/// HTTP/SSE-based counterpart to <see cref="AgentServerRunner"/>.
/// Connects to a host's agent inbox via Server-Sent Events to receive commands
/// and posts <see cref="ServerEvent"/> batches back via REST.
/// </summary>
/// <remarks>
/// <para>
/// Expected host endpoints:
/// <list type="bullet">
///   <item><description>POST /api/agent/{sessionId}/ready — signals that the agent process is up</description></item>
///   <item><description>GET  /api/agent/{sessionId}/inbox — SSE stream of <see cref="ServerRequest"/> commands</description></item>
///   <item><description>POST /api/agent/{sessionId}/events — delivers <see cref="ServerEvent"/> batches to the host</description></item>
/// </list>
/// </para>
/// <para>
/// The same <c>processor</c> delegate signature as <see cref="AgentServerRunner"/> is used so a single
/// agent pipeline can serve either transport.
/// </para>
/// </remarks>
public sealed partial class AgentHttpRunner : IDisposable
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Agent processing error")]
    private partial void LogAgentProcessingError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "SSE connection closed by host")]
    private partial void LogSseConnectionClosed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent inbox unavailable ({Cause}); reconnect attempt {Attempt} in {DelayMs} ms")]
    private partial void LogReconnecting(string cause, int attempt, long delayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable inbox message skipped ({ErrorType})")]
    private partial void LogUnreadableRequest(string errorType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted ready signal for session {SessionId}")]
    private partial void LogReadyPosted(string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received request type {RequestType}")]
    private partial void LogRequestReceived(string requestType);

    private readonly Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> _processMessage;
    private readonly HttpClient _http;
    private readonly string _sessionId;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<AgentHttpRunner> _logger;

    private TurnQueue? _turns;
    private string? _workingPath;
    private string? _lastEventId;
    private bool _receivedSinceConnect;
    private readonly HitlBridge? _hitl;

    /// <summary>
    /// Optional callback invoked when a context update is received.
    /// </summary>
    public Action<ContextUpdateRequest>? OnContextUpdate { get; set; }

    /// <summary>
    /// When true, <see cref="BuildContextualContent"/> becomes a pass-through.
    /// Set this when an external orchestrator handles context injection itself.
    /// </summary>
    public bool SkipContextEnrichment { get; set; }

    /// <summary>
    /// Delay before the first reconnect after the inbox stream ends without a shutdown request or cannot be reached.
    /// Each further consecutive attempt doubles it, up to <see cref="MaxReconnectDelay"/>. Default: 1 second.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound of the reconnect delay. Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Consecutive reconnect attempts before <see cref="RunAsync"/> fails with <see cref="HttpRequestException"/>.
    /// Default 10 (about three minutes with the default delays), so an agent whose host is gone exits instead of
    /// waiting forever; null keeps reconnecting until a shutdown request or cancellation. The count resets once a
    /// reconnected stream delivers a message.
    /// </summary>
    public int? MaxReconnectAttempts { get; set; } = 10;

    /// <param name="hostUrl">Base URL of the host, e.g. "http://localhost:5100".</param>
    /// <param name="sessionId">Session ID to connect to.</param>
    /// <param name="processMessage">Processes a user message and yields server events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="jsonOptions">Custom JSON options. Defaults to snake_case.</param>
    /// <param name="typeInfoModifiers">
    /// Optional modifiers applied to <see cref="DefaultJsonTypeInfoResolver"/> to extend or override
    /// polymorphic type registrations. Applied in order; each modifier is appended to the resolver chain.
    /// </param>
    /// <param name="hitlBridge">The approver the agent's gate asks: while the runner runs, its approval requests are posted
    /// as <see cref="HitlRequestEvent"/>s and each <see cref="HitlResponseRequest"/> from the inbox answers one. Without
    /// it, a <c>hitl_response</c> is ignored.</param>
    /// <param name="httpHandler">Message handler for the connection to the host (proxy, TLS, testing). Null uses the
    /// default handler. The runner disposes it.</param>
    public AgentHttpRunner(
        string hostUrl,
        string sessionId,
        Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage,
        ILogger<AgentHttpRunner> logger,
        JsonSerializerOptions? jsonOptions = null,
        Action<JsonTypeInfo>[]? typeInfoModifiers = null,
        HitlBridge? hitlBridge = null,
        HttpMessageHandler? httpHandler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(processMessage);
        ArgumentNullException.ThrowIfNull(logger);

        _hitl = hitlBridge;
        _processMessage = processMessage;
        _sessionId = sessionId;
        _logger = logger;
        _jsonOptions = AgentServerRunner.ApplyModifiers(
            jsonOptions ?? AgentServerRunner.DefaultJsonOpts, typeInfoModifiers);
        _http = new HttpClient(httpHandler ?? new HttpClientHandler(), disposeHandler: true)
        {
            BaseAddress = new Uri(hostUrl.TrimEnd('/')),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
    }

    /// <summary>
    /// Runs the agent loop: signals readiness, subscribes to the SSE inbox, and processes commands
    /// until a shutdown request or cancellation. When the inbox stream ends or cannot be reached it reconnects with
    /// backoff (sending <c>Last-Event-ID</c> when the host numbers its events); a turn in progress keeps running.
    /// </summary>
    /// <exception cref="HttpRequestException">The host refused the inbox (a 4xx other than 408/429), or
    /// <see cref="MaxReconnectAttempts"/> consecutive reconnects failed.</exception>
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var hitlAttachment = _hitl?.Attach(PostEventAsync);
        await PostReadyAsync(ct);
        await ProcessInboxAsync(ct);
    }

    /// <summary>
    /// Best-effort fire-and-forget event publish for out-of-band notices
    /// (e.g. a <see cref="FallbackServerEvent"/> emitted while the streaming pipeline is mid-flight).
    /// Failures are swallowed so callers can use a synchronous sink.
    /// </summary>
    public void PublishEvent(ServerEvent evt)
    {
        _ = Task.Run(() => PostEventAsync(evt, CancellationToken.None));
    }

    private async Task PostReadyAsync(CancellationToken ct)
    {
        var url = $"/api/agent/{_sessionId}/ready";
        var payload = new { session_id = _sessionId };
        var response = await _http.PostAsJsonAsync(url, payload, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        LogReadyPosted(_sessionId);
    }

    private async Task ProcessInboxAsync(CancellationToken ct)
    {
        var attempt = 0;
        // Turns run on their own task so the inbox keeps being read — a turn waiting for approval gets its
        // hitl_response even when another message arrived first.
        _turns = new TurnQueue(ct);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                string reason;
                try
                {
                    if (await ReadInboxAsync(ct))
                    {
                        return; // shutdown request
                    }

                    reason = "stream closed";
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
                {
                    reason = ex.Message;
                }

                if (_receivedSinceConnect)
                {
                    attempt = 0;
                }

                attempt++;
                if (MaxReconnectAttempts is { } max && attempt > max)
                {
                    throw new HttpRequestException(
                        $"The agent inbox for session '{_sessionId}' was lost and {max} reconnect attempt(s) failed ({reason}).");
                }

                var delay = ReconnectDelay(attempt);
                LogReconnecting(reason, attempt, (long)delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
        finally
        {
            await _turns.DisposeAsync();
            _turns = null;
        }
    }

    private TimeSpan ReconnectDelay(int attempt)
    {
        var factor = Math.Pow(2, Math.Min(attempt - 1, 16));
        var delay = TimeSpan.FromTicks((long)Math.Min(InitialReconnectDelay.Ticks * factor, MaxReconnectDelay.Ticks));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    // A dropped connection or a host that is briefly down or overloaded is worth retrying; a host that refuses the
    // session (404, 401, ...) is not.
    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500
            || code is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests,
        IOException => true,
        _ => false,
    };

    /// <summary>
    /// Reads one inbox connection to its end. Returns true when a shutdown request arrived.
    /// </summary>
    private async Task<bool> ReadInboxAsync(CancellationToken ct)
    {
        var url = $"/api/agent/{_sessionId}/inbox";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", _lastEventId);
        }

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        _receivedSinceConnect = false;

        await foreach (var serverRequest in ReadSseRequestsAsync(reader, ct))
        {
            _receivedSinceConnect = true;
            LogRequestReceived(serverRequest.GetType().Name);

            if (serverRequest is ShutdownRequest)
            {
                return true;
            }

            if (serverRequest is CancelRequest)
            {
                _turns?.CancelCurrent();
                continue;
            }

            if (serverRequest is ContextUpdateRequest ctx)
            {
                _workingPath = ctx.WorkingPath;
                OnContextUpdate?.Invoke(ctx);
                continue;
            }

            if (serverRequest is HitlResponseRequest hitl)
            {
                _hitl?.Resolve(hitl);
                continue;
            }

            if (serverRequest is UserMessageRequest msg)
            {
                var contextualMsg = SkipContextEnrichment || _workingPath is null
                    ? msg
                    : msg with { Content = BuildContextualContent(msg.Content) };

                _turns?.Enqueue(token => HandleMessageAsync(contextualMsg, token));
            }
        }

        return false;
    }

    private async IAsyncEnumerable<ServerRequest> ReadSseRequestsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                LogSseConnectionClosed();
                yield break;
            }

            if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                _lastEventId = line[3..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue; // blank separators, "event:", comments
            }

            var json = line[5..].TrimStart();
            ServerRequest? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ServerRequest>(json, _jsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                parsed = null;
                LogUnreadableRequest(ex.GetType().Name);
                await PostEventAsync(new ErrorEvent($"Unreadable request skipped: {ex.Message}"), CancellationToken.None);
            }

            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    private string BuildContextualContent(string userContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"[WorkingPath: {_workingPath}]");
        sb.AppendLine();
        sb.Append(userContent);
        return sb.ToString();
    }

    private async Task HandleMessageAsync(UserMessageRequest msg, CancellationToken ct)
    {
        var turnEndSent = false;
        try
        {
            await foreach (var evt in _processMessage(msg, ct))
            {
                if (evt is TurnEndEvent)
                {
                    turnEndSent = true;
                }

                await PostEventAsync(evt, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentional cancellation via CancelRequest — fallback TurnEndEvent posted in finally.
        }
        catch (Exception ex)
        {
            LogAgentProcessingError(ex);
            await PostEventAsync(new ErrorEvent(ex.Message), CancellationToken.None);
        }
        finally
        {
            // _processMessage's own pipeline (AgentResponseMapper.ToServerEvents) yields a
            // usage-populated TurnEndEvent as its last event on normal completion — this is
            // only a fallback for the exception/cancellation paths above that abandon the
            // stream before it gets there.
            if (!turnEndSent)
            {
                await PostEventAsync(new TurnEndEvent(), CancellationToken.None);
            }
        }
    }

    private async Task PostEventAsync(ServerEvent evt, CancellationToken ct)
    {
        var url = $"/api/agent/{_sessionId}/events";
        try
        {
            var response = await _http.PostAsJsonAsync(url, evt, _jsonOptions, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(
                $"[AgentHttpRunner] Failed to post event: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
