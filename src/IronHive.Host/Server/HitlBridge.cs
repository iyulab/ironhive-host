using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using IronHive.Agent.Mcp;
using IronHive.Agent.Mode;
using IronHive.Host.Protocol;
using IronHive.Host.Session;
using ModelContextProtocol.Protocol;

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
/// <item>A wait need not hold its turn: with <see cref="DeferAfter"/> set and a runner attached that can open a turn for the
/// answer, a request not answered within that time is deferred - the call does not run, the model is told it waits for
/// approval (<see cref="ApprovalResult.Defer"/>), and the turn goes on. The answer (or <see cref="Timeout"/>, counted from
/// the first send) reaches the runner as a <see cref="DeferredApproval"/>, which opens a turn that runs the call
/// (<see cref="DeferredApprovalTurn"/>).</item>
/// <item>A wait can outlive the process: with a <see cref="WaitLog"/> each request is logged before it is sent and settled
/// when answered, and a restarted host offers a logged wait again with <see cref="ReofferAsync"/> (a wait inside a turn) or
/// <see cref="RestoreDeferred"/> (a deferred one). A call whose answer is already known (<see cref="Preanswer"/>) is
/// answered without asking.</item>
/// </list>
/// Session rules a host keeps of its own (trust lists, "already denied in this session") fit as an
/// <see cref="IHumanApprovalService"/> that decorates this one.
/// <para>
/// The bridge also carries an MCP server's URL-mode elicitation (<see cref="IMcpElicitationHandler"/>): an
/// <see cref="ElicitationRequestEvent"/> goes to the client and the call waits for the matching
/// <see cref="ElicitationResponseRequest"/> (<see cref="ResolveElicitation"/>). That wait always stays inside its turn — the
/// MCP client holds the retry in-process — and ends as <c>cancel</c> after <see cref="Timeout"/>, when no runner is attached,
/// when the runner detaches, or when the server stops.
/// </para>
/// </remarks>
public sealed class HitlBridge : IHumanApprovalService, IMcpElicitationHandler, IDisposable
{
    /// <summary>The default time a request waits for an answer before it is rejected.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<HitlResponseRequest>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HitlResponseRequest> _preanswered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DeferredWait> _deferred = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _elicitations = new(StringComparer.Ordinal);
    private readonly Func<ApprovalRequest, string, HitlRequestEvent> _format;
    private Func<ServerEvent, CancellationToken, Task>? _publish;
    private Action<DeferredApproval>? _onDeferredAnswer;

