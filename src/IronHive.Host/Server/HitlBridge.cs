using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using IronHive.Agent.Mode;
using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// The approver for a host whose human is on the other end of a wire: each <c>Ask</c> verdict becomes a
/// <see cref="HitlRequestEvent"/> sent to the client, and the call waits for the matching <see cref="HitlResponseRequest"/>.
/// <see cref="AgentServerRunner"/> (stdio) and <see cref="AgentHttpRunner"/> (HTTP) attach to it while they run and route
/// every <c>hitl_response</c> they receive to <see cref="Resolve"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Fail-closed: with no runner attached, or when no answer arrives within <see cref="Timeout"/>, the call is rejected
/// with a reason the model reads. Cancelling the turn cancels the wait (<see cref="OperationCanceledException"/>).</item>
/// <item>Several requests may wait at once; each has its own id, and a response is matched to its request by
/// <see cref="HitlResponseRequest.Id"/>. A response without an id resolves the request only when exactly one is waiting.</item>
/// <item>The wait is registered before the request is published, so an answer can never arrive before anything waits for
/// it.</item>
/// <item>A wait can outlive the process: with a <see cref="WaitLog"/> each request is logged before it is sent and settled
/// when answered, and a restarted host offers a logged wait again with <see cref="ReofferAsync"/>. A call whose answer is
/// already known (<see cref="Preanswer"/>) is answered without asking.</item>
/// </list>
/// Session rules a host keeps of its own (trust lists, "already denied in this session") fit as an
/// <see cref="IHumanApprovalService"/> that decorates this one.
/// </remarks>
public sealed class HitlBridge : IHumanApprovalService, IDisposable
{
    /// <summary>The default time a request waits for an answer before it is rejected.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<HitlResponseRequest>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HitlResponseRequest> _preanswered = new(StringComparer.Ordinal);
    private readonly Func<ApprovalRequest, string, HitlRequestEvent> _format;
    private Func<ServerEvent, CancellationToken, Task>? _publish;

    /// <summary>Creates the bridge.</summary>
    /// <param name="timeout">How long a request waits for an answer; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="format">Builds the event sent for a request (given the request and the id it must carry); the default
    /// sends the tool name, arguments, call id, risk level, the verdict's reason as the action, the primary argument (path,
    /// command, url, query) as the target and the approval prompt as the description.</param>
    public HitlBridge(TimeSpan? timeout = null, Func<ApprovalRequest, string, HitlRequestEvent>? format = null)
    {
        if (timeout is { } t && t <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The approval timeout must be positive.");
        }

        Timeout = timeout ?? DefaultTimeout;
        _format = format ?? DefaultFormat;
    }

    /// <summary>How long a request waits for an answer before it is rejected.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Hears each request when it starts waiting and when it is settled; null keeps waits in memory only. A server mode
    /// that records its session sets this to the session's <see cref="Session.SessionTurnRecorder"/>.
    /// </summary>
    public IApprovalWaitLog? WaitLog { get; set; }

    /// <summary>True while a runner is attached — requests can reach a client.</summary>
    public bool IsAttached => Volatile.Read(ref _publish) is not null;

    /// <summary>
    /// Attaches the transport that sends events to the client. Returns a handle that detaches it; requests still waiting
    /// when it is detached are rejected.
    /// </summary>
    public IDisposable Attach(Func<ServerEvent, CancellationToken, Task> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (Interlocked.CompareExchange(ref _publish, publish, null) is not null)
        {
            throw new InvalidOperationException("A runner is already attached to this approval bridge.");
        }

        return new Detacher(this, publish);
    }

    /// <summary>
    /// Delivers a client's answer. Returns false when it matches no waiting request (an unknown id, or no id while several
    /// requests wait) — the answer is then dropped and the request keeps waiting.
    /// </summary>
    public bool Resolve(HitlResponseRequest response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Id is { Length: > 0 } id)
        {
            return _pending.TryRemove(id, out var waiter) && waiter.TrySetResult(response);
        }

        var snapshot = _pending.ToArray();
        if (snapshot.Length != 1)
        {
            return false;
        }

