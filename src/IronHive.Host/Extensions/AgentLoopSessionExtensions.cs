using IronHive.Agent.Loop;
using IronHive.Host.Session;
using SessionData = IronHive.Host.Session.Session;

namespace IronHive.Host.Extensions;

/// <summary>
/// Extension methods for integrating IAgentLoop with ISessionManager.
/// Provides simplified session loading and context restoration.
/// </summary>
public static class AgentLoopSessionExtensions
{
    /// <summary>
    /// Loads a session and initializes the agent's conversation history.
    /// Combines session loading and context restoration in one call.
    /// </summary>
    /// <param name="agentLoop">The agent loop to initialize.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="sessionId">The session ID to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SessionNotFoundException">Thrown when session is not found.</exception>
    /// <returns>The loaded session.</returns>
    public static async Task<SessionData> LoadSessionAsync(
        this IAgentLoop agentLoop,
        ISessionManager sessionManager,
        string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentLoop);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await sessionManager.LoadSessionAsync(sessionId, cancellationToken: cancellationToken)
            ?? throw new SessionNotFoundException(sessionId);

        var messages = await sessionManager.RestoreContextAsync(session, cancellationToken: cancellationToken);
        await agentLoop.InitializeHistoryAsync(messages, cancellationToken: cancellationToken);

        return session;
    }

    /// <summary>
    /// Gets or creates a session for the project, then initializes the agent's conversation history.
    /// </summary>
    /// <param name="agentLoop">The agent loop to initialize.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="projectPath">The project path.</param>
    /// <param name="model">The model ID to use for new sessions.</param>
    /// <param name="continueLatest">If true, continues from latest session if available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session (new or existing).</returns>
    public static async Task<SessionData> LoadOrCreateSessionAsync(
        this IAgentLoop agentLoop,
        ISessionManager sessionManager,
        string projectPath,
        string model,
        bool continueLatest = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentLoop);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        SessionData? session = null;

        if (continueLatest)
        {
            session = await sessionManager.GetLatestSessionAsync(projectPath, cancellationToken: cancellationToken);
            if (session != null)
            {
                var messages = await sessionManager.RestoreContextAsync(session, cancellationToken: cancellationToken);
                await agentLoop.InitializeHistoryAsync(messages, cancellationToken: cancellationToken);
                return session;
            }
        }

        // Create new session
        session = await sessionManager.CreateSessionAsync(projectPath, model, cancellationToken: cancellationToken);
        await agentLoop.ClearHistoryAsync(cancellationToken: cancellationToken);

        return session;
    }

    /// <summary>
    /// Saves a conversation turn to the session.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="session">The session to save to.</param>
    /// <param name="userPrompt">The user's prompt.</param>
    /// <param name="response">The agent's response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SaveTurnAsync(
        this ISessionManager sessionManager,
        SessionData session,
        string userPrompt,
        AgentResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(session);

        await new SessionTurnRecorder(sessionManager, session).RecordAsync(userPrompt, response, cancellationToken);
    }
}

/// <summary>
/// Exception thrown when a session is not found.
/// </summary>
public class SessionNotFoundException : Exception
{
    /// <summary>
    /// The session ID that was not found.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Creates a new SessionNotFoundException.
    /// </summary>
    /// <param name="sessionId">The session ID that was not found.</param>
    public SessionNotFoundException(string sessionId)
        : base($"Session not found: {sessionId}")
    {
        SessionId = sessionId;
    }
}
