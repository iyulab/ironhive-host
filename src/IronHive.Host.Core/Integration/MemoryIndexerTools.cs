using System.Text.Json;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Core.Integration;

/// <summary>
/// Provides AI tools that integrate with a memory service for semantic memory operations.
/// </summary>
public class MemoryIndexerTools : IDisposable
{
    private readonly IMemoryToolsProvider _provider;
    private readonly string _defaultUserId;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Creates memory tools with the given provider.
    /// </summary>
    /// <param name="provider">Memory provider implementation</param>
    /// <param name="defaultUserId">Default user ID for memory operations</param>
    public MemoryIndexerTools(IMemoryToolsProvider provider, string defaultUserId = "default")
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _defaultUserId = defaultUserId;
    }

    /// <summary>
    /// Gets all memory-related AI tools.
    /// </summary>
    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            CreateStoreTool(),
            CreateRecallTool(),
            CreateSearchTool(),
            CreateForgetTool()
        ];
    }

    /// <summary>
    /// Creates a tool for storing memories.
    /// </summary>
    private AIFunction CreateStoreTool()
    {
        return AIFunctionFactory.Create(
            async (string content, string? userId, float importance, string? category) =>
            {
                try
                {
                    var result = await _provider.StoreAsync(
                        userId ?? _defaultUserId,
                        content,
                        importance,
                        category);

                    return JsonSerializer.Serialize(result, JsonOptions);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new MemoryOperationResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "memory_store",
            description: "Stores information in long-term memory. Use importance (0.0-1.0) to indicate how critical the information is. Higher importance increases retention.");
    }

    /// <summary>
    /// Creates a tool for recalling memories by semantic similarity.
    /// </summary>
    private AIFunction CreateRecallTool()
    {
        return AIFunctionFactory.Create(
            async (string query, string? userId, int limit) =>
            {
                try
                {
                    var result = await _provider.RecallAsync(
                        userId ?? _defaultUserId,
                        query,
                        limit > 0 ? limit : 5);

                    return JsonSerializer.Serialize(result, JsonOptions);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new MemoryRecallResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "memory_recall",
            description: "Recalls memories semantically related to the query. Returns the most relevant memories based on vector similarity.");
    }

    /// <summary>
    /// Creates a tool for searching memories with filters.
    /// </summary>
    private AIFunction CreateSearchTool()
    {
        return AIFunctionFactory.Create(
            async (string? query, string? userId, string? category, string? tier, int limit) =>
            {
                try
                {
                    var result = await _provider.SearchAsync(
                        userId ?? _defaultUserId,
                        new MemorySearchOptions
                        {
                            Query = query,
                            Category = category,
                            Tier = tier,
                            Limit = limit > 0 ? limit : 10
                        });

                    return JsonSerializer.Serialize(result, JsonOptions);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new MemoryRecallResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "memory_search",
            description: "Searches memories with optional filters. Supports filtering by category, tier (Buffer/Short/Long/Archive), and semantic query.");
    }

    /// <summary>
    /// Creates a tool for forgetting/deleting memories.
    /// </summary>
    private AIFunction CreateForgetTool()
    {
        return AIFunctionFactory.Create(
            async (string memoryId, string? userId) =>
            {
                try
                {
                    var result = await _provider.ForgetAsync(
                        userId ?? _defaultUserId,
                        memoryId);

                    return JsonSerializer.Serialize(result, JsonOptions);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new MemoryOperationResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "memory_forget",
            description: "Removes a specific memory by its ID. Use with caution as this cannot be undone.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Interface for memory tools provider.
/// Implement this to integrate with MemoryIndexer.Sdk or other memory backends.
/// </summary>
public interface IMemoryToolsProvider
{
    /// <summary>
    /// Stores a memory.
    /// </summary>
    Task<MemoryOperationResult> StoreAsync(
        string userId,
        string content,
        float importance,
        string? category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recalls memories by semantic similarity.
    /// </summary>
    Task<MemoryRecallResult> RecallAsync(
        string userId,
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches memories with filters.
    /// </summary>
    Task<MemoryRecallResult> SearchAsync(
        string userId,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets (deletes) a memory.
    /// </summary>
    Task<MemoryOperationResult> ForgetAsync(
        string userId,
        string memoryId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Search options for memory queries.
/// </summary>
public class MemorySearchOptions
{
    /// <summary>
    /// Semantic query string.
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Filter by category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Filter by memory tier (Buffer, Short, Long, Archive).
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int Limit { get; set; } = 10;
}

/// <summary>
/// Result of a memory operation.
/// </summary>
public class MemoryOperationResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Memory ID if applicable.
    /// </summary>
    public string? MemoryId { get; set; }

    /// <summary>
    /// Memory tier if applicable.
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>
    /// Result message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Result of memory recall/search.
/// </summary>
public class MemoryRecallResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of memories found.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Retrieved memories.
    /// </summary>
    public List<MemoryItem>? Memories { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// A memory item.
/// </summary>
public class MemoryItem
{
    /// <summary>
    /// Unique memory ID.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Memory content.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Memory tier.
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>
    /// Category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Confidence score.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// In-memory implementation of IMemoryToolsProvider for testing.
/// </summary>
public class InMemoryToolsProvider : IMemoryToolsProvider
{
    private readonly Dictionary<string, List<MemoryItem>> _memories = new();
    private int _idCounter;

    /// <inheritdoc />
    public Task<MemoryOperationResult> StoreAsync(
        string userId,
        string content,
        float importance,
        string? category,
        CancellationToken cancellationToken = default)
    {
        if (!_memories.TryGetValue(userId, out var userMemories))
        {
            userMemories = [];
            _memories[userId] = userMemories;
        }

        var tier = importance switch
        {
            >= 0.8f => "Archive",
            >= 0.5f => "Long",
            >= 0.2f => "Short",
            _ => "Buffer"
        };

        var memory = new MemoryItem
        {
            Id = $"mem_{Interlocked.Increment(ref _idCounter)}",
            Content = content,
            Tier = tier,
            Category = category,
            Confidence = importance,
            CreatedAt = DateTimeOffset.UtcNow
        };

        userMemories.Add(memory);

        return Task.FromResult(new MemoryOperationResult
        {
            Success = true,
            MemoryId = memory.Id,
            Tier = tier,
            Message = "Memory stored successfully"
        });
    }

    /// <inheritdoc />
    public Task<MemoryRecallResult> RecallAsync(
        string userId,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!_memories.TryGetValue(userId, out var userMemories))
        {
            return Task.FromResult(new MemoryRecallResult
            {
                Success = true,
                Count = 0,
                Memories = []
            });
        }

        // Simple keyword matching for in-memory implementation
        var queryLower = query.ToLowerInvariant();
        var results = userMemories
            .Where(m => m.Content?.Contains(queryLower, StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(m => m.Confidence)
            .Take(limit)
            .ToList();

        return Task.FromResult(new MemoryRecallResult
        {
            Success = true,
            Count = results.Count,
            Memories = results
        });
    }

    /// <inheritdoc />
    public Task<MemoryRecallResult> SearchAsync(
        string userId,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!_memories.TryGetValue(userId, out var userMemories))
        {
            return Task.FromResult(new MemoryRecallResult
            {
                Success = true,
                Count = 0,
                Memories = []
            });
        }

        var query = userMemories.AsEnumerable();

        if (!string.IsNullOrEmpty(options.Query))
        {
            query = query.Where(m =>
                m.Content?.Contains(options.Query, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrEmpty(options.Category))
        {
            query = query.Where(m =>
                m.Category?.Equals(options.Category, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrEmpty(options.Tier))
        {
            query = query.Where(m =>
                m.Tier?.Equals(options.Tier, StringComparison.OrdinalIgnoreCase) == true);
        }

        var results = query
            .OrderByDescending(m => m.Confidence)
            .Take(options.Limit)
            .ToList();

        return Task.FromResult(new MemoryRecallResult
        {
            Success = true,
            Count = results.Count,
            Memories = results
        });
    }

    /// <inheritdoc />
    public Task<MemoryOperationResult> ForgetAsync(
        string userId,
        string memoryId,
        CancellationToken cancellationToken = default)
    {
        if (!_memories.TryGetValue(userId, out var userMemories))
        {
            return Task.FromResult(new MemoryOperationResult
            {
                Success = false,
                Message = "Memory not found"
            });
        }

        var removed = userMemories.RemoveAll(m => m.Id == memoryId);

        return Task.FromResult(new MemoryOperationResult
        {
            Success = removed > 0,
            MemoryId = memoryId,
            Message = removed > 0 ? "Memory forgotten" : "Memory not found"
        });
    }
}
