using IronHive.Agent.Memory;

namespace IronHive.Cli.Tests.Memory;

public class MemoryRecallResultTests
{
    // TotalCount tests

    [Fact]
    public void TotalCount_Empty_ShouldReturnZero()
    {
        var result = new MemoryRecallResult();

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void TotalCount_OnlyUserMemories_ShouldReturnUserCount()
    {
        var result = new MemoryRecallResult
        {
            UserMemories = [new MemoryItem { Content = "fact1" }, new MemoryItem { Content = "fact2" }]
        };

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public void TotalCount_OnlySessionMemories_ShouldReturnSessionCount()
    {
        var result = new MemoryRecallResult
        {
            SessionMemories = [new MemoryItem { Content = "msg1" }]
        };

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public void TotalCount_Both_ShouldReturnSum()
    {
        var result = new MemoryRecallResult
        {
            UserMemories = [new MemoryItem { Content = "fact" }],
            SessionMemories = [new MemoryItem { Content = "msg1" }, new MemoryItem { Content = "msg2" }]
        };

        Assert.Equal(3, result.TotalCount);
    }

    // FormatAsContext tests

    [Fact]
    public void FormatAsContext_Empty_ShouldReturnEmptyString()
    {
        var result = new MemoryRecallResult();

        Assert.Equal(string.Empty, result.FormatAsContext());
    }

    [Fact]
    public void FormatAsContext_OnlyUserMemories_ShouldReturnUserSection()
    {
        var result = new MemoryRecallResult
        {
            UserMemories =
            [
                new MemoryItem { Content = "User prefers dark mode" },
                new MemoryItem { Content = "User works with C#" }
            ]
        };

        var context = result.FormatAsContext();

        Assert.Contains("## User Knowledge", context);
        Assert.Contains("- User prefers dark mode", context);
        Assert.Contains("- User works with C#", context);
        Assert.DoesNotContain("## Session Context", context);
    }

    [Fact]
    public void FormatAsContext_OnlySessionMemories_ShouldReturnSessionSection()
    {
        var result = new MemoryRecallResult
        {
            SessionMemories =
            [
                new MemoryItem { Content = "Discussed refactoring", Role = "user" },
                new MemoryItem { Content = "Suggested pattern X", Role = "assistant" }
            ]
        };

        var context = result.FormatAsContext();

        Assert.Contains("## Session Context", context);
        Assert.Contains("- [user] Discussed refactoring", context);
        Assert.Contains("- [assistant] Suggested pattern X", context);
        Assert.DoesNotContain("## User Knowledge", context);
    }

    [Fact]
    public void FormatAsContext_BothSections_ShouldContainBoth()
    {
        var result = new MemoryRecallResult
        {
            UserMemories = [new MemoryItem { Content = "fact" }],
            SessionMemories = [new MemoryItem { Content = "msg", Role = "user" }]
        };

        var context = result.FormatAsContext();

        Assert.Contains("## User Knowledge", context);
        Assert.Contains("## Session Context", context);
    }

    [Fact]
    public void FormatAsContext_BothSections_ShouldBeSeparatedByDoubleNewline()
    {
        var result = new MemoryRecallResult
        {
            UserMemories = [new MemoryItem { Content = "fact" }],
            SessionMemories = [new MemoryItem { Content = "msg", Role = "user" }]
        };

        var context = result.FormatAsContext();

        Assert.Contains("\n\n", context);
        var userIndex = context.IndexOf("## User Knowledge", StringComparison.Ordinal);
        var sessionIndex = context.IndexOf("## Session Context", StringComparison.Ordinal);
        Assert.True(userIndex < sessionIndex);
    }

    [Fact]
    public void FormatAsContext_SessionMemory_NullRole_ShouldShowEmpty()
    {
        var result = new MemoryRecallResult
        {
            SessionMemories = [new MemoryItem { Content = "no role message", Role = null }]
        };

        var context = result.FormatAsContext();

        Assert.Contains("- [] no role message", context);
    }

    // MemoryItem defaults

    [Fact]
    public void MemoryItem_Defaults_ShouldBeCorrect()
    {
        var item = new MemoryItem { Content = "test" };

        Assert.Equal("test", item.Content);
        Assert.Null(item.Role);
        Assert.Equal(0f, item.Score);
        Assert.Equal(default, item.CreatedAt);
    }

    [Fact]
    public void MemoryItem_AllProperties_ShouldBeSettable()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new MemoryItem
        {
            Content = "memory content",
            Role = "assistant",
            Score = 0.95f,
            CreatedAt = now
        };

        Assert.Equal("memory content", item.Content);
        Assert.Equal("assistant", item.Role);
        Assert.Equal(0.95f, item.Score);
        Assert.Equal(now, item.CreatedAt);
    }
}
