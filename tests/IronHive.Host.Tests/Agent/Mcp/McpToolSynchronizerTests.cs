using AwesomeAssertions;
using IronHive.Agent.Mcp;
using IronHive.Agent.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Agent.Mcp;

/// <summary>
/// A plugin that connects after the loop was built must reach the loop that is already running: its tools
/// are added to the loop's list, replace the tools of its earlier connection, and leave when it disconnects.
/// </summary>
public class McpToolSynchronizerTests
{
    private static AIFunction Tool(string name) => AIFunctionFactory.Create(() => name, name);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AITool>> NothingSeeded =
        new Dictionary<string, IReadOnlyList<AITool>>();

    private readonly IMcpPluginManager _manager = Substitute.For<IMcpPluginManager>();

    private void Connects(string plugin) =>
        _manager.PluginConnected += Raise.EventWith(_manager, new McpPluginEventArgs { PluginName = plugin });

    private void Disconnects(string plugin) =>
        _manager.PluginDisconnected += Raise.EventWith(_manager, new McpPluginEventArgs { PluginName = plugin });

    [Fact]
    public async Task APluginThatConnectsLate_HasItsToolsAddedToTheRunningList()
    {
        var sut = new McpToolSynchronizer(_manager);
        var list = new LiveToolList([Tool("read")]);
        sut.Track(list, NothingSeeded);
        _manager.GetToolsAsync("message", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AITool>>([Tool("message.send")]));

        Connects("message");
        await sut.Idle();

        list.Select(tool => tool.Name).Should().Equal("read", "message.send");
    }

    [Fact]
    public async Task APluginThatConnectsAgain_ReplacesItsEarlierTools_AndLeavesTheOthersAlone()
    {
        var sut = new McpToolSynchronizer(_manager);
        var oldA = Tool("a.old");
        var other = Tool("b.tool");
        var list = new LiveToolList([Tool("read"), oldA, other]);
        sut.Track(list, new Dictionary<string, IReadOnlyList<AITool>> { ["a"] = [oldA], ["b"] = [other] });
        _manager.GetToolsAsync("a", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AITool>>([Tool("a.new")]));

        Connects("a");
        await sut.Idle();

        list.Select(tool => tool.Name).Should().Equal("read", "b.tool", "a.new");
        list.Contains(oldA).Should().BeFalse("it is bound to the connection that was replaced");
    }

    [Fact]
    public async Task APluginThatDisconnects_HasItsToolsTakenOut_SoTheModelIsNotOfferedWhatNobodyAnswers()
    {
        var sut = new McpToolSynchronizer(_manager);
        var tool = Tool("a.tool");
        var list = new LiveToolList([Tool("read"), tool]);
        sut.Track(list, new Dictionary<string, IReadOnlyList<AITool>> { ["a"] = [tool] });

        Disconnects("a");
        await sut.Idle();

        list.Select(t => t.Name).Should().Equal("read");
    }

    [Fact]
    public async Task EveryTrackedList_GetsTheChange()
    {
        var sut = new McpToolSynchronizer(_manager);
        var first = new LiveToolList();
        var second = new LiveToolList();
        sut.Track(first, NothingSeeded);
        sut.Track(second, NothingSeeded);
        _manager.GetToolsAsync("p", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AITool>>([Tool("p.t")]));

        Connects("p");
        await sut.Idle();

        first.Select(t => t.Name).Should().Equal("p.t");
        second.Select(t => t.Name).Should().Equal("p.t");
    }

    [Fact]
    public async Task AListingThatFails_LeavesTheListUntouched_AndTheNextConnectionStillWorks()
    {
        var sut = new McpToolSynchronizer(_manager);
        var list = new LiveToolList([Tool("read")]);
        sut.Track(list, NothingSeeded);
        _manager.GetToolsAsync("p", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<IReadOnlyList<AITool>>(new IOException("gone again")),
            _ => Task.FromResult<IReadOnlyList<AITool>>([Tool("p.t")]));

        Connects("p");
        await sut.Idle();
        list.Select(t => t.Name).Should().Equal("read");

        Connects("p");
        await sut.Idle();
        list.Select(t => t.Name).Should().Equal("read", "p.t");
    }

    [Fact]
    public async Task WithNoListTracked_AnEventDoesNotEvenListTheTools()
    {
        var sut = new McpToolSynchronizer(_manager);

        Connects("p");
        await sut.Idle();

        await _manager.DidNotReceive().GetToolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
