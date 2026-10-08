using AwesomeAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Agent.Tracking;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The host's <c>budget</c> section: a session limit on tokens and cost, enforced before every model call of a turn (the
/// decorator chain carries an unbound <see cref="UsageLimitChatClient"/>), with one limiter per loop so two sessions do
/// not spend one budget.
/// </summary>
public sealed class BudgetWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "budget-wiring-" + Guid.NewGuid().ToString("N"));

    public BudgetWiringTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheBudgetSection_LoadsFromConfigYaml_AndIsARecognisedKey()
    {
        const string yaml = """
            budget:
              maxSessionTokens: 200000
              maxSessionCost: 1.5
              warningThreshold: 0.9
            """;
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), yaml);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.Budget.MaxSessionTokens.Should().Be(200_000);
        config.Budget.MaxSessionCost.Should().Be(1.5m);
        config.Budget.WarningThreshold.Should().Be(0.9f);
        config.Budget.StopOnLimit.Should().BeTrue();
        ConfigurationManager.FindUnknownKeys(yaml).Should().BeEmpty();
    }

    [Fact]
    public async Task WithABudget_EachLoop_BindsItsOwnLimiter_InsideFunctionInvocation()
    {
        var (factory, clients) = DecoratingFactory();
        var loops = new AgentLoopFactory(factory, Substitute.For<IThinkingTurnManager>(),
            budget: new IronHive.Agent.Tracking.UsageLimitsConfig { MaxSessionTokens = 1_000 });

        await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);

        var limiters = clients.Select(c => c.GetService<UsageLimitChatClient>()!.Limiter).ToList();
        limiters.Should().HaveCount(2).And.OnlyContain(l => l != null);
        limiters[0].Should().NotBeSameAs(limiters[1], "a budget is per session — a second loop must not spend the first one's");
    }

    [Fact]
    public async Task WithoutABudget_TheLimitClientStaysUnbound()
    {
        var (factory, clients) = DecoratingFactory();
        var loops = new AgentLoopFactory(factory, Substitute.For<IThinkingTurnManager>(), budget: new UsageLimitsConfig());

        await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);

        clients.Single().GetService<UsageLimitChatClient>()!.Limiter.Should().BeNull();
    }

    /// <summary>A chat client factory that hands out clients decorated the way the host's factory decorates them.</summary>
    private static (IChatClientFactory Factory, List<IChatClient> Clients) DecoratingFactory()
    {
        var pipeline = ServiceCollectionExtensions.CreateToolInvocationPipeline(
            Substitute.For<IronHive.Agent.Mode.IToolCallPolicy>(), approvalService: null, loggerFactory: null);
        var clients = new List<IChatClient>();
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var client = ServiceCollectionExtensions.DecorateChatClient(Substitute.For<IChatClient>(), new ChatBehaviorConfig(), pipeline);
            clients.Add(client);
            return client;
        });
        return (factory, clients);
    }
}