        return _pending.TryRemove(snapshot[0].Key, out var only) && only.TrySetResult(response);
    }

    /// <summary>
    /// Records the answer to the call <paramref name="callId"/> before it is asked: the next request for that call is
    /// answered with it at once, without sending anything. A host resuming a wait that a previous process logged runs the
    /// call through its tool pipeline after this, so the gate hears the answer the client gave.
    /// </summary>
    /// <returns>Withdraws the answer if no request used it (a gate that no longer asks about the call).</returns>
    public IDisposable Preanswer(string callId, HitlResponseRequest answer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(answer);
        _preanswered[callId] = answer;
        return new Withdrawal(this, callId, answer);
    }

    private sealed class Withdrawal(HitlBridge bridge, string callId, HitlResponseRequest answer) : IDisposable
    {
        public void Dispose() =>
            bridge._preanswered.TryRemove(new KeyValuePair<string, HitlResponseRequest>(callId, answer));
    }

    /// <summary>
    /// Offers a wait logged by a previous process to the client again, with the same request id, and waits for the answer.
    /// The wait's time counts from when it was first sent: past <see cref="Timeout"/> it is a rejection without asking.
    /// The wait is not settled here - the caller settles it once the call's result is written.
    /// </summary>
    /// <param name="wait">The logged wait.</param>
    /// <param name="cancellationToken">Cancels the wait; it stays unsettled.</param>
    public async Task<HitlResponseRequest> ReofferAsync(Session.ApprovalWaitEntry wait, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wait);

        var remaining = Timeout - (DateTimeOffset.UtcNow - wait.Timestamp);
        if (remaining <= TimeSpan.Zero)
        {
            return new HitlResponseRequest(false, TimedOutReason(), wait.RequestId);
        }

        var publish = Volatile.Read(ref _publish);
        if (publish is null)
        {
            return new HitlResponseRequest(false, NoClientReason, wait.RequestId);
        }

        var waiter = new TaskCompletionSource<HitlResponseRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[wait.RequestId] = waiter;
        try
        {
            using var timeoutCts = new CancellationTokenSource(remaining);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await using var registration = linked.Token.Register(() => waiter.TrySetCanceled(linked.Token));

            await publish(
                new HitlRequestEvent(
                    wait.RequestId, wait.Action, wait.Target, wait.Description, wait.Tool, wait.Arguments, wait.ToolUseId, wait.Level),
                cancellationToken);

            try
            {
                return await waiter.Task;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new HitlResponseRequest(false, TimedOutReason(), wait.RequestId);
            }
        }
        finally
        {
            _pending.TryRemove(wait.RequestId, out _);
        }
    }

    /// <inheritdoc />
    public async Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CallId is { Length: > 0 } callId && _preanswered.TryRemove(callId, out var known))
        {
            return ToResult(known);
        }

        var publish = Volatile.Read(ref _publish);
        if (publish is null)
        {
            return ApprovalResult.Reject(NoClientReason);
        }

        var id = Guid.NewGuid().ToString("N");
        var waiter = new TaskCompletionSource<HitlResponseRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = waiter;
        try
        {
            using var timeoutCts = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await using var registration = linked.Token.Register(() => waiter.TrySetCanceled(linked.Token));

            // Registered above, logged and published here: an answer cannot outrun its wait, and a wait the client can
            // see is already on record.
            var sent = _format(request, id);
            if (WaitLog is { } log)
            {
                await log.WaitingAsync(request, sent, cancellationToken);
            }

            await publish(sent, cancellationToken);

            HitlResponseRequest response;
            try
            {
                response = await waiter.Task;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The turn was cancelled: the wait stays on record, unsettled, for a restarted host to offer again
                throw;
            }
            catch (OperationCanceledException)
            {
                response = new HitlResponseRequest(false, TimedOutReason(), id);
            }

            if (WaitLog is { } settledLog)
            {
                await settledLog.SettledAsync(id, CancellationToken.None);
            }

            return ToResult(response);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Rejects every waiting request; nothing is attached afterwards.</summary>
    public void Dispose()
    {
        Volatile.Write(ref _publish, null);
        RejectAllPending("the approval bridge was shut down");
    }

    private void RejectAllPending(string reason)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var waiter))
            {
                waiter.TrySetResult(new HitlResponseRequest(false, reason, id));
            }
        }
    }

    private const string NoClientReason = "no client is attached to answer approval requests";

    private string TimedOutReason() =>
        string.Create(CultureInfo.InvariantCulture, $"no answer to the approval request within {Timeout.TotalSeconds:F0}s");

    private static ApprovalResult ToResult(HitlResponseRequest response)
    {
        if (!response.Approved)
        {
            return ApprovalResult.Reject(response.Reason);
        }

        return new ApprovalResult
        {
            Approved = true,
            AlwaysApprove = response.AlwaysApprove,
            ModifiedArguments = response.ModifiedArguments?.ToDictionary(
                kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal)
        };
    }

    private static HitlRequestEvent DefaultFormat(ApprovalRequest request, string id)
    {
        var args = request.Arguments;
        var target = FirstArgument(args, "path", "command", "url", "query") ?? string.Empty;
        return new HitlRequestEvent(
            Id: id,
            Action: request.RiskAssessment.Reason ?? request.ToolName,
            Target: target,
            Description: request.Description ?? request.RiskAssessment.ApprovalPrompt ?? string.Empty,
            ToolName: request.ToolName,
            Arguments: args is null ? null : JsonSerializer.SerializeToElement(args),
            CallId: request.CallId,
            Level: request.RiskAssessment.Level.ToString().ToLowerInvariant());
    }

    private static string? FirstArgument(IDictionary<string, object?>? args, params string[] keys)
    {
        if (args is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (args.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private sealed class Detacher(HitlBridge bridge, Func<ServerEvent, CancellationToken, Task> publish) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref bridge._publish, null, publish) == publish)
            {
                bridge.RejectAllPending("the client connection closed before answering");
            }
        }
    }
}
