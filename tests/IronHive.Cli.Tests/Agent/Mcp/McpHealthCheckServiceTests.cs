using FluentAssertions;
using IronHive.Agent.Mcp;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

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
        await _pluginManager.DidNotReceive().IsHealthyAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_with_healthy_plugin_should_not_raise_event()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.IsHealthyAsync("test-plugin", Arg.Any<CancellationToken>())
            .Returns(true);

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        var eventRaised = false;
        sut.PluginUnhealthy += (_, _) => eventRaised = true;

        sut.Start();
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200);

        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task Tick_with_unhealthy_plugin_should_raise_PluginUnhealthy_event()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.IsHealthyAsync("test-plugin", Arg.Any<CancellationToken>())
            .Returns(false);

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        McpPluginEventArgs? receivedArgs = null;
        sut.PluginUnhealthy += (_, args) => receivedArgs = args;

        sut.Start();
        timeProvider.Advance(TimeSpan.FromMinutes(2));
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
        await sut.DisposeAsync();

        true.Should().BeTrue();
    }
}
