using IronHive.Host.Integration;

namespace IronHive.Host.Tests.Integration;

/// <summary>
/// Unit tests for MemoryIndexerTools.
/// </summary>
public class MemoryIndexerToolsTests
{
    [Fact]
    public void Constructor_WithNullProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryIndexerTools(null!));
    }

    [Fact]
    public void GetTools_ReturnsAllMemoryTools()
    {
        var provider = new InMemoryToolsProvider();
        using var tools = new MemoryIndexerTools(provider);

        var result = tools.GetTools();

        Assert.Equal(4, result.Count);
        Assert.Contains(result, t => t.Name == "memory_store");
        Assert.Contains(result, t => t.Name == "memory_recall");
        Assert.Contains(result, t => t.Name == "memory_search");
        Assert.Contains(result, t => t.Name == "memory_forget");
    }

    [Fact]
    public void GetTools_HasCorrectDescriptions()
    {
        var provider = new InMemoryToolsProvider();
        using var tools = new MemoryIndexerTools(provider);

        var result = tools.GetTools();

        var storeTool = result.First(t => t.Name == "memory_store");
        Assert.Contains("Stores", storeTool.Description);
        Assert.Contains("importance", storeTool.Description);

        var recallTool = result.First(t => t.Name == "memory_recall");
        Assert.Contains("Recalls", recallTool.Description);
        Assert.Contains("semantic", recallTool.Description);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var provider = new InMemoryToolsProvider();
        var tools = new MemoryIndexerTools(provider);

        tools.Dispose();
        tools.Dispose(); // Should not throw
    }
}

/// <summary>
/// Unit tests for InMemoryToolsProvider.
/// </summary>
public class InMemoryToolsProviderTests
{
    [Fact]
    public async Task StoreAsync_ReturnsSuccessResult()
    {
        var provider = new InMemoryToolsProvider();

        var result = await provider.StoreAsync(
            "user1",
            "Test content",
            0.8f,
            "test-category");

        Assert.True(result.Success);
        Assert.NotNull(result.MemoryId);
        Assert.Equal("Archive", result.Tier); // 0.8f importance -> Archive tier
        Assert.Equal("Memory stored successfully", result.Message);
    }

    [Fact]
    public async Task StoreAsync_AssignsCorrectTiers()
    {
        var provider = new InMemoryToolsProvider();

        // Test tier assignments based on importance
        var bufferResult = await provider.StoreAsync("user1", "low", 0.1f, null);
        Assert.Equal("Buffer", bufferResult.Tier);

        var shortResult = await provider.StoreAsync("user1", "medium-low", 0.3f, null);
        Assert.Equal("Short", shortResult.Tier);

        var longResult = await provider.StoreAsync("user1", "medium", 0.6f, null);
        Assert.Equal("Long", longResult.Tier);

        var archiveResult = await provider.StoreAsync("user1", "high", 0.9f, null);
        Assert.Equal("Archive", archiveResult.Tier);
    }

    [Fact]
    public async Task RecallAsync_ReturnsMatchingMemories()
    {
        var provider = new InMemoryToolsProvider();

        await provider.StoreAsync("user1", "The quick brown fox", 0.8f, null);
        await provider.StoreAsync("user1", "jumps over the lazy dog", 0.7f, null);
        await provider.StoreAsync("user1", "Unrelated content", 0.9f, null);

        var result = await provider.RecallAsync("user1", "fox", 10);

        Assert.True(result.Success);
        Assert.Single(result.Memories!);
        Assert.Contains("fox", result.Memories![0].Content);
    }

    [Fact]
    public async Task RecallAsync_ReturnsEmptyForNoMatches()
    {
        var provider = new InMemoryToolsProvider();

        await provider.StoreAsync("user1", "Some content", 0.5f, null);

        var result = await provider.RecallAsync("user1", "nonexistent", 10);

        Assert.True(result.Success);
        Assert.Empty(result.Memories!);
    }

    [Fact]
    public async Task RecallAsync_ReturnsEmptyForNonexistentUser()
    {
        var provider = new InMemoryToolsProvider();

        var result = await provider.RecallAsync("nonexistent-user", "query", 10);

        Assert.True(result.Success);
        Assert.Empty(result.Memories!);
    }

    [Fact]
    public async Task SearchAsync_FiltersByCategory()
    {
        var provider = new InMemoryToolsProvider();

        await provider.StoreAsync("user1", "Content A", 0.5f, "category-a");
        await provider.StoreAsync("user1", "Content B", 0.6f, "category-b");

        var result = await provider.SearchAsync("user1", new MemorySearchOptions
        {
            Category = "category-a"
        });

        Assert.True(result.Success);
        Assert.Single(result.Memories!);
        Assert.Equal("category-a", result.Memories![0].Category);
    }

    [Fact]
    public async Task SearchAsync_FiltersByTier()
    {
        var provider = new InMemoryToolsProvider();

        await provider.StoreAsync("user1", "Low importance", 0.1f, null);
        await provider.StoreAsync("user1", "High importance", 0.9f, null);

        var result = await provider.SearchAsync("user1", new MemorySearchOptions
        {
            Tier = "Archive"
        });

        Assert.True(result.Success);
        Assert.Single(result.Memories!);
        Assert.Equal("Archive", result.Memories![0].Tier);
    }

    [Fact]
    public async Task ForgetAsync_RemovesMemory()
    {
        var provider = new InMemoryToolsProvider();

        var storeResult = await provider.StoreAsync("user1", "To be forgotten", 0.5f, null);
        var memoryId = storeResult.MemoryId!;

        var forgetResult = await provider.ForgetAsync("user1", memoryId);

        Assert.True(forgetResult.Success);
        Assert.Equal("Memory forgotten", forgetResult.Message);

        // Verify memory is gone
        var searchResult = await provider.SearchAsync("user1", new MemorySearchOptions());
        Assert.Empty(searchResult.Memories!);
    }

    [Fact]
    public async Task ForgetAsync_ReturnsFailureForNonexistentMemory()
    {
        var provider = new InMemoryToolsProvider();

        var result = await provider.ForgetAsync("user1", "nonexistent-id");

        Assert.False(result.Success);
        Assert.Equal("Memory not found", result.Message);
    }
}

/// <summary>
/// Unit tests for MemorySearchOptions.
/// </summary>
public class MemorySearchOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new MemorySearchOptions();

        Assert.Null(options.Query);
        Assert.Null(options.Category);
        Assert.Null(options.Tier);
        Assert.Equal(10, options.Limit);
    }
}

/// <summary>
/// Unit tests for MemoryOperationResult.
/// </summary>
public class MemoryOperationResultTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new MemoryOperationResult
        {
            Success = true,
            MemoryId = "mem_123",
            Tier = "Archive",
            Message = "Success message",
            Error = null
        };

        Assert.True(result.Success);
        Assert.Equal("mem_123", result.MemoryId);
        Assert.Equal("Archive", result.Tier);
        Assert.Equal("Success message", result.Message);
        Assert.Null(result.Error);
    }
}

/// <summary>
/// Unit tests for MemoryItem.
/// </summary>
public class MemoryItemTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var item = new MemoryItem
        {
            Id = "mem_123",
            Content = "Test content",
            Tier = "Long",
            Category = "test",
            Confidence = 0.75f,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal("mem_123", item.Id);
        Assert.Equal("Test content", item.Content);
        Assert.Equal("Long", item.Tier);
        Assert.Equal("test", item.Category);
        Assert.Equal(0.75f, item.Confidence);
        Assert.NotNull(item.CreatedAt);
    }
}
