using System.Globalization;
using System.Text.Json;
using IronHive.Host.Protocol;

namespace IronHive.Host.Session;

/// <summary>
/// Answers the protocol's read-only session requests (<see cref="ListSessionsRequest"/>, <see cref="ReadSessionRequest"/>)
/// from an <see cref="ISessionManager"/>: no storage of its own, and the wire shapes are the protocol's own, so the stored
/// transcript format stays internal to the host.
/// </summary>
public sealed class StoredSessionReader
{
    /// <summary>Sessions in a page when the request names no limit.</summary>
    public const int DefaultListLimit = 20;

    /// <summary>The most sessions a page holds.</summary>
    public const int MaxListLimit = 100;

    /// <summary>Entries in a page when the request names no limit.</summary>
    public const int DefaultReadLimit = 100;

    /// <summary>The most entries a page holds.</summary>
    public const int MaxReadLimit = 500;

    // How much of the first user message a summary shows in place of a title.
    private const int PreviewLength = 120;

    private readonly ISessionManager _sessions;
    private readonly string _projectPath;

    /// <param name="sessions">The store to read.</param>
    /// <param name="projectPath">The project whose sessions a list request without a project names.</param>
    public StoredSessionReader(ISessionManager sessions, string projectPath)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _projectPath = projectPath ?? throw new ArgumentNullException(nameof(projectPath));
    }

    /// <summary>One page of the stored sessions, newest first.</summary>
    public async Task<ServerEvent> ListAsync(ListSessionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryReadCursor(request.Cursor, out var offset))
        {
            return Unreadable("cursor");
        }

        var limit = Math.Clamp(request.Limit ?? DefaultListLimit, 1, MaxListLimit);
        // The store lists newest first and cannot skip, so it is asked for the page and one more: the extra row says
        // whether a next page exists.
        var rows = await _sessions.ListSessionsAsync(request.ProjectPath ?? _projectPath, offset + limit + 1, cancellationToken);
        var page = rows.Skip(offset).Take(limit).Select(ToItem).ToList();
        var next = rows.Count > offset + limit ? WriteCursor(offset + limit) : null;
        return new SessionListEvent(request.Id, page, next);
    }

    /// <summary>One page of a session's conversation, oldest first; an <see cref="ErrorEvent"/> coded
    /// <see cref="ErrorCodes.SessionNotFound"/> when the session does not exist.</summary>
    public async Task<ServerEvent> ReadAsync(ReadSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryReadCursor(request.Cursor, out var offset))
        {
            return Unreadable("cursor");
        }

        var session = await _sessions.LoadSessionAsync(request.SessionId, cancellationToken);
        if (session is null)
        {
            return new ErrorEvent($"No stored session '{request.SessionId}'.") { Code = ErrorCodes.SessionNotFound };
        }

        var limit = Math.Clamp(request.Limit ?? DefaultReadLimit, 1, MaxReadLimit);
        var entries = (await _sessions.ReadTranscriptAsync(session, cancellationToken))
            .Select(ToItem)
            .OfType<SessionEntryItem>()
            .ToList();
        var page = entries.Skip(offset).Take(limit).ToList();
        var next = entries.Count > offset + limit ? WriteCursor(offset + limit) : null;
        return new SessionPageEvent(request.Id, session.Id, page, next);
    }

    private static SessionSummaryItem ToItem(SessionSummary summary) => new(
        summary.Id,
        summary.CreatedAt,
        summary.LastActiveAt,
        summary.Status.ToString().ToLowerInvariant(),
        summary.Model,
        summary.MessageCount,
        Preview(summary.FirstUserMessage));

    private static string? Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var oneLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= PreviewLength ? oneLine : oneLine[..PreviewLength].TrimEnd() + "…";
    }

    // Only what a conversation view shows: bookkeeping entries (start, end, resume, approval waits) are not part of it.
    private static SessionEntryItem? ToItem(TranscriptEntry entry) => entry switch
    {
        UserMessageEntry user => new SessionEntryItem("user", user.Content),
        AssistantMessageEntry assistant => new SessionEntryItem("assistant", assistant.Content),
        ToolUseEntry use => new SessionEntryItem("tool_use", Tool: use.Tool, Input: ToJson(use.Input), CallId: use.ToolUseId),
        ToolResultEntry result => new SessionEntryItem("tool_result", result.Output, CallId: result.ToolUseId, IsError: result.IsError),
        _ => null,
    };

    private static JsonElement? ToJson(object? input) => input switch
    {
        null => null,
        JsonElement element => element,
        string text => TryParse(text) ?? JsonSerializer.SerializeToElement(text),
        _ => JsonSerializer.SerializeToElement(input),
    };

    private static JsonElement? TryParse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A cursor is the position of the next row. It is opaque to a client; this is the only place that reads it.
    private static string WriteCursor(int offset) => offset.ToString(CultureInfo.InvariantCulture);

    private static bool TryReadCursor(string? cursor, out int offset)
    {
        offset = 0;
        return string.IsNullOrEmpty(cursor)
            || (int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0);
    }

    private static ErrorEvent Unreadable(string what) =>
        new($"The {what} in the request is not one this server issued.") { Code = ErrorCodes.UnreadableRequest };
}
