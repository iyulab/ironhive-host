using System.Text.Json;
using IronHive.Agent.Mcp;
using IronHive.Host.Session;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// A tool call released while an MCP server's question to the person was unanswered (<c>mcp.elicitation.deferAfterSeconds</c>):
/// the call's entry, the id of the <c>elicitation_request</c> still open to the client, and when that request was first sent
/// (its expiry counts from there, also across a restart).
/// </summary>
/// <param name="RequestId">The id the client answers with.</param>
/// <param name="SentAt">When the request was first sent.</param>
/// <param name="Entry">What is needed to send the call again.</param>
/// <param name="AskedKey">The key of the request in <see cref="McpAwaitingInput.Requests"/> the client was asked; the other requests
/// of the entry are answered <c>cancel</c>. Null when it could not be told: then the first request - the one the client was shown
/// - gets the person's answer and the others <c>cancel</c>; an answer is never given to a question the person did not see.</param>
public sealed record AwaitingInputWait(string RequestId, DateTimeOffset SentAt, McpAwaitingInput Entry, string? AskedKey)
{
    /// <summary>
    /// The answers to send with the call: the person's action for the asked request - with the form content of an accepted
    /// form-mode question - and <c>cancel</c> for any other.
    /// </summary>
    internal IReadOnlyDictionary<string, ElicitResult> AnswersFor(string action, IDictionary<string, JsonElement>? content = null)
    {
        var shown = Shown;
        var answers = new Dictionary<string, ElicitResult>(StringComparer.Ordinal);
        foreach (var key in Entry.Requests.Keys)
        {
            answers[key] = string.Equals(key, shown, StringComparison.Ordinal)
                ? new ElicitResult { Action = action, Content = action == "accept" ? content : null }
                : new ElicitResult { Action = ElicitationActionsCancel };
        }

        return answers;
    }

    /// <summary>The key of the request the client was shown (see <see cref="AskedKey"/>).</summary>
    internal string? Shown => AskedKey ?? Entry.Requests.Keys.FirstOrDefault();

    /// <summary>The request the client was shown, or null when the entry holds none.</summary>
    internal ElicitRequestParams? ShownRequest => Shown is { } key && Entry.Requests.TryGetValue(key, out var request) ? request : null;

    private const string ElicitationActionsCancel = "cancel";

    /// <summary>The transcript form of this wait.</summary>
    public AwaitingInputEntry ToTranscript() => new()
    {
        Timestamp = SentAt,
        RequestId = RequestId,
        ToolUseId = Entry.CallId ?? RequestId,
        Plugin = Entry.PluginName,
        Tool = Entry.ToolName,
        Arguments = Entry.Arguments is null ? null : JsonSerializer.SerializeToElement(Entry.Arguments),
        RequestState = Entry.RequestState,
        Requests = Entry.Requests.ToDictionary(
            r => r.Key, r => JsonSerializer.SerializeToElement(r.Value, McpJsonUtilities.DefaultOptions), StringComparer.Ordinal),
        AskedKey = AskedKey,
    };

    /// <summary>
    /// <see cref="FromTranscript"/> for an entry that may not be readable (written by a newer host, or damaged): false instead of
    /// an exception, so such an entry can be closed without keeping the session from starting.
    /// </summary>
    /// <param name="entry">The logged entry.</param>
    /// <param name="wait">The wait, when it could be read.</param>
    public static bool TryFromTranscript(AwaitingInputEntry entry, out AwaitingInputWait? wait)
    {
        try
        {
            wait = FromTranscript(entry);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            wait = null;
            return false;
        }
    }

    /// <summary>The wait a transcript entry records.</summary>
    public static AwaitingInputWait FromTranscript(AwaitingInputEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var requests = entry.Requests.ToDictionary(
            r => r.Key,
            r => JsonSerializer.Deserialize<ElicitRequestParams>(r.Value, McpJsonUtilities.DefaultOptions)
                ?? throw new JsonException($"The logged request '{r.Key}' is empty."),
            StringComparer.Ordinal);
        IReadOnlyDictionary<string, object?>? arguments = entry.Arguments is { ValueKind: JsonValueKind.Object } json
            ? json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal)
            : null;
        return new AwaitingInputWait(
            entry.RequestId,
            entry.Timestamp,
            new McpAwaitingInput(entry.Plugin, entry.Tool, arguments, entry.RequestState, requests, entry.ToolUseId == entry.RequestId ? null : entry.ToolUseId),
            entry.AskedKey);
    }
}

/// <summary>
/// The answer to a released call's question - or its expiry - handed to the runner, which opens a turn that sends the call
/// again (<see cref="AwaitingInputTurn"/>).
/// </summary>
/// <param name="Wait">The released call and the request it waits on.</param>
/// <param name="Action">The client's answer, one of <see cref="Protocol.ElicitationActions"/>; <c>cancel</c> when expired.</param>
/// <param name="Expired">No answer arrived within <see cref="HitlBridge.Timeout"/>, counted from the first send.</param>
/// <param name="Content">The form content of an accepted form-mode question (already checked against its schema), else null.</param>
public sealed record AwaitingInputAnswer(AwaitingInputWait Wait, string Action, bool Expired, IDictionary<string, JsonElement>? Content = null);

/// <summary>
/// Hears each released call when it starts waiting and when it is settled, so a host can keep it somewhere that outlives the
/// process (<see cref="Session.SessionTurnRecorder"/> writes it to the session).
/// </summary>
public interface IAwaitingInputLog
{
    /// <summary>The call was released; its question stays open to the client.</summary>
    /// <param name="wait">The released call.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AwaitingAsync(AwaitingInputWait wait, CancellationToken cancellationToken);

    /// <summary>The wait was settled - answered or expired. The answer is used once: it is called before the call is sent again.</summary>
    /// <param name="requestId">The id of the settled request.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SettledAsync(string requestId, CancellationToken cancellationToken);
}
