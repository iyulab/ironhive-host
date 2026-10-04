using AwesomeAssertions;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Cli.Commands;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>chatBehavior.reasoningEffort</c> (and <c>run --reasoning-effort</c>) reaches every model call of a CLI / server
/// loop; unset sends nothing.
/// </summary>
public sealed class ReasoningEffortWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "reasoning-wiring-" + Guid.NewGuid().ToString("N"));

    public ReasoningEffortWiringTests() => Directory.CreateDirectory(_root);

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
              reasoningEffort: none
            """);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.ChatBehavior.ReasoningEffort.Should().Be("none");
    }

    [Theory]
    [InlineData("none", ReasoningEffort.None)]
    [InlineData("low", ReasoningEffort.Low)]
    [InlineData("extra_high", ReasoningEffort.ExtraHigh)]
    public async Task A_configured_level_reaches_every_call(string level, ReasoningEffort expected)
    {
        var (factory, options) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(), chatBehavior: new ChatBehaviorConfig { ReasoningEffort = level });

        var loop = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await loop.RunAsync("one", TestContext.Current.CancellationToken);
        await loop.RunAsync("two", TestContext.Current.CancellationToken);

        options.Should().HaveCount(2).And.AllSatisfy(o => o!.Reasoning!.Effort.Should().Be(expected));
    }

    [Fact]
    public async Task Unset_sends_no_reasoning()
    {
        var (factory, options) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager());

        var loop = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await loop.RunAsync("one", TestContext.Current.CancellationToken);

        options.Should().ContainSingle().Which!.Reasoning.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_level_fails_when_the_loop_is_created_and_names_the_setting()
    {
        var (factory, _) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(), chatBehavior: new ChatBehaviorConfig { ReasoningEffort = "minimal" });

        var act = () => loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).WithMessage("*'minimal'*none, low, medium, high or extra_high*")
            .Which.ParamName.Should().Be("chatBehavior.reasoningEffort");
    }

    [Fact]
    public void The_run_flag_accepts_the_levels_and_refuses_anything_else()
    {
        new RunCommand.Settings { ReasoningEffort = "low" }.Validate().Successful.Should().BeTrue();
        new RunCommand.Settings { ReasoningEffort = "minimal" }.Validate().Successful.Should().BeFalse();
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
