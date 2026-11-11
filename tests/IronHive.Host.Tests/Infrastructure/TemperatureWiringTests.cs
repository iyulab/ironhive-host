using AwesomeAssertions;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>chatBehavior.temperature</c> reaches every model call of a CLI / server loop; unset sends no temperature at all
/// (a model that accepts only its own default sampling answers any other value with HTTP 400).
/// </summary>
public sealed class TemperatureWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "temperature-wiring-" + Guid.NewGuid().ToString("N"));

    public TemperatureWiringTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheSetting_LoadsFromConfigYaml()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), """
            chatBehavior:
              temperature: 0.3
            """);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.ChatBehavior.Temperature.Should().Be(0.3f);
    }

    [Fact]
    public void The_default_is_unset()
        => new ChatBehaviorConfig().Temperature.Should().BeNull();

    [Fact]
    public async Task Unset_sends_no_temperature_on_any_call()
    {
        var (factory, options) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager());

        var loop = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await loop.RunAsync("one", TestContext.Current.CancellationToken);
        await loop.RunAsync("two", TestContext.Current.CancellationToken);

        options.Should().HaveCount(2).And.AllSatisfy(o => o!.Temperature.Should().BeNull());
    }

    [Fact]
    public async Task A_configured_temperature_reaches_every_call()
    {
        var (factory, options) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(), chatBehavior: new ChatBehaviorConfig { Temperature = 0.3f });

        var loop = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await loop.RunAsync("one", TestContext.Current.CancellationToken);
        await loop.RunAsync("two", TestContext.Current.CancellationToken);

        options.Should().HaveCount(2).And.AllSatisfy(o => o!.Temperature.Should().Be(0.3f));
    }

    [Fact]
    public async Task The_factory_option_replaces_the_configured_temperature()
    {
        var (factory, options) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(), chatBehavior: new ChatBehaviorConfig { Temperature = 0.3f });

        var loop = await loops.CreateAsync(
            new AgentLoopFactoryOptions { WorkingDirectory = _root, Temperature = 0.9f }, TestContext.Current.CancellationToken);
        await loop.RunAsync("one", TestContext.Current.CancellationToken);

        options.Should().ContainSingle().Which!.Temperature.Should().Be(0.9f);
    }

    [Fact]
    public async Task A_turns_temperature_replaces_the_configured_one_for_that_turn_only()
    {
        var (factory, options) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(), chatBehavior: new ChatBehaviorConfig { Temperature = 0.3f });

        var loop = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await loop.RunAsync("one", new ChatOptions { Temperature = 1f }, TestContext.Current.CancellationToken);
        await loop.RunAsync("two", TestContext.Current.CancellationToken);

        options.Select(o => o!.Temperature).Should().Equal(1f, 0.3f);
    }

    private static IThinkingTurnManager TurnManager()
    {
        var services = new ServiceCollection();
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();
        return services.BuildServiceProvider().GetRequiredService<IThinkingTurnManager>();
    }

    /// <summary>A chat client factory whose clients answer "ok" and record the options of each request.</summary>
    private static (IChatClientFactory Factory, List<ChatOptions?> Options) RecordingFactory()
    {
        var options = new List<ChatOptions?>();
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var client = Substitute.For<IChatClient>();
            client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    options.Add(call.Arg<ChatOptions?>());
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
                });
            return client;
        });
        return (factory, options);
    }
}
