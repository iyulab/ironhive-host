using Microsoft.Extensions.AI;

namespace IronHive.Host.Session;

/// <summary>
/// Manages conversation sessions with JSONL transcript persistence.
/// Compatible with Claude Code session format for interoperability.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Creates a new session for the given project path.
    /// </summary>
    /// <param name="projectPath">The project root directory</param>
    /// <param name="model">The model ID being used</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created session</returns>
    Task<Session> CreateSessionAsync(string projectPath, string model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the session with this ID — loading it when it exists, creating it under this ID when it does not. The
    /// resume key of a long-running process: restarting with the same ID continues the same session.
    /// </summary>
    /// <param name="sessionId">The session ID: letters, digits, '-' and '_' (at most 128).</param>
    /// <param name="projectPath">The project root directory, used when the session is created.</param>
    /// <param name="model">The model ID, used when the session is created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing or new session.</returns>
    /// <exception cref="ArgumentException">The ID is not a valid session ID.</exception>
    Task<Session> OpenSessionAsync(string sessionId, string projectPath, string model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing session by its ID.
    /// </summary>
    /// <param name="sessionId">The session ID to load</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded session, or null if not found</returns>
    Task<Session?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent session for a project.
    /// </summary>
    /// <param name="projectPath">The project root directory</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest session, or null if none exists</returns>
    Task<Session?> GetLatestSessionAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all sessions for a project.
    /// </summary>
    /// <param name="projectPath">The project root directory</param>
    /// <param name="limit">Maximum number of sessions to return</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of sessions, newest first</returns>
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(string projectPath, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a user message to the session transcript.
    /// </summary>
    Task SaveUserMessageAsync(Session session, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an assistant message to the session transcript.
    /// </summary>
    Task SaveAssistantMessageAsync(Session session, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a tool use to the session transcript.
    /// </summary>
    Task SaveToolUseAsync(Session session, string tool, object input, string toolUseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a tool result to the session transcript.
    /// </summary>
    Task SaveToolResultAsync(Session session, string toolUseId, string output, bool isError = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the conversation context from a session.
    /// </summary>
    /// <param name="session">The session to restore</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chat messages for context</returns>
    Task<IReadOnlyList<ChatMessage>> RestoreContextAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forks a session, creating a new session with the same history up to a point.
    /// </summary>
    /// <param name="session">The session to fork</param>
    /// <param name="forkPoint">Optional entry index to fork from (default: latest)</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new forked session</returns>
    Task<Session> ForkSessionAsync(Session session, int? forkPoint = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a session as ended.
    /// </summary>
    /// <param name="session">The session to end</param>
    /// <param name="reason">The reason for ending (user_exit, error, etc.)</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EndSessionAsync(Session session, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session and its transcript.
    /// </summary>
    /// <param name="sessionId">The session ID to delete</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a conversation session.
/// </summary>
public record Session
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The project path this session belongs to.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Path to the JSONL transcript file.
    /// </summary>
    public required string TranscriptPath { get; init; }

    /// <summary>
    /// The model ID used for this session.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the session was last resumed (null if never resumed).
    /// </summary>
    public DateTimeOffset? ResumedAt { get; init; }

    /// <summary>
    /// Current session status.
    /// </summary>
    public SessionStatus Status { get; init; } = SessionStatus.Active;

    /// <summary>
    /// Project hash used for directory organization.
    /// </summary>
    public required string ProjectHash { get; init; }
}

/// <summary>
/// Session status enumeration.
/// </summary>
public enum SessionStatus
{
    /// <summary>Session is currently active.</summary>
    Active,

    /// <summary>Session ended normally by user.</summary>
    Ended,

    /// <summary>Session ended due to an error.</summary>
    Error,

    /// <summary>Session was forked into a new session.</summary>
    Forked
}

/// <summary>
/// Summary information for session listing.
/// </summary>
public record SessionSummary
{
    /// <summary>Session ID.</summary>
    public required string Id { get; init; }

    /// <summary>When the session was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the session was last active.</summary>
    public required DateTimeOffset LastActiveAt { get; init; }

    /// <summary>Current session status.</summary>
    public required SessionStatus Status { get; init; }

    /// <summary>The model used.</summary>
    public required string Model { get; init; }

    /// <summary>Number of messages in the session.</summary>
    public int MessageCount { get; init; }

    /// <summary>First user message (truncated for preview).</summary>
    public string? FirstUserMessage { get; init; }
}
