using System.Collections.Concurrent;
using System.Text.Json;
using IronHive.Agent.Mcp;
using IronHive.Host.Protocol;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace IronHive.Host.Server;

// A tool call released while an MCP server's question to the person is unanswered (mcp.elicitation.deferAfterSeconds): the
// question stays open to the client under its id, and its answer - or its expiry, counted from the first send - reaches the
// runner as an AwaitingInputAnswer, which opens a turn that sends the call again (AwaitingInputTurn).
public sealed partial class HitlBridge
{
    private readonly ConcurrentDictionary<string, SentElicitation> _sentElicitations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AwaitingSlot> _awaitingInput = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EarlyAnswer> _earlyAnswers = new(StringComparer.Ordinal);

    // Model call id -> request id of a released call, from the moment the manager reports it until the turn that completes it
    // starts: the call's first tool_end must read awaiting_input even when the answer arrives before it is mapped.
    private readonly ConcurrentDictionary<string, string> _awaitingCalls = new(StringComparer.Ordinal);
    private Action<AwaitingInputAnswer>? _onAwaitingInputAnswer;
    private long _elicitationDeferTicks = -1;

    /// <summary>
    /// How long a tool call may hold its turn while the person answers an MCP server's question (the plugin manager's
    /// <see cref="IMcpElicitationHandler.DeferAfter"/>); null (the default) never releases a call. Set it before the plugins
    /// connect (the manager reads it then) and only for a runner that takes the answer's turn
    /// (<see cref="Attach(Func{ServerEvent, CancellationToken, Task}, Action{DeferredApproval}?, Action{AwaitingInputAnswer}?)"/>);
    /// call <see cref="Observe"/> with the plugin manager. The released question waits for its answer for <see cref="Timeout"/>,
    /// counted from when it was sent; a value at or above <see cref="Timeout"/> never releases (the bridge cancels the wait at
    /// that limit first). A host that sets this must map its turns with the
    /// <see cref="AgentResponseMapper"/> overload that takes <see cref="IsAwaitingInput"/>: the overload without it reports a
    /// released call's placeholder result as a finished tool.
    /// </summary>
    public TimeSpan? ElicitationDeferAfter
    {
        get => Interlocked.Read(ref _elicitationDeferTicks) is var ticks and >= 0 ? TimeSpan.FromTicks(ticks) : null;
        set
        {
            if (value is { } v && v < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The elicitation defer time cannot be negative.");
            }

            Interlocked.Exchange(ref _elicitationDeferTicks, value?.Ticks ?? -1);
        }
    }

    /// <inheritdoc />
    TimeSpan? IMcpElicitationHandler.DeferAfter => ElicitationDeferAfter;

    /// <summary>
    /// Hears each released call and its question settled, so a host can keep it somewhere that outlives the process; null keeps
    /// them in memory only. A server mode that records its session sets this to the session's
    /// <see cref="Session.SessionTurnRecorder"/>. A log that throws is not fatal: the wait is then kept in memory only and a
    /// restarted host will not find it.
    /// </summary>
    public IAwaitingInputLog? AwaitingLog { get; set; }

    private bool HasAwaitingInput => !_awaitingInput.IsEmpty;

    /// <summary>
    /// Follows the plugin manager's released calls (<see cref="IMcpPluginManager.CallAwaitingInput"/>): each is tied to the
    /// question the client was asked, logged (<see cref="AwaitingLog"/>) and kept until its answer or expiry. Call it once per
    /// manager, before the plugins connect; dispose the handle to stop.
    /// </summary>
    /// <param name="manager">The plugin manager whose calls may be released.</param>
    public IDisposable Observe(IMcpPluginManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        void Handler(object? sender, McpAwaitingInputEventArgs args) => OnCallAwaiting(args.Awaiting);
        manager.CallAwaitingInput += Handler;
        return new Unsubscriber(() => manager.CallAwaitingInput -= Handler);
    }

