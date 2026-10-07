using System.Runtime.CompilerServices;
using System.Text;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Host.Protocol;
using IronHive.Host.Server;

namespace IronHive.Host.Session;

/// <summary>
/// Writes each turn of an agent loop to a session transcript as it happens, so a restarted process that opens the same
/// session (<see cref="ISessionManager.OpenSessionAsync"/>) continues the conversation. The user message is written when
/// the turn starts and each tool call when its result arrives; the assistant's text when the turn ends. A turn that
/// ends by an error or cancellation keeps what it wrote and is marked as interrupted, so the model that resumes it
/// knows the turn did not finish.
/// </summary>
/// <remarks>
/// As the <see cref="HitlBridge.WaitLog"/> of the session's approval bridge it also writes each call that waits for a
/// human answer, before the request is sent: the call (<see cref="ToolUseEntry"/>) and the request
/// (<see cref="ApprovalWaitEntry"/>). A turn that stops while such a wait is open is suspended, not interrupted - nothing
/// more is written, and a restarted host offers the wait again (<see cref="SuspendedTurnResumer"/>) and finishes the turn.
/// </remarks>
public sealed class SessionTurnRecorder : IApprovalWaitLog
{
    /// <summary>
    /// Appended to the assistant text of a turn that did not finish.
    /// </summary>
    public const string InterruptedMarker = "[turn interrupted before it finished]";

    private readonly ISessionManager _sessions;

    // Guards the in-memory state below; the transcript writes themselves are appends
    private readonly Lock _state = new();
    private readonly StringBuilder _turnText = new();

    // Calls whose tool use was written when their approval request was sent; their result is written alone
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    // Approval requests sent and not settled yet
    private readonly HashSet<string> _openWaits = new(StringComparer.Ordinal);

    /// <param name="sessions">The session store.</param>
    /// <param name="session">The session the turns belong to.</param>
    public SessionTurnRecorder(ISessionManager sessions, Session session)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// The session the turns are written to.
    /// </summary>
    public Session Session { get; }

    /// <summary>
    /// Passes a streaming turn through unchanged while writing it to the session.
    /// </summary>
    /// <param name="userPrompt">The message the turn answers, as the model received it.</param>
    /// <param name="chunks">The turn's stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<AgentResponseChunk> RecordAsync(
        string userPrompt,
        IAsyncEnumerable<AgentResponseChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentNullException.ThrowIfNull(chunks);

        await _sessions.SaveUserMessageAsync(Session, userPrompt, cancellationToken);
        await foreach (var chunk in RecordTurnAsync(chunks, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Passes the rest of a turn through while writing it: a continuation
    /// (<see cref="IAgentLoop.ContinueStreamingAsync(CancellationToken)"/>) that answers tool results already written, with
    /// no new user message.
    /// </summary>
    /// <param name="chunks">The continuation's stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<AgentResponseChunk> RecordContinuationAsync(
        IAsyncEnumerable<AgentResponseChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return RecordTurnAsync(chunks, cancellationToken);
    }

    /// <summary>
    /// Writes a turn that already completed (the non-streaming path).
    /// </summary>
    public async Task RecordAsync(string userPrompt, AgentResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentNullException.ThrowIfNull(response);

        await _sessions.SaveUserMessageAsync(Session, userPrompt, cancellationToken);
        foreach (var call in response.ToolCalls)
        {
            await WriteToolCallAsync(call, cancellationToken);
        }

        await _sessions.SaveAssistantMessageAsync(Session, response.Content, cancellationToken);
    }

    /// <summary>
    /// Writes the result of a call a previous process left waiting for approval and settles its request - what a resumed
    /// turn writes before it continues.
    /// </summary>
    public async Task WriteResumedResultAsync(
        ApprovalWaitEntry wait, string output, bool isError, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wait);
        await _sessions.SaveToolResultAsync(Session, wait.ToolUseId, output, isError, cancellationToken);
        await _sessions.SaveApprovalClosedAsync(Session, wait.RequestId, CancellationToken.None);
        lock (_state)
        {
            _openWaits.Remove(wait.RequestId);
        }
    }

