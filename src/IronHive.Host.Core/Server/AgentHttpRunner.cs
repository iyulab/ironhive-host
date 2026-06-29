using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.Logging;

using IronHive.Host.Protocol;

namespace IronHive.Host.Core.Server;

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted ready signal for session {SessionId}")]
    private partial void LogReadyPosted(string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received request type {RequestType}")]
    private partial void LogRequestReceived(string requestType);

    private readonly Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> _processMessage;
    private readonly HttpClient _http;
    private readonly string _sessionId;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<AgentHttpRunner> _logger;

    private CancellationTokenSource? _turnCts;
    private string? _workingPath;
    private TaskCompletionSource<HitlResponseRequest>? _pendingHitl;

    /// <summary>
    /// Optional callback invoked when a context update is received.
    /// </summary>
    public Action<ContextUpdateRequest>? OnContextUpdate { get; set; }

    /// <summary>
    /// When true, <see cref="BuildContextualContent"/> becomes a pass-through.
    /// Set this when an external orchestrator handles context injection itself.
    /// </summary>
    public bool SkipContextEnrichment { get; set; }

    /// <param name="hostUrl">Base URL of the host, e.g. "http://localhost:5100".</param>
    /// <param name="sessionId">Session ID to connect to.</param>
    /// <param name="processMessage">Processes a user message and yields server events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="jsonOptions">Custom JSON options. Defaults to snake_case.</param>
    /// <param name="typeInfoModifiers">
    /// Optional modifiers applied to <see cref="DefaultJsonTypeInfoResolver"/> to extend or override
    /// polymorphic type registrations. Applied in order; each modifier is appended to the resolver chain.
    /// </param>
    public AgentHttpRunner(
        string hostUrl,
        string sessionId,
        Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage,
        ILogger<AgentHttpRunner> logger,
        JsonSerializerOptions? jsonOptions = null,
        Action<JsonTypeInfo>[]? typeInfoModifiers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(processMessage);
        ArgumentNullException.ThrowIfNull(logger);

        _processMessage = processMessage;
        _sessionId = sessionId;
        _logger = logger;
        _jsonOptions = AgentServerRunner.ApplyModifiers(
            jsonOptions ?? AgentServerRunner.DefaultJsonOpts, typeInfoModifiers);
        _http = new HttpClient
        {
            BaseAddress = new Uri(hostUrl.TrimEnd('/')),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
    }

    /// <summary>
    /// Resolves a pending HITL request. Called externally when the host relays the user's response.
    /// </summary>
    public void ResolveHitl(HitlResponseRequest response)
    {
        _pendingHitl?.TrySetResult(response);
    }

    /// <summary>
    /// Waits for a HITL response from the host. Called from within the agent pipeline.
    /// </summary>
    public Task<HitlResponseRequest> WaitForHitlResponseAsync(CancellationToken ct)
    {
        _pendingHitl = new TaskCompletionSource<HitlResponseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => _pendingHitl.TrySetCanceled(ct));
        return _pendingHitl.Task;
    }

    /// <summary>
    /// Runs the agent loop: signals readiness, subscribes to the SSE inbox, and processes commands
    /// until shutdown or cancellation.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
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
        var url = $"/api/agent/{_sessionId}/inbox";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        Task? handleTask = null;

        try
        {
            await foreach (var serverRequest in ReadSseRequestsAsync(reader, ct))
            {
                LogRequestReceived(serverRequest.GetType().Name);

                if (serverRequest is ShutdownRequest)
                {
                    break;
                }

                if (serverRequest is CancelRequest)
                {
                    _turnCts?.Cancel();
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
                    _pendingHitl?.TrySetResult(hitl);
                    continue;
                }

                if (serverRequest is UserMessageRequest msg)
                {
                    if (handleTask is not null)
                    {
                        await handleTask;
                    }

                    _turnCts?.Dispose();
                    _turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    var contextualMsg = SkipContextEnrichment || _workingPath is null
                        ? msg
                        : msg with { Content = BuildContextualContent(msg.Content) };

                    handleTask = HandleMessageAsync(contextualMsg, _turnCts.Token);
                }
            }
        }
        finally
        {
            if (handleTask is not null)
            {
                await handleTask;
            }

            _turnCts?.Dispose();
            _turnCts = null;
        }
    }

    private async IAsyncEnumerable<ServerRequest> ReadSseRequestsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (IOException) when (!ct.IsCancellationRequested)
            {
                LogSseConnectionClosed();
                yield break;
            }

            if (line is null)
            {
                LogSseConnectionClosed();
                yield break;
            }

            // SSE blank lines and "event:" lines are informational — skip.
            if (string.IsNullOrEmpty(line) || line.StartsWith("event:", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var json = line[6..];
                ServerRequest? parsed = null;
                try
                {
                    parsed = JsonSerializer.Deserialize<ServerRequest>(json, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    await Console.Error.WriteLineAsync($"[AgentHttpRunner] Failed to parse SSE data: {ex.Message}");
                }

                if (parsed is not null)
                {
                    yield return parsed;
                }
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
        try
        {
            await foreach (var evt in _processMessage(msg, ct))
            {
                await PostEventAsync(evt, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentional cancellation via CancelRequest — TurnEndEvent posted in finally.
        }
        catch (Exception ex)
        {
            LogAgentProcessingError(ex);
            await PostEventAsync(new ErrorEvent(ex.Message), CancellationToken.None);
        }
        finally
        {
            await PostEventAsync(new TurnEndEvent(), CancellationToken.None);
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
        _turnCts?.Dispose();
        _http.Dispose();
    }
}
