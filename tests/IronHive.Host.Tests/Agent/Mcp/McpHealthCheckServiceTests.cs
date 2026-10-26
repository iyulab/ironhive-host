using AwesomeAssertions;
using IronHive.Agent.Mcp;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace IronHive.Host.Tests.Agent.Mcp;

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

        await sut.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200, TestContext.Current.CancellationToken);

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

        await sut.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.PluginName.Should().Be("test-plugin");
    }

    [Fact]
    public async Task Tick_with_unhealthy_plugin_should_connect_it_again()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.IsHealthyAsync("test-plugin", Arg.Any<CancellationToken>()).Returns(false);

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(_pluginManager, "@every 1m", timeProvider);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await _pluginManager.Received().ReconnectAsync("test-plugin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_with_healthy_plugin_should_not_reconnect_it()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.IsHealthyAsync("test-plugin", Arg.Any<CancellationToken>()).Returns(true);

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var sut = new McpHealthCheckService(_pluginManager, "@every 1m", timeProvider);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await _pluginManager.DidNotReceive().ReconnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_reconnect_trigger_should_try_the_pending_plugins_on_its_own_schedule()
    {
        _pluginManager.ConnectedPlugins.Returns(Array.Empty<string>());
        _pluginManager.ReconnectPendingAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        // The health check is an hour apart; only the reconnect trigger (every 30 s) can have fired after two minutes.
        await using var sut = new McpHealthCheckService(_pluginManager, "@every 1h", timeProvider);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await _pluginManager.Received().ReconnectPendingAsync(Arg.Any<CancellationToken>());
        await _pluginManager.DidNotReceive().IsHealthyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IsNeeded_is_true_for_a_connected_plugin_and_for_a_plugin_that_is_only_pending()
    {
        _pluginManager.ConnectedPlugins.Returns(SinglePlugin);
        _pluginManager.PendingPlugins.Returns(new Dictionary<string, McpPluginPending>());
        McpHealthCheckService.IsNeeded(_pluginManager).Should().BeTrue();

        _pluginManager.ConnectedPlugins.Returns(Array.Empty<string>());
        _pluginManager.PendingPlugins.Returns(new Dictionary<string, McpPluginPending>
        {
            ["late"] = new("401", 1, DateTimeOffset.UtcNow),
        });
        McpHealthCheckService.IsNeeded(_pluginManager).Should().BeTrue("a server that was not ready at start has nothing connected");
    }

    [Fact]
    public void IsNeeded_is_false_without_a_manager_or_without_any_plugin()
    {
        McpHealthCheckService.IsNeeded(null).Should().BeFalse();

        _pluginManager.ConnectedPlugins.Returns(Array.Empty<string>());
        _pluginManager.PendingPlugins.Returns(new Dictionary<string, McpPluginPending>());
        McpHealthCheckService.IsNeeded(_pluginManager).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_should_not_throw()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sut = new McpHealthCheckService(
            _pluginManager, "@every 1m", timeProvider);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.DisposeAsync();

        true.Should().BeTrue();
    }
}
