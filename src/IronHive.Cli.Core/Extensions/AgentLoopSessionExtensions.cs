using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Session;
using SessionData = IronHive.Cli.Core.Session.Session;

namespace IronHive.Cli.Core.Extensions;

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
    /// <exception cref="SessionNotFoundException">Thrown when session is not found.</exception>
    /// <returns>The loaded session.</returns>
    public static async Task<SessionData> LoadSessionAsync(
        this IAgentLoop agentLoop,
        ISessionManager sessionManager,
        string sessionId)
    {
        ArgumentNullException.ThrowIfNull(agentLoop);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await sessionManager.LoadSessionAsync(sessionId)
            ?? throw new SessionNotFoundException(sessionId);

        var messages = await sessionManager.RestoreContextAsync(session);
        agentLoop.InitializeHistory(messages);

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
    /// <returns>The session (new or existing).</returns>
    public static async Task<SessionData> LoadOrCreateSessionAsync(
        this IAgentLoop agentLoop,
        ISessionManager sessionManager,
        string projectPath,
        string model,
        bool continueLatest = false)
    {
        ArgumentNullException.ThrowIfNull(agentLoop);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        SessionData? session = null;

        if (continueLatest)
        {
            session = await sessionManager.GetLatestSessionAsync(projectPath);
            if (session != null)
            {
                var messages = await sessionManager.RestoreContextAsync(session);
                agentLoop.InitializeHistory(messages);
                return session;
            }
        }

        // Create new session
        session = await sessionManager.CreateSessionAsync(projectPath, model);
        agentLoop.ClearHistory();

        return session;
    }

    /// <summary>
    /// Saves a conversation turn to the session.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="session">The session to save to.</param>
    /// <param name="userPrompt">The user's prompt.</param>
    /// <param name="response">The agent's response.</param>
    public static async Task SaveTurnAsync(
        this ISessionManager sessionManager,
        SessionData session,
        string userPrompt,
        AgentResponse response)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentNullException.ThrowIfNull(response);

        // Save user message
        await sessionManager.SaveUserMessageAsync(session, userPrompt);

        // Save tool calls if any
        foreach (var toolCall in response.ToolCalls)
        {
            var toolUseId = Guid.NewGuid().ToString("N")[..12];
            await sessionManager.SaveToolUseAsync(session, toolCall.ToolName, toolCall.Arguments, toolUseId);
            await sessionManager.SaveToolResultAsync(session, toolUseId, toolCall.Result, !toolCall.Success);
        }

        // Save assistant response
        await sessionManager.SaveAssistantMessageAsync(session, response.Content);
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
