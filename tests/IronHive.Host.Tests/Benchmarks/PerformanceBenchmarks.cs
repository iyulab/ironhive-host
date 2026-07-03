using System.Diagnostics;
using System.Globalization;
using IronHive.Agent.Context;
using IronHive.Agent.ErrorRecovery;
using IronHive.Agent.Loop;
using IronHive.Agent.Tracking;
using IronHive.Agent.Webhook;
using IronHive.Host.Core.Config;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tests.Benchmarks;

/// <summary>
/// Performance benchmarks for core components.
/// These tests measure execution time and memory usage.
/// </summary>
[Trait("Category", "Benchmark")]
public class PerformanceBenchmarks
{
    [Fact]
    public void TokenCounter_LargeHistory_CompletesQuickly()
    {
        var counter = new ContextTokenCounter("gpt-4o");
        var history = CreateLargeHistory(100);

        var sw = Stopwatch.StartNew();
        var tokens = counter.CountTokens(history);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Token counting took {sw.ElapsedMilliseconds}ms");
        Assert.True(tokens > 0);
    }

    [Fact]
    public void TokenCounter_SingleMessage_SubMillisecond()
    {
        var counter = new ContextTokenCounter("gpt-4o");
        var message = new ChatMessage(ChatRole.User, "Hello, world!");

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            counter.CountTokens(message);
        }
        sw.Stop();

        var avgMs = sw.ElapsedMilliseconds / 1000.0;
        Assert.True(avgMs < 1, string.Format(CultureInfo.InvariantCulture, "Average token counting took {0}ms per message", avgMs));
    }

    [Fact]
    public async Task HistoryCompactor_CompactsLargeHistory_Quickly()
    {
        var counter = new ContextTokenCounter("gpt-4o");
        var options = new HistoryCompactorOptions { PreserveTailTurns = 10 };
        var compactor = new HistoryCompactor(counter, options: options);
        var history = CreateLargeHistory(50);

        var sw = Stopwatch.StartNew();
        var result = await compactor.CompactAsync(history, 5000);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"Compaction took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void GoalReminder_InjectsQuickly()
    {
        var options = new GoalReminderOptions { MinMessagesBeforeReminder = 5 };
        var reminder = new GoalReminder(options);
        var history = CreateLargeHistory(100);
        reminder.CurrentGoal = "Complete the task efficiently.";

        var sw = Stopwatch.StartNew();
        var result = reminder.InjectReminderIfNeeded(history);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100, $"Goal injection took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ErrorRecoveryService_RecordsQuickly()
    {
        var service = new ErrorRecoveryService();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            service.RecordError(new InvalidOperationException($"Test error {i}"));
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"Recording 1000 errors took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ErrorRecoveryService_AnalyzesQuickly()
    {
        var service = new ErrorRecoveryService();
        var error = new ErrorOccurrence
        {
            Message = "Test error",
            Category = ErrorCategory.Network
        };

        // Warm up
        service.AnalyzeError(error);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            service.AnalyzeError(error);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100, $"Analyzing 1000 errors took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void UsageLimiter_ChecksQuickly()
    {
        var config = new UsageLimitsConfig
        {
            MaxSessionTokens = 100000,
            MaxSessionCost = 10.00m
        };
        var limiter = new UsageLimiter(config);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10000; i++)
        {
            limiter.RecordTokenUsage(10, 0.001m);
            limiter.CheckLimits();
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200, $"10000 usage checks took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void WebhookEvent_SerializesQuickly()
    {
        var webhookEvent = new WebhookEvent
        {
            EventType = WebhookEventType.ToolCompleted,
            SessionId = "test-session",
            Data = new Dictionary<string, object?>
            {
                ["toolName"] = "shell",
                ["duration"] = 100,
                ["success"] = true
            }
        };

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10000; i++)
        {
            webhookEvent.ToJson();
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"Serializing 10000 events took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ConfigurationManager_LoadsQuickly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"config-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var manager = new ConfigurationManager(tempDir, Path.Combine(tempDir, "config.yaml"));

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
            {
                manager.Load(forceReload: true);
            }
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000, $"Loading config 100 times took {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ContextManager_PreparesHistoryQuickly()
    {
        var counter = new ContextTokenCounter("gpt-4o");
        var manager = new ContextManager(counter);
        var history = CreateLargeHistory(50);
        manager.SetGoal("Complete the task");

        var sw = Stopwatch.StartNew();
        var result = await manager.PrepareHistoryAsync(history, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"Preparing history took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void PromptCacheManager_AppliesCacheHintsQuickly()
    {
        var counter = new ContextTokenCounter("gpt-4o");
        var manager = new PromptCacheManager(counter);
        var history = CreateLargeHistory(100);

        var sw = Stopwatch.StartNew();
        var result = manager.ApplyCacheHints(history);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200, $"Applying cache hints took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Memory_LargeHistoryCreation_ReasonableMemory()
    {
        var beforeMem = GC.GetTotalMemory(true);
        var history = CreateLargeHistory(1000);
        var afterMem = GC.GetTotalMemory(true);

        var memoryUsedMb = (afterMem - beforeMem) / (1024.0 * 1024.0);

        // 1000 messages should not use more than 50MB
        Assert.True(memoryUsedMb < 50, string.Format(CultureInfo.InvariantCulture, "Creating 1000 messages used {0:F2}MB", memoryUsedMb));
        Assert.Equal(1000, history.Count);
    }

    [Fact]
    public void CompactionTrigger_EvaluatesQuickly()
    {
        var trigger = new ThresholdCompactionTrigger(thresholdPercentage: 0.92f);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            trigger.ShouldCompact(currentTokens: 90000, maxTokens: 100000);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100, $"Evaluating trigger 1000 times took {sw.ElapsedMilliseconds}ms");
    }

    private static List<ChatMessage> CreateLargeHistory(int messageCount)
    {
        var history = new List<ChatMessage>();

        // Add system message
        history.Add(new ChatMessage(ChatRole.System,
            "You are a helpful assistant. " + new string('x', 500)));

        // Add alternating user/assistant messages
        for (var i = 0; i < messageCount - 1; i++)
        {
            var role = i % 2 == 0 ? ChatRole.User : ChatRole.Assistant;
            var content = $"Message {i}: " + new string('y', 100);
            history.Add(new ChatMessage(role, content));
        }

        return history;
    }
}
