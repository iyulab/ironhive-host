using AwesomeAssertions;
using IronHive.Agent.Loop;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using NSubstitute;
using Xunit;

namespace IronHive.Host.Tests.Server;

public class AgentResponseMapperTests
{
    [Fact]
    public async Task ToServerEvents_TextDelta_YieldsTextDeltaEvent()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk { TextDelta = "hello" });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<TextDeltaEvent>().Which.Content.Should().Be("hello");
        events[1].Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public async Task ToServerEvents_AddendumOnTheFinalChunk_YieldsItsOwnEventBeforeTurnEnd()
    {
        // The observer's note must not leave the host as one more text_delta: a client that
        // aggregates the assistant's text would fold it into the model's words.
        var chunks = ToAsyncEnumerable(
            new AgentResponseChunk { TextDelta = "Task registered." },
            new AgentResponseChunk { Turn = new TurnRecord { Content = "Task registered." }, Addendum = "(no tool was called)" });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(3);
        events[0].Should().BeOfType<TextDeltaEvent>().Which.Content.Should().Be("Task registered.");
        events[1].Should().BeOfType<AddendumEvent>().Which.Content.Should().Be("(no tool was called)");
        events[2].Should().BeOfType<TurnEndEvent>();
        events.OfType<TextDeltaEvent>().Should().ContainSingle("the note is not a text delta");
    }

    [Fact]
    public async Task ToServerEvents_FinalChunkWithoutAddendum_YieldsNoAddendumEvent()
    {
        var chunks = ToAsyncEnumerable(
            new AgentResponseChunk { TextDelta = "hello" },
            new AgentResponseChunk { Turn = new TurnRecord { Content = "hello" } });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.OfType<AddendumEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ToServerEvents_ToolCallNameDelta_YieldsToolStartEvent()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "ReadFile" }
        });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ToolStartEvent>().Which.Tool.Should().Be("ReadFile");
        events[1].Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public async Task ToServerEvents_ToolCallArgumentsOnly_YieldsOnlyTurnEndEvent()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", ArgumentsDelta = "{\"a\":1}" }
        });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.Should().ContainSingle().Which.Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public async Task ToServerEvents_NoChunkCarriesUsage_TurnEndEventHasNullTokens()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk { TextDelta = "hi" });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var turnEnd = events.OfType<TurnEndEvent>().Should().ContainSingle().Which;
        turnEnd.InputTokens.Should().BeNull();
        turnEnd.OutputTokens.Should().BeNull();
        turnEnd.TotalTokens.Should().BeNull();
    }

    [Fact]
    public async Task ToServerEvents_SingleChunkWithUsage_TurnEndEventCarriesThatUsage()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk
        {
            TextDelta = "hi",
            Usage = new TokenUsage { InputTokens = 100, OutputTokens = 20 }
        });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var turnEnd = events.OfType<TurnEndEvent>().Should().ContainSingle().Which;
        turnEnd.InputTokens.Should().Be(100);
        turnEnd.OutputTokens.Should().Be(20);
        turnEnd.TotalTokens.Should().Be(120);
    }

    [Fact]
    public async Task ToServerEvents_MultipleRoundTripsWithUsage_SumsAcrossRoundTrips()
    {
        // A tool-calling turn makes several model round-trips — each round-trip's final chunk
        // carries that round-trip's own usage, not the whole turn's.
        var chunks = ToAsyncEnumerable(
            new AgentResponseChunk { TextDelta = "thinking" },
            new AgentResponseChunk
            {
                ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "ReadFile" },
                Usage = new TokenUsage { InputTokens = 50, OutputTokens = 10 }
            },
            new AgentResponseChunk { TextDelta = "done" },
            new AgentResponseChunk { Usage = new TokenUsage { InputTokens = 80, OutputTokens = 15 } }
        );
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var turnEnd = events.OfType<TurnEndEvent>().Should().ContainSingle().Which;
        turnEnd.InputTokens.Should().Be(130);
        turnEnd.OutputTokens.Should().Be(25);
        turnEnd.TotalTokens.Should().Be(155);
    }

    [Fact]
    public async Task ToServerEvents_WithLogger_CallsProcessChunk()
    {
        var logger = Substitute.For<IExecutionLogger>();
        var chunk = new AgentResponseChunk { TextDelta = "hi" };
        var chunks = ToAsyncEnumerable(chunk);

        await foreach (var _ in chunks.ToServerEvents(logger, ct: TestContext.Current.CancellationToken))
        {
        }

        await logger.Received(1).ProcessChunkAsync(chunk);
    }

    [Fact]
    public async Task ToServerEvents_MixedChunks_YieldsCorrectOrder()
    {
        var chunks = ToAsyncEnumerable(
            new AgentResponseChunk { TextDelta = "A" },
            new AgentResponseChunk
            {
                ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "Glob" }
            },
            new AgentResponseChunk { TextDelta = "B" }
        );
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents(ct: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(4);
        events[0].Should().BeOfType<TextDeltaEvent>().Which.Content.Should().Be("A");
        events[1].Should().BeOfType<ToolStartEvent>().Which.Tool.Should().Be("Glob");
        events[2].Should().BeOfType<TextDeltaEvent>().Which.Content.Should().Be("B");
        events[3].Should().BeOfType<TurnEndEvent>();
    }

    private static async IAsyncEnumerable<AgentResponseChunk> ToAsyncEnumerable(
        params AgentResponseChunk[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
