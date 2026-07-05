using FluentAssertions;
using IronHive.Agent.Loop;
using IronHive.Host.Server;
using IronHive.Host.Protocol;
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
        await foreach (var evt in chunks.ToServerEvents())
        {
            events.Add(evt);
        }

        events.Should().ContainSingle()
            .Which.Should().BeOfType<TextDeltaEvent>()
            .Which.Content.Should().Be("hello");
    }

    [Fact]
    public async Task ToServerEvents_ToolCallNameDelta_YieldsToolStartEvent()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", NameDelta = "ReadFile" }
        });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents())
        {
            events.Add(evt);
        }

        events.Should().ContainSingle()
            .Which.Should().BeOfType<ToolStartEvent>()
            .Which.Tool.Should().Be("ReadFile");
    }

    [Fact]
    public async Task ToServerEvents_ToolCallArgumentsOnly_YieldsNothing()
    {
        var chunks = ToAsyncEnumerable(new AgentResponseChunk
        {
            ToolCallDelta = new ToolCallChunk { Id = "tc-001", ArgumentsDelta = "{\"a\":1}" }
        });
        var events = new List<ServerEvent>();
        await foreach (var evt in chunks.ToServerEvents())
        {
            events.Add(evt);
        }

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task ToServerEvents_WithLogger_CallsProcessChunk()
    {
        var logger = Substitute.For<IExecutionLogger>();
        var chunk = new AgentResponseChunk { TextDelta = "hi" };
        var chunks = ToAsyncEnumerable(chunk);

        await foreach (var _ in chunks.ToServerEvents(logger))
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
        await foreach (var evt in chunks.ToServerEvents())
        {
            events.Add(evt);
        }

        events.Should().HaveCount(3);
        events[0].Should().BeOfType<TextDeltaEvent>().Which.Content.Should().Be("A");
        events[1].Should().BeOfType<ToolStartEvent>().Which.Tool.Should().Be("Glob");
        events[2].Should().BeOfType<TextDeltaEvent>().Which.Content.Should().Be("B");
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