    /// <summary>True while a released call with this model call id waits for its question to be answered.</summary>
    /// <param name="callId">The model's id for the call.</param>
    public bool IsAwaitingInput(string? callId) => callId is not null && _awaitingCalls.ContainsKey(callId);

    // The turn that completes a released call starts: its first tool_end has been (or can no longer be) mapped. A newer wait of
    // the same call (the server asked again) keeps its mark.
    internal void ClearAwaitingCall(string? callId, string requestId)
    {
        if (callId is not null)
        {
            _awaitingCalls.TryRemove(new KeyValuePair<string, string>(callId, requestId));
        }
    }

    // A question sent to the client for a call the manager may release: how long the record may wait for the manager's report
    internal TimeSpan UnclaimedSentGrace { get; set; } = TimeSpan.FromSeconds(30);

    private void ReleaseUnclaimedSent(string id) =>
        _ = Task.Delay(UnclaimedSentGrace).ContinueWith(
            _ =>
            {
                if (!_awaitingInput.ContainsKey(id))
                {
                    _sentElicitations.TryRemove(id, out SentElicitation? _);
                    _earlyAnswers.TryRemove(id, out EarlyAnswer? _);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

    /// <summary>
    /// Registers a released call a previous process logged (<see cref="Session.AwaitingInputEntry"/>): its answer, or its
    /// <see cref="Timeout"/> counted from the first send, reaches the attached runner as an <see cref="AwaitingInputAnswer"/>.
    /// Call it before attaching; the attached runner sends the question again (<see cref="ReofferDeferredAsync"/>).
    /// </summary>
    /// <param name="wait">The logged wait.</param>
    public void RestoreAwaitingInput(AwaitingInputWait wait)
    {
        ArgumentNullException.ThrowIfNull(wait);
        _awaitingInput[wait.RequestId] = new AwaitingSlot(wait, unsent: true);
    }

    private void TrackSent(string id, string pluginName, McpCallInfo? call, ElicitRequestParams request)
    {
        // A released question the manager never reported (the turn ended instead) must not pile up
        var oldest = DateTimeOffset.UtcNow - Timeout;
        foreach (var stale in _sentElicitations.Where(e => e.Value.SentAt < oldest && !_elicitations.ContainsKey(e.Key)).Select(e => e.Key).ToList())
        {
            _sentElicitations.TryRemove(stale, out _);
            _earlyAnswers.TryRemove(stale, out _);
        }

        _sentElicitations[id] = new SentElicitation(id, pluginName, call?.CallId, call?.ToolName, request, DateTimeOffset.UtcNow);
    }

    // Raised by the plugin manager after it released a call: tie the entry to the question the client holds, keep and log it.
    private void OnCallAwaiting(McpAwaitingInput entry)
    {
        var sent = FindSent(entry);
        var wait = sent is null
            ? new AwaitingInputWait(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, entry, AskedKeyOf(entry, null))
            : new AwaitingInputWait(sent.Id, sent.SentAt, entry, AskedKeyOf(entry, sent.Request));

        // The handler that asked may still be unwinding: the answer must reach this wait, not its cancelled waiter
        _elicitations.TryRemove(wait.RequestId, out _);

        // Logged first, before the call's placeholder result goes back to the model and before an answer can reach the wait: a
        // process that stops now still finds it, and the settle entry can never precede it. The manager's event is synchronous,
        // so this waits for the write. A log that fails is not fatal: the wait is kept in memory only.
        try
        {
            AwaitingLog?.AwaitingAsync(wait, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // In memory only (documented on AwaitingLog); the question and its answer still work in this process
        }

        var slot = new AwaitingSlot(wait, unsent: false);
        _awaitingInput[wait.RequestId] = slot;
        if (wait.Entry.CallId is { } callId)
        {
            _awaitingCalls[callId] = wait.RequestId;
        }

        _sentElicitations.TryRemove(wait.RequestId, out _);
        var remaining = Timeout - (DateTimeOffset.UtcNow - wait.SentAt);
        slot.StartExpiry(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, () => ExpireAwaitingInput(wait.RequestId));

        if (sent is null)
        {
            // No question was open for this call (cannot happen while the handler is this bridge): offer it now
            _ = PublishAwaitingAsync(wait);
        }

        // An answer that arrived between the release and this registration
        if (_earlyAnswers.TryRemove(wait.RequestId, out var early))
        {
            SettleAwaitingInput(wait.RequestId, early);
        }
    }

    private SentElicitation? FindSent(McpAwaitingInput entry)
    {
        SentElicitation? best = null;
        foreach (var candidate in _sentElicitations.Values)
        {
            // A question whose handler has not unwound yet still counts: the manager's report may come before it does
            if (!string.Equals(candidate.Plugin, entry.PluginName, StringComparison.Ordinal))
            {
                continue;
            }

            var same = entry.CallId is not null && candidate.CallId is not null
                ? string.Equals(candidate.CallId, entry.CallId, StringComparison.Ordinal)
                : string.Equals(candidate.Tool, entry.ToolName, StringComparison.Ordinal);
            if (same && (best is null || candidate.SentAt > best.SentAt))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static string? AskedKeyOf(McpAwaitingInput entry, ElicitRequestParams? asked)
    {
        if (asked is not null)
        {
            foreach (var (key, request) in entry.Requests)
            {
                if (string.Equals(request.Message, asked.Message, StringComparison.Ordinal)
                    && string.Equals(request.Url, asked.Url, StringComparison.Ordinal)
                    && string.Equals(request.ElicitationId, asked.ElicitationId, StringComparison.Ordinal))
                {
                    return key;
                }
            }
        }

        return entry.Requests.Count == 1 ? entry.Requests.Keys.Single() : null;
    }

    // An answer to the question of a released call. An accepted form-mode answer is checked against the schema of the request
    // the client was shown before anything is settled - as for a question still inside its turn, a wrong answer is refused and
    // the question stays open for a correction.
    private bool ResolveAwaitingInput(string id, ElicitationResponseRequest response, out ElicitationRefusal? refusal)
    {
        refusal = null;
        var asked = _awaitingInput.TryGetValue(id, out var open) ? open.Wait.ShownRequest
            : _sentElicitations.TryGetValue(id, out var sent) ? sent.Request
            : null;
        var form = asked is not null && IsFormRequest(asked);
        if (response.Action == ElicitationActions.Accept && form
            && ElicitationFormContent.Validate(asked!, response.Content) is { } invalid)
        {
            refusal = new ElicitationRefusal(id, invalid);
            return false;
        }

        return SettleAwaitingInput(id, new EarlyAnswer(response.Action, response.Action == ElicitationActions.Accept && form ? response.Content : null));
    }

    // A form-mode question as this host asks it: form mode is advertised only with SupportsForms (else it was declined, never sent).
    private bool IsFormRequest(ElicitRequestParams request) =>
        !string.Equals(request.Mode, "url", StringComparison.Ordinal) && request.RequestedSchema is not null && SupportsForms;

    private bool SettleAwaitingInput(string id, EarlyAnswer answer)
    {
        if (_awaitingInput.TryRemove(id, out var slot))
        {
            return CompleteAwaitingInput(slot, answer.Action, answer.Content, expired: false);
        }

        if (!_sentElicitations.ContainsKey(id))
        {
            return false;
        }

        // The call was just released and the manager has not reported it yet: keep the answer for the registration. The first
        // answer is the answer: a second one finds nothing, as it would after the wait was settled.
        if (!_earlyAnswers.TryAdd(id, answer))
        {
            return false;
        }

        if (_awaitingInput.ContainsKey(id) && _earlyAnswers.TryRemove(id, out var kept) && _awaitingInput.TryRemove(id, out var registered))
        {
            return CompleteAwaitingInput(registered, kept.Action, kept.Content, expired: false);
        }

        return true;
    }

    private bool ResolveOnlyAwaitingInput(ElicitationResponseRequest response, out ElicitationRefusal? refusal)
    {
        refusal = null;
        var waiting = _awaitingInput.ToArray();
        return waiting.Length == 1 && ResolveAwaitingInput(waiting[0].Key, response, out refusal);
    }

    private void ExpireAwaitingInput(string requestId)
    {
        if (_awaitingInput.TryRemove(requestId, out var slot))
        {
            CompleteAwaitingInput(slot, ElicitationActions.Cancel, content: null, expired: true);
        }
    }

    private bool CompleteAwaitingInput(AwaitingSlot slot, string action, IDictionary<string, JsonElement>? content, bool expired)
    {
        slot.StopExpiry();
        if (Volatile.Read(ref _onAwaitingInputAnswer) is not { } onAnswer)
        {
            // No runner to open the turn: with a log the wait stays unsettled on record, for a restarted host
            return false;
        }

        onAnswer(new AwaitingInputAnswer(slot.Wait, action, expired, content));
        return true;
    }

    private async Task ReofferAwaitingInputAsync(Func<ServerEvent, CancellationToken, Task> publish, CancellationToken cancellationToken)
    {
        foreach (var slot in _awaitingInput.Values)
        {
            if (!slot.TakeUnsent())
            {
                continue;
            }

            // Its time counts from the first send, by a previous process: it may have run out while nothing was running
            var remaining = Timeout - (DateTimeOffset.UtcNow - slot.Wait.SentAt);
            if (remaining <= TimeSpan.Zero)
            {
                ExpireAwaitingInput(slot.Wait.RequestId);
                continue;
            }

            await publish(ToEvent(slot.Wait), cancellationToken);
            slot.StartExpiry(remaining, () => ExpireAwaitingInput(slot.Wait.RequestId));
        }
    }

    private async Task PublishAwaitingAsync(AwaitingInputWait wait)
    {
        if (Volatile.Read(ref _publish) is not { } publish)
        {
            return;
        }

        try
        {
            await publish(ToEvent(wait), CancellationToken.None);
        }
        catch (Exception)
        {
            // The client is gone; the wait stays on record for a restarted host
        }
    }

    private void DropAwaitingInput()
    {
        foreach (var id in _awaitingInput.Keys)
        {
            if (_awaitingInput.TryRemove(id, out var slot))
            {
                slot.StopExpiry();
            }
        }

        _sentElicitations.Clear();
        _earlyAnswers.Clear();
        _awaitingCalls.Clear();
    }

    // The question offered again (a restarted host, or a call released with no question open): in the mode it was asked in.
    private ElicitationRequestEvent ToEvent(AwaitingInputWait wait)
    {
        var asked = wait.ShownRequest;
        var form = asked is not null && IsFormRequest(asked);
        return new ElicitationRequestEvent(
            wait.RequestId, wait.Entry.PluginName, form ? "form" : "url", asked?.Message ?? string.Empty, asked?.Url, asked?.ElicitationId, wait.Entry.CallId, wait.Entry.ToolName)
        {
            RequestedSchema = form ? JsonSerializer.SerializeToElement(asked!.RequestedSchema, McpJsonUtilities.DefaultOptions) : null,
        };
    }

    private sealed record EarlyAnswer(string Action, IDictionary<string, JsonElement>? Content);

    private sealed record SentElicitation(string Id, string Plugin, string? CallId, string? Tool, ElicitRequestParams Request, DateTimeOffset SentAt);

    private sealed class AwaitingSlot(AwaitingInputWait wait, bool unsent)
    {
        private int _unsent = unsent ? 1 : 0;
        private Timer? _expiry;

        public AwaitingInputWait Wait { get; } = wait;

        public bool TakeUnsent() => Interlocked.Exchange(ref _unsent, 0) == 1;

        public void StartExpiry(TimeSpan after, Action expire) =>
            Interlocked.Exchange(ref _expiry, new Timer(_ => expire(), null, after, System.Threading.Timeout.InfiniteTimeSpan))?.Dispose();

        public void StopExpiry() => Interlocked.Exchange(ref _expiry, null)?.Dispose();
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                unsubscribe();
            }
        }
    }
}