    /// <inheritdoc />
    async Task IApprovalWaitLog.WaitingAsync(ApprovalRequest request, HitlRequestEvent sent, CancellationToken cancellationToken)
    {
        var toolUseId = string.IsNullOrEmpty(request.CallId) ? sent.Id : request.CallId;
        string? textSoFar = null;
        lock (_state)
        {
            if (_turnText.Length > 0)
            {
                textSoFar = _turnText.ToString();
                _turnText.Clear();
            }

            _announced.Add(toolUseId);
            _openWaits.Add(sent.Id);
        }

        // The text before the call belongs before it in the transcript; written now, since the turn may not end here
        if (textSoFar is not null)
        {
            await _sessions.SaveAssistantMessageAsync(Session, textSoFar, cancellationToken);
        }

        await _sessions.SaveToolUseAsync(
            Session, request.ToolName, (object?)request.Arguments ?? new Dictionary<string, object?>(), toolUseId, cancellationToken);
        await _sessions.SaveApprovalWaitAsync(Session, new ApprovalWaitEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            RequestId = sent.Id,
            ToolUseId = toolUseId,
            Tool = request.ToolName,
            Arguments = sent.Arguments,
            Action = sent.Action,
            Target = sent.Target,
            Description = sent.Description,
            Level = sent.Level,
        }, cancellationToken);
    }

    /// <inheritdoc />
    async Task IApprovalWaitLog.SettledAsync(string requestId, CancellationToken cancellationToken)
    {
        bool open;
        lock (_state)
        {
            open = _openWaits.Remove(requestId);
        }

        if (open)
        {
            await _sessions.SaveApprovalClosedAsync(Session, requestId, cancellationToken);
        }
    }

    private async IAsyncEnumerable<AgentResponseChunk> RecordTurnAsync(
        IAsyncEnumerable<AgentResponseChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (_state)
        {
            _turnText.Clear();
        }

        // A call is reported as its result arrives and again in the turn's final record, as a different object: the call id
        // is the identity (by reference only when a call has none)
        var writtenIds = new HashSet<string>(StringComparer.Ordinal);
        var writtenWithoutId = new HashSet<ToolCallResult>(ReferenceEqualityComparer.Instance);
        bool FirstTime(ToolCallResult call) =>
            string.IsNullOrEmpty(call.CallId) ? writtenWithoutId.Add(call) : writtenIds.Add(call.CallId);
        var finished = false;
        try
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken))
            {
                if (chunk.TextDelta is not null)
                {
                    lock (_state)
                    {
                        _turnText.Append(chunk.TextDelta);
                    }
                }

                if (chunk.ToolResult is { } arrived && FirstTime(arrived))
                {
                    await WriteToolCallAsync(arrived, cancellationToken);
                }

                if (chunk.Turn is not null)
                {
                    foreach (var call in chunk.Turn.ToolCalls)
                    {
                        if (FirstTime(call))
                        {
                            await WriteToolCallAsync(call, cancellationToken);
                        }
                    }
                }

                yield return chunk;
            }

            finished = true;
        }
        finally
        {
            // A turn stopped while a call waits for approval is suspended: the wait is on record and a restarted host
            // finishes the turn, so nothing marks it interrupted. Otherwise the record of what happened is written even
            // when the caller cancelled - it must not depend on how the turn ended.
            string? text;
            lock (_state)
            {
                var suspended = !finished && _openWaits.Count > 0;
                if (!finished && !suspended)
                {
                    _turnText.Append(_turnText.Length > 0 ? "\n\n" : string.Empty).Append(InterruptedMarker);
                }

                text = _turnText.Length > 0 && !suspended ? _turnText.ToString() : null;
                _turnText.Clear();
            }

            if (text is not null)
            {
                await _sessions.SaveAssistantMessageAsync(Session, text, CancellationToken.None);
            }
        }
    }

    private async Task WriteToolCallAsync(ToolCallResult call, CancellationToken cancellationToken)
    {
        var toolUseId = string.IsNullOrEmpty(call.CallId) ? Guid.NewGuid().ToString("N")[..12] : call.CallId;
        bool announced;
        lock (_state)
        {
            announced = _announced.Remove(toolUseId);
        }

        // A call announced with its approval request already has its tool use written
        if (!announced)
        {
            await _sessions.SaveToolUseAsync(Session, call.ToolName, call.Arguments, toolUseId, cancellationToken);
        }

        // Success is null when the outcome is unknown (no function-invocation middleware) — only an explicit false is an error
        await _sessions.SaveToolResultAsync(Session, toolUseId, call.Result, call.Success == false, cancellationToken);
    }
}