    /// <summary>Creates the bridge.</summary>
    /// <param name="timeout">How long a request waits for an answer; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="format">Builds the event sent for a request (given the request and the id it must carry); the default
    /// sends the tool name, arguments, call id, risk level, the verdict's reason as the action, the primary argument (path,
    /// command, url, query) as the target and the approval prompt as the description.</param>
    /// <param name="deferAfter">How long a request holds its turn before it is deferred (<see cref="DeferAfter"/>); null
    /// (the default) never defers.</param>
    public HitlBridge(
        TimeSpan? timeout = null,
        Func<ApprovalRequest, string, HitlRequestEvent>? format = null,
        TimeSpan? deferAfter = null)
    {
        if (timeout is { } t && t <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The approval timeout must be positive.");
        }

        if (deferAfter is { } d && d < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deferAfter), "The approval defer time cannot be negative.");
        }

        Timeout = timeout ?? DefaultTimeout;
        DeferAfter = deferAfter;
        _format = format ?? DefaultFormat;
    }

    /// <summary>How long a request waits for an answer before it is rejected.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// How long a request holds its turn before it is deferred; null never defers. Deferring needs a runner that opens a turn
    /// for the answer (<see cref="AgentServerRunner.DeferredAnswerTurn"/>); without one the request waits in its turn as if
    /// this were null. A value at or above <see cref="Timeout"/> never defers.
    /// </summary>
    public TimeSpan? DeferAfter { get; }

    /// <summary>
    /// Hears each request when it starts waiting and when it is settled; null keeps waits in memory only. A server mode
    /// that records its session sets this to the session's <see cref="Session.SessionTurnRecorder"/>.
    /// </summary>
    public IApprovalWaitLog? WaitLog { get; set; }

    /// <summary>True while a runner is attached — requests can reach a client.</summary>
    public bool IsAttached => Volatile.Read(ref _publish) is not null;

    /// <summary>True while a call waits for its answer inside its turn (not deferred).</summary>
    internal bool HasWaitInTurn => !_pending.IsEmpty;

    /// <summary>Rejects the requests waiting inside their turns now - a server that stops cannot hear their answers.</summary>
    internal void RejectWaitsInTurn(string reason) => RejectAllPending(reason);

    /// <summary>
    /// Called each time a request starts waiting inside its turn (after it is sent) - how a stopping runner hears about a
    /// wait that begins after it stopped reading, and suspends or denies it like the ones already waiting.
    /// </summary>
    internal Action? WaitStarted
    {
        get => Volatile.Read(ref _waitStarted);
        set => Volatile.Write(ref _waitStarted, value);
    }

    private Action? _waitStarted;

    /// <summary>
    /// Attaches the transport that sends events to the client. Returns a handle that detaches it; requests still waiting
    /// when it is detached are rejected.
    /// </summary>
    public IDisposable Attach(Func<ServerEvent, CancellationToken, Task> publish) => Attach(publish, onDeferredAnswer: null);

    /// <summary>
    /// Attaches the transport that sends events to the client, and the runner's way to open a turn for a deferred request's
    /// answer: <paramref name="onDeferredAnswer"/> is called once per deferred request, when it is answered or expires, and
    /// must not block (it queues the turn). Without it requests are never deferred. Returns a handle that detaches both;
    /// requests still waiting in their turn are then rejected, and deferred ones are dropped (with a <see cref="WaitLog"/>
    /// they stay on record for a restarted host).
    /// </summary>
    public IDisposable Attach(Func<ServerEvent, CancellationToken, Task> publish, Action<DeferredApproval>? onDeferredAnswer)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (Interlocked.CompareExchange(ref _publish, publish, null) is not null)
        {
            throw new InvalidOperationException("A runner is already attached to this approval bridge.");
        }

        Volatile.Write(ref _onDeferredAnswer, onDeferredAnswer);
        return new Detacher(this, publish);
    }

    /// <summary>
    /// Delivers a client's answer. Returns false when it matches no waiting request (an unknown id, or no id while several
    /// requests wait) — the answer is then dropped and the request keeps waiting. A deferred request's answer is handed to
    /// the runner, which opens a turn for it.
    /// </summary>
    public bool Resolve(HitlResponseRequest response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Id is { Length: > 0 } id)
        {
            if (_pending.TryRemove(id, out var waiter))
            {
                return waiter.TrySetResult(response);
            }

            return _deferred.TryRemove(id, out var deferred) && Complete(deferred, response, expired: false);
        }

        var waiting = _pending.ToArray();
        var deferredWaits = _deferred.ToArray();
        if (waiting.Length + deferredWaits.Length != 1)
        {
            return false;
        }

        if (waiting.Length == 1)
        {
            return _pending.TryRemove(waiting[0].Key, out var only) && only.TrySetResult(response);
        }

        return _deferred.TryRemove(deferredWaits[0].Key, out var onlyDeferred)
            && Complete(onlyDeferred, response with { Id = onlyDeferred.Wait.RequestId }, expired: false);
    }

    /// <summary>
    /// Delivers a client's answer to an <see cref="ElicitationRequestEvent"/>. Returns false when it matches no waiting request
    /// (an unknown id, or no id while several wait) or its action is not one of <see cref="ElicitationActions"/> — the answer
    /// is then dropped and the request keeps waiting.
    /// </summary>
    public bool ResolveElicitation(ElicitationResponseRequest response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Action is not (ElicitationActions.Accept or ElicitationActions.Decline or ElicitationActions.Cancel))
        {
            return false;
        }

        if (response.Id is { Length: > 0 } id)
        {
            return _elicitations.TryRemove(id, out var waiter) && waiter.TrySetResult(response.Action);
        }

        var waiting = _elicitations.ToArray();
        return waiting.Length == 1
            && _elicitations.TryRemove(waiting[0].Key, out var only)
            && only.TrySetResult(response.Action);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sends an <see cref="ElicitationRequestEvent"/> and waits inside the turn for its <see cref="ElicitationResponseRequest"/>.
    /// Answers <c>cancel</c> without asking when no runner is attached, and after <see cref="Timeout"/>; a request that is not
    /// URL mode is answered <c>decline</c> (only URL mode is advertised). Cancelling the turn cancels the wait.
    /// </remarks>
    public async ValueTask<ElicitResult> ElicitAsync(string pluginName, ElicitRequestParams request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Mode, "url", StringComparison.Ordinal))
        {
            return new ElicitResult { Action = ElicitationActions.Decline };
        }

        var publish = Volatile.Read(ref _publish);
        if (publish is null)
        {
            return new ElicitResult { Action = ElicitationActions.Cancel };
        }

        var id = Guid.NewGuid().ToString("N");
        var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _elicitations[id] = waiter;
        try
        {
            using var timeoutCts = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await using var registration = linked.Token.Register(() => waiter.TrySetCanceled(linked.Token));

            // Registered above, published here: an answer cannot outrun its wait.
            await publish(
                new ElicitationRequestEvent(id, pluginName, "url", request.Message ?? string.Empty, request.Url, request.ElicitationId),
                cancellationToken);

            try
            {
                return new ElicitResult { Action = await waiter.Task };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ElicitResult { Action = ElicitationActions.Cancel };
            }
        }
        finally
        {
            _elicitations.TryRemove(id, out _);
        }
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
    public async Task<HitlResponseRequest> ReofferAsync(ApprovalWaitEntry wait, CancellationToken cancellationToken = default) =>
        (await ReofferCoreAsync(wait, defer: false, cancellationToken))!;

    /// <summary>
    /// Like <see cref="ReofferAsync"/>, but a wait not answered within <see cref="DeferAfter"/> is deferred instead of held:
    /// it returns null, and the answer later reaches the runner as a <see cref="DeferredApproval"/>. Without
    /// <see cref="DeferAfter"/> or a runner that takes deferred answers it never returns null.
    /// </summary>
    /// <param name="wait">The logged wait.</param>
    /// <param name="cancellationToken">Cancels the wait; it stays unsettled.</param>
    public Task<HitlResponseRequest?> ReofferOrDeferAsync(ApprovalWaitEntry wait, CancellationToken cancellationToken = default) =>
        ReofferCoreAsync(wait, defer: true, cancellationToken);

    private async Task<HitlResponseRequest?> ReofferCoreAsync(ApprovalWaitEntry wait, bool defer, CancellationToken cancellationToken)
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

            await publish(ToEvent(wait), cancellationToken);
            WaitStarted?.Invoke();

            try
            {
                if (defer && await DeferredAsync(wait, waiter, cancellationToken))
                {
                    return null;
                }

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

    /// <summary>
    /// Registers a deferred wait a previous process logged (<see cref="ApprovalWaitEntry.Deferred"/>): its answer, or its
    /// <see cref="Timeout"/> counted from the first send, reaches the attached runner as a <see cref="DeferredApproval"/>.
    /// Call it before attaching; the attached runner sends the request again (<see cref="ReofferDeferredAsync"/>).
    /// </summary>
    public void RestoreDeferred(ApprovalWaitEntry wait)
    {
        ArgumentNullException.ThrowIfNull(wait);
        Defer(wait, unsent: true);
    }

    /// <summary>
    /// Sends again, with their original ids, the deferred requests registered with <see cref="RestoreDeferred"/> - what a
    /// runner does right after it attaches, so the client sees what still waits for an answer.
    /// </summary>
    public async Task ReofferDeferredAsync(CancellationToken cancellationToken = default)
    {
        var publish = Volatile.Read(ref _publish);
        if (publish is null)
        {
            return;
        }

        foreach (var deferred in _deferred.Values)
        {
            if (!deferred.TakeUnsent())
            {
                continue;
            }

            // Its time counts from the first send, by a previous process: it may have run out while nothing was running
            var remaining = Timeout - (DateTimeOffset.UtcNow - deferred.Wait.Timestamp);
            if (remaining <= TimeSpan.Zero)
            {
                Expire(deferred.Wait.RequestId);
                continue;
            }

            await publish(ToEvent(deferred.Wait), cancellationToken);
            deferred.StartExpiry(remaining, () => Expire(deferred.Wait.RequestId));
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
            var sentAt = DateTimeOffset.UtcNow;
            if (WaitLog is { } log)
            {
                await log.WaitingAsync(request, sent, cancellationToken);
            }

            await publish(sent, cancellationToken);
            WaitStarted?.Invoke();

            HitlResponseRequest response;
            try
            {
                if (await DeferredAsync(ToEntry(request, sent, sentAt), waiter, cancellationToken))
                {
                    // Not settled: the wait goes on outside the turn, and its answer opens a later one
                    return ApprovalResult.Defer($"request {id}");
                }

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

    /// <summary>Rejects every waiting request and drops the deferred ones; nothing is attached afterwards.</summary>
    public void Dispose()
    {
        Volatile.Write(ref _publish, null);
        Volatile.Write(ref _onDeferredAnswer, null);
        RejectAllPending("the approval bridge was shut down");
        DropDeferred();
    }

    // Waits for the answer until DeferAfter; when none came and deferring is possible, moves the wait out of the turn.
    // Returns true when it was deferred. An answer that wins the race is left on the waiter for the caller to read.
    private async Task<bool> DeferredAsync(
        ApprovalWaitEntry wait, TaskCompletionSource<HitlResponseRequest> waiter, CancellationToken cancellationToken)
    {
        if (DeferAfter is not { } deferAfter || deferAfter >= Timeout - (DateTimeOffset.UtcNow - wait.Timestamp)
            || Volatile.Read(ref _onDeferredAnswer) is null)
        {
            return false;
        }

        var settled = await Task.WhenAny(waiter.Task, Task.Delay(deferAfter, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (settled == waiter.Task || !_pending.TryRemove(wait.RequestId, out _))
        {
            // Answered (or being answered) in time
            return false;
        }

        Defer(wait, unsent: false);
        return true;
    }

    private void Defer(ApprovalWaitEntry wait, bool unsent)
    {
        var deferred = new DeferredWait(wait, unsent);
        _deferred[wait.RequestId] = deferred;

        // A restored wait starts its clock when it is sent again (ReofferDeferredAsync), once a runner can take its expiry
        if (!unsent)
        {
            var remaining = Timeout - (DateTimeOffset.UtcNow - wait.Timestamp);
            deferred.StartExpiry(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, () => Expire(wait.RequestId));
        }
    }

    private void Expire(string requestId)
    {
        if (_deferred.TryRemove(requestId, out var deferred))
        {
            Complete(deferred, new HitlResponseRequest(false, TimedOutReason(), requestId), expired: true);
        }
    }

    private bool Complete(DeferredWait deferred, HitlResponseRequest answer, bool expired)
    {
        deferred.StopExpiry();
        if (Volatile.Read(ref _onDeferredAnswer) is not { } onAnswer)
        {
            // No runner to open the turn: with a wait log the wait stays unsettled on record, for a restarted host
            return false;
        }

        onAnswer(new DeferredApproval(deferred.Wait, answer, expired));
        return true;
    }

    private void DropDeferred()
    {
        foreach (var id in _deferred.Keys)
        {
            if (_deferred.TryRemove(id, out var deferred))
            {
                deferred.StopExpiry();
            }
        }
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

        // An elicitation cannot outlive the wait that holds it (the MCP client retries in-process): it ends as cancel.
        foreach (var id in _elicitations.Keys)
        {
            if (_elicitations.TryRemove(id, out var elicitation))
            {
                elicitation.TrySetResult(ElicitationActions.Cancel);
            }
        }
    }

    private const string NoClientReason = "no client is attached to answer approval requests";

    private string TimedOutReason() =>
        string.Create(CultureInfo.InvariantCulture, $"no answer to the approval request within {Timeout.TotalSeconds:F0}s");

    private static HitlRequestEvent ToEvent(ApprovalWaitEntry wait) =>
        new(wait.RequestId, wait.Action, wait.Target, wait.Description, wait.Tool, wait.Arguments, wait.ToolUseId, wait.Level);

    private static ApprovalWaitEntry ToEntry(ApprovalRequest request, HitlRequestEvent sent, DateTimeOffset sentAt) => new()
    {
        Timestamp = sentAt,
        RequestId = sent.Id,
        ToolUseId = string.IsNullOrEmpty(request.CallId) ? sent.Id : request.CallId,
        Tool = request.ToolName,
        Arguments = sent.Arguments,
        Action = sent.Action,
        Target = sent.Target,
        Description = sent.Description,
        Level = sent.Level,
    };

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
            Level: request.RiskAssessment.Level.ToString().ToLowerInvariant(),
            DelegatedBy: request.DelegatedBy);
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

    private sealed class DeferredWait(ApprovalWaitEntry wait, bool unsent)
    {
        private int _unsent = unsent ? 1 : 0;
        private Timer? _expiry;

        public ApprovalWaitEntry Wait { get; } = wait;

        public bool TakeUnsent() => Interlocked.Exchange(ref _unsent, 0) == 1;

        public void StartExpiry(TimeSpan after, Action expire) =>
            Interlocked.Exchange(ref _expiry, new Timer(_ => expire(), null, after, System.Threading.Timeout.InfiniteTimeSpan))?.Dispose();

        public void StopExpiry() => Interlocked.Exchange(ref _expiry, null)?.Dispose();
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
                Volatile.Write(ref bridge._onDeferredAnswer, null);
                bridge.RejectAllPending("the client connection closed before answering");
                bridge.DropDeferred();
            }
        }
    }
}

/// <summary>
/// The answer to a deferred approval request (<see cref="HitlBridge.DeferAfter"/>) - or its expiry - handed to the runner,
/// which opens a turn that runs the call (<see cref="DeferredApprovalTurn"/>).
/// </summary>
/// <param name="Wait">The request as sent: its id, the call it is about and the call's arguments.</param>
/// <param name="Answer">The client's answer; a rejection when <paramref name="Expired"/>.</param>
/// <param name="Expired">No answer arrived within <see cref="HitlBridge.Timeout"/>, counted from the first send.</param>
public sealed record DeferredApproval(ApprovalWaitEntry Wait, HitlResponseRequest Answer, bool Expired);
