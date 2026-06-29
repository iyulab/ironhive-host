using IronHive.Agent.Memory;
using IronHive.Host.Core.Context;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Context;

public class LongTermMemoryTests
{
    [Fact]
    public void Constructor_WithoutMemoryService_IsAvailableFalse()
    {
        var manager = new LongTermMemoryManager();

        Assert.False(manager.IsAvailable);
        Assert.Null(manager.SessionId);
    }

    [Fact]
    public void Constructor_WithMemoryService_IsAvailableTrue()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        mockService.SessionId.Returns("test-session");

        var manager = new LongTermMemoryManager(mockService);

        Assert.True(manager.IsAvailable);
        Assert.Equal("test-session", manager.SessionId);
    }

    [Fact]
    public async Task SaveUserMessageAsync_WithService_CallsRemember()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        var manager = new LongTermMemoryManager(mockService);

        await manager.SaveUserMessageAsync("Hello");

        await mockService.Received(1).RememberUserMessageAsync("Hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUserMessageAsync_WithoutService_DoesNothing()
    {
        var manager = new LongTermMemoryManager();

        // Should not throw
        await manager.SaveUserMessageAsync("Hello");
    }

    [Fact]
    public async Task SaveUserMessageAsync_Disabled_DoesNotSave()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        var options = new LongTermMemoryOptions { AutoSaveUserMessages = false };
        var manager = new LongTermMemoryManager(mockService, options);

        await manager.SaveUserMessageAsync("Hello");

        await mockService.DidNotReceive().RememberUserMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAssistantMessageAsync_WithService_CallsRemember()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        var manager = new LongTermMemoryManager(mockService);

        await manager.SaveAssistantMessageAsync("Hi there!");

        await mockService.Received(1).RememberAssistantMessageAsync("Hi there!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecallAsync_WithoutService_ReturnsEmptyResult()
    {
        var manager = new LongTermMemoryManager();

        var result = await manager.RecallAsync("test query");

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task RecallAsync_WithService_ReturnsFilteredResults()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        mockService.RecallAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRecallResult
            {
                UserMemories =
                [
                    new MemoryItem { Content = "High relevance", Score = 0.9f },
                    new MemoryItem { Content = "Low relevance", Score = 0.3f }
                ],
                SessionMemories = []
            });

        var options = new LongTermMemoryOptions { MinRelevanceScore = 0.7f };
        var manager = new LongTermMemoryManager(mockService, options);

        var result = await manager.RecallAsync("test query");

        Assert.Single(result.UserMemories);
        Assert.Equal("High relevance", result.UserMemories[0].Content);
    }

    [Fact]
    public async Task InjectMemoriesAsync_WithoutService_ReturnsOriginal()
    {
        var manager = new LongTermMemoryManager();
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };

        var result = await manager.InjectMemoriesAsync(history, "Hello");

        Assert.Same(history, result);
    }

    [Fact]
    public async Task InjectMemoriesAsync_WithMemories_InjectsAfterSystem()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        mockService.RecallAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRecallResult
            {
                UserMemories =
                [
                    new MemoryItem { Content = "Important fact", Score = 0.95f }
                ],
                SessionMemories = []
            });

        var manager = new LongTermMemoryManager(mockService);
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "You are helpful."),
            new(ChatRole.User, "Hello")
        };

        var result = await manager.InjectMemoriesAsync(history, "Hello");

        Assert.Equal(3, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Equal(ChatRole.System, result[1].Role); // Injected memories
        Assert.Contains("Important fact", result[1].Text);
    }

    [Fact]
    public async Task InjectMemoriesAsync_NoMemories_ReturnsOriginal()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        mockService.RecallAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRecallResult
            {
                UserMemories = [],
                SessionMemories = []
            });

        var manager = new LongTermMemoryManager(mockService);
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };

        var result = await manager.InjectMemoriesAsync(history, "Hello");

        Assert.Same(history, result);
    }

    [Fact]
    public async Task InjectMemoriesAsync_AutoRecallDisabled_ReturnsOriginal()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        var options = new LongTermMemoryOptions { AutoRecall = false };
        var manager = new LongTermMemoryManager(mockService, options);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };

        var result = await manager.InjectMemoriesAsync(history, "Hello");

        Assert.Same(history, result);
        await mockService.DidNotReceive().RecallAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void StartSession_WithService_CallsStart()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        var manager = new LongTermMemoryManager(mockService);

        manager.StartSession("custom-session");

        mockService.Received(1).StartSession("custom-session");
    }

    [Fact]
    public async Task EndSessionAsync_WithService_CallsEnd()
    {
        var mockService = Substitute.For<ISessionMemoryService>();
        var manager = new LongTermMemoryManager(mockService);

        await manager.EndSessionAsync();

        await mockService.Received(1).EndSessionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DefaultOptions_HasCorrectDefaults()
    {
        var options = new LongTermMemoryOptions();

        Assert.True(options.AutoRecall);
        Assert.Equal(5, options.MaxRecallCount);
        Assert.Equal(0.7f, options.MinRelevanceScore);
        Assert.True(options.AutoSaveUserMessages);
        Assert.True(options.AutoSaveAssistantMessages);
    }
}
