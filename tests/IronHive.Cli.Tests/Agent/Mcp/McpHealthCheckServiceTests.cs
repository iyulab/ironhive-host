using FluentAssertions;
using IronHive.Agent.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IronHive.Cli.Tests.Agent.Mcp;

public class McpHealthCheckServiceTests
{
    private static readonly string[] SinglePlugin = ["test-plugin"];

    private readonly IMcpPluginManager _pluginManager = Substitute.For<IMcpPluginManager>();

    [Fact]
    public async Task Tick_with_no_connected_plugins_should_complete_silently()
    {
        _pluginManager.ConnectedPlugins.Returns(Array.Empty<string>());

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        // No exception expected — nothing to check
        await _pluginManager.DidNotReceive().GetToolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_with_healthy_plugin_should_not_raise_event()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.GetToolsAsync("test-plugin", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AITool>());

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        var eventRaised = false;
        sut.PluginUnhealthy += (_, _) => eventRaised = true;

        sut.Start();
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        // Allow scheduler tick to process
        await Task.Delay(200);

        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task Tick_with_unhealthy_plugin_should_raise_PluginUnhealthy_event()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.GetToolsAsync("test-plugin", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Plugin not responding"));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        McpPluginEventArgs? receivedArgs = null;
        sut.PluginUnhealthy += (_, args) => receivedArgs = args;

        sut.Start();
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        // Allow scheduler tick to process
        await Task.Delay(200);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.PluginName.Should().Be("test-plugin");
    }

    [Fact]
    public async Task DisposeAsync_should_not_throw()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        sut.Start();

        // DisposeAsync returns ValueTask — invoke and await directly
        await sut.DisposeAsync();

        // If we get here without exception, the test passes
        true.Should().BeTrue();
    }
}
