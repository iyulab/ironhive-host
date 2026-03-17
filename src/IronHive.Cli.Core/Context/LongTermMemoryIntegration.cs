using IronHive.Agent.Memory;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Context;

/// <summary>
/// Options for long-term memory integration.
/// </summary>
public class LongTermMemoryOptions
{
    /// <summary>
    /// Whether to automatically recall memories on each turn.
    /// Default is true.
    /// </summary>
    public bool AutoRecall { get; init; } = true;

    /// <summary>
    /// Maximum number of memories to recall.
    /// Default is 5.
    /// </summary>
    public int MaxRecallCount { get; init; } = 5;

    /// <summary>
    /// Minimum relevance score (0-1) for memories to be included.
    /// Default is 0.7.
    /// </summary>
    public float MinRelevanceScore { get; init; } = 0.7f;

    /// <summary>
    /// Whether to auto-save user messages to memory.
    /// Default is true.
    /// </summary>
    public bool AutoSaveUserMessages { get; init; } = true;

    /// <summary>
    /// Whether to auto-save assistant messages to memory.
    /// Default is true.
    /// </summary>
    public bool AutoSaveAssistantMessages { get; init; } = true;
}

/// <summary>
/// Integrates long-term memory with context management.
/// Provides session-persistent and cross-session memory capabilities.
/// </summary>
public class LongTermMemoryManager
{
    private readonly ISessionMemoryService? _memoryService;
    private readonly LongTermMemoryOptions _options;

    public LongTermMemoryManager(
        ISessionMemoryService? memoryService = null,
        LongTermMemoryOptions? options = null)
    {
        _memoryService = memoryService;
        _options = options ?? new LongTermMemoryOptions();
    }

    /// <summary>
    /// Whether memory service is available.
    /// </summary>
    public bool IsAvailable => _memoryService is not null;

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string? SessionId => _memoryService?.SessionId;

    /// <summary>
    /// Saves a user message to memory if configured.
    /// </summary>
    public async Task SaveUserMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        if (_memoryService is null || !_options.AutoSaveUserMessages)
        {
            return;
        }

        await _memoryService.RememberUserMessageAsync(content, cancellationToken);
    }

    /// <summary>
    /// Saves an assistant message to memory if configured.
    /// </summary>
    public async Task SaveAssistantMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        if (_memoryService is null || !_options.AutoSaveAssistantMessages)
        {
            return;
        }

        await _memoryService.RememberAssistantMessageAsync(content, cancellationToken);
    }

    /// <summary>
    /// Recalls relevant memories for a query.
    /// </summary>
    public async Task<MemoryRecallResult> RecallAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_memoryService is null)
        {
            return new MemoryRecallResult();
        }

        var result = await _memoryService.RecallAsync(query, _options.MaxRecallCount, cancellationToken);

        // Filter by minimum relevance score
        return new MemoryRecallResult
        {
            UserMemories = result.UserMemories
                .Where(m => m.Score >= _options.MinRelevanceScore)
                .ToList(),
            SessionMemories = result.SessionMemories
                .Where(m => m.Score >= _options.MinRelevanceScore)
                .ToList()
        };
    }

    /// <summary>
    /// Injects recalled memories into the conversation context.
    /// </summary>
    public async Task<IReadOnlyList<ChatMessage>> InjectMemoriesAsync(
        IReadOnlyList<ChatMessage> history,
        string currentQuery,
        CancellationToken cancellationToken = default)
    {
        if (_memoryService is null || !_options.AutoRecall)
        {
            return history;
        }

        var memories = await RecallAsync(currentQuery, cancellationToken);

        if (memories.TotalCount == 0)
        {
            return history;
        }

        var memoryContext = memories.FormatAsContext();
        if (string.IsNullOrWhiteSpace(memoryContext))
        {
            return history;
        }

        // Inject memories as a system message after the main system prompt
        var result = new List<ChatMessage>();
        var injected = false;

        foreach (var message in history)
        {
            result.Add(message);

            // Inject after the first system message
            if (!injected && message.Role == ChatRole.System)
            {
                result.Add(new ChatMessage(ChatRole.System,
                    $"[RELEVANT MEMORIES]\n{memoryContext}"));
                injected = true;
            }
        }

        // If no system message was found, prepend the memories
        if (!injected)
        {
            result.Insert(0, new ChatMessage(ChatRole.System,
                $"[RELEVANT MEMORIES]\n{memoryContext}"));
        }

        return result;
    }

    /// <summary>
    /// Starts a new memory session.
    /// </summary>
    public void StartSession(string? sessionId = null)
    {
        _memoryService?.StartSession(sessionId);
    }

    /// <summary>
    /// Ends the current memory session.
    /// </summary>
    public async Task EndSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryService is not null)
        {
            await _memoryService.EndSessionAsync(cancellationToken);
        }
    }
}
