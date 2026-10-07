using System.Runtime.CompilerServices;
using System.Text;
using IronHive.Agent.Loop;

namespace IronHive.Host.Session;

/// <summary>
/// Writes each turn of an agent loop to a session transcript as it happens, so a restarted process that opens the same
/// session (<see cref="ISessionManager.OpenSessionAsync"/>) continues the conversation. The user message is written when
/// the turn starts and each tool call when its result arrives; the assistant's text when the turn ends. A turn that
/// ends by an error or cancellation keeps what it wrote and is marked as interrupted, so the model that resumes it
/// knows the turn did not finish.
/// </summary>
public sealed class SessionTurnRecorder
{
    /// <summary>
    /// Appended to the assistant text of a turn that did not finish.
    /// </summary>
    public const string InterruptedMarker = "[turn interrupted before it finished]";

    private readonly ISessionManager _sessions;

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

        var text = new StringBuilder();
        var written = new HashSet<ToolCallResult>(ReferenceEqualityComparer.Instance);
        var finished = false;
        try
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken))
            {
                if (chunk.TextDelta is not null)
                {
                    text.Append(chunk.TextDelta);
                }

                if (chunk.ToolResult is { } arrived && written.Add(arrived))
                {
                    await WriteToolCallAsync(arrived, cancellationToken);
                }

                if (chunk.Turn is not null)
                {
                    foreach (var call in chunk.Turn.ToolCalls)
                    {
                        if (written.Add(call))
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
            // Written even when the caller cancelled: the record of what happened must not depend on how it ended
            if (!finished)
            {
                text.Append(text.Length > 0 ? "\n\n" : string.Empty).Append(InterruptedMarker);
            }

            if (text.Length > 0)
            {
                await _sessions.SaveAssistantMessageAsync(Session, text.ToString(), CancellationToken.None);
            }
        }
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

    private async Task WriteToolCallAsync(ToolCallResult call, CancellationToken cancellationToken)
    {
        var toolUseId = string.IsNullOrEmpty(call.CallId) ? Guid.NewGuid().ToString("N")[..12] : call.CallId;
        await _sessions.SaveToolUseAsync(Session, call.ToolName, call.Arguments, toolUseId, cancellationToken);
        // Success is null when the outcome is unknown (no function-invocation middleware) — only an explicit false is an error
        await _sessions.SaveToolResultAsync(Session, toolUseId, call.Result, call.Success == false, cancellationToken);
    }
}
