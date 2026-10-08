using System.Runtime.CompilerServices;
using AwesomeAssertions;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Agent.Providers;
using IronHive.Agent.Tracking;
using IronHive.Cli.Infrastructure;
using IronHive.Cli.Infrastructure.Delegation;
using IronHive.Host.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using CliServices = IronHive.Cli.Infrastructure.ServiceCollectionExtensions;
using ConfigurationManager = IronHive.Host.Config.ConfigurationManager;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>delegation:</c> — Ironbees named agents as tools. A delegated run calls its tools through the session's own
/// pipeline (permission rules, the approval prompt) exactly once per call, spends the session budget, and sends the
/// model its bare model id.
/// </summary>
public sealed class DelegationWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"delegation-wiring-{Guid.NewGuid():N}");

    public DelegationWiringTests()
    {
        var agent = Path.Combine(_root, "agents", "researcher");
        Directory.CreateDirectory(agent);
        File.WriteAllText(Path.Combine(agent, "agent.yaml"), """
            name: researcher
            description: Looks things up in files and writes notes.
            version: 1.0.0
            model:
              deployment: yaml-model
            """);
        File.WriteAllText(Path.Combine(agent, "system-prompt.md"), "You research and write notes.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static DelegationHostConfig Config(string? model = "bare-model", string? provider = null) => new()
    {
        Agents = [new DelegatedAgentHostConfig { Name = "researcher", Model = model, Provider = provider }],
    };

    [Fact]
    public async Task ADelegatedToolCall_AsksThroughTheSessionsApprovalPath_Once_AndADenialDoesNotRunIt()
    {
        var approval = Substitute.For<IHumanApprovalService>();
        approval.RequestApprovalAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalResult.Reject("no"));
        var session = SessionClient(PolicyWith(PermissionAction.Ask), approval);
        var delegated = new ScriptedChatClient(new FunctionCallContent("w", "write_file", new Dictionary<string, object?> { ["path"] = "a.txt" }));
        var write = new CountingTool("write_file");

        var tools = await CreateAsync(Config(), session, FactoryReturning(delegated), [write]);
        var result = await Invoke(tools.Single());

        await approval.ReceivedWithAnyArgs(1).RequestApprovalAsync(default!, TestContext.Current.CancellationToken);
        write.Invocations.Should().Be(0, "a denied call is not run");
        result.Should().Contain("done");
    }

    [Fact]
    public async Task AnAllowedDelegatedToolCall_RunsExactlyOnce_AndTheModelGetsTheBareModelId()
    {
        var session = SessionClient(PolicyWith(PermissionAction.Allow), approval: null);
        var delegated = new ScriptedChatClient(new FunctionCallContent("w", "write_file", new Dictionary<string, object?> { ["path"] = "a.txt" }));
        var write = new CountingTool("write_file");

        var tools = await CreateAsync(Config(model: "bare-model"), session, FactoryReturning(delegated), [write]);
        await Invoke(tools.Single());

        write.Invocations.Should().Be(1, "the agent runtime runs the call; the plain client must not run it again");
        delegated.ModelIds.Should().NotBeEmpty().And.AllBe("bare-model");
    }

    [Fact]
    public async Task TheConfiguredProvider_ServesTheAgentsModel()
    {
        var session = SessionClient(PolicyWith(PermissionAction.Allow), approval: null);
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync("gpustack", "bare-model", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IChatClient>(new ScriptedChatClient(call: null)));

        var tools = await CreateAsync(Config(model: "bare-model", provider: "gpustack"), session, factory, []);
        await Invoke(tools.Single());

        await factory.Received().CreateAsync("gpustack", "bare-model", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADelegatedRun_SpendsTheSessionBudget_AndAReachedLimitRefusesTheNextOne()
    {
        var session = SessionClient(PolicyWith(PermissionAction.Allow), approval: null);
        var limiter = new UsageLimiter(new UsageLimitsConfig { MaxSessionTokens = 100 });
        var delegated = new ScriptedChatClient(call: null, usage: new UsageDetails { InputTokenCount = 90, OutputTokenCount = 30 });

        var tools = await CreateAsync(Config(), session, FactoryReturning(delegated), [], limiter);
        await Invoke(tools.Single());
        var second = await Invoke(tools.Single());

        limiter.CheckLimits().TokensUsed.Should().Be(120, "the run's usage is charged to the session once");
        second.Should().Contain("refused");
    }

    [Fact]
    public async Task ASessionClientWithoutAPipeline_IsRefused_RatherThanRunWithoutTheGate()
    {
        var act = () => CreateAsync(Config(), new ScriptedChatClient(call: null), FactoryReturning(new ScriptedChatClient(call: null)), []);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("tool pipeline");
    }

    [Fact]
    public async Task AnAgentThatIsNotInTheDirectory_FailsAtSessionStart_NamingThePath()
    {
        var session = SessionClient(PolicyWith(PermissionAction.Allow), approval: null);
        var config = new DelegationHostConfig { Agents = [new DelegatedAgentHostConfig { Name = "missing" }] };

        var act = () => CreateAsync(config, session, FactoryReturning(new ScriptedChatClient(call: null)), []);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("missing");
    }

    [Fact]
    public async Task TheLoopFactory_OffersOneToolPerConfiguredAgent_BesideTheSessionsTools()
    {
        var session = SessionClient(PolicyWith(PermissionAction.Allow), approval: null);
        var sessionFactory = FactoryReturning(session);
        var services = new ServiceCollection();
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();
        var loops = new AgentLoopFactory(sessionFactory, services.BuildServiceProvider().GetRequiredService<IThinkingTurnManager>(),
            delegation: Config(), delegationClients: FactoryReturning(new ScriptedChatClient(call: null)));

        var created = await loops.CreateWithToolsAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);

        created.Tools.Should().Contain(t => t.Name == "researcher");
        created.Tools.Should().Contain(t => t.Name == "ReadFile", "the session keeps its own tools");
    }

    [Fact]
    public async Task NoAgents_NoTools()
    {
        var tools = await CreateAsync(new DelegationHostConfig(), new ScriptedChatClient(call: null), Substitute.For<IChatClientFactory>(), []);

        tools.Should().BeEmpty();
    }

    [Fact]
    public void TheDelegationSection_LoadsFromConfigYaml_AndAScopeThatListsAgentsReplacesTheList()
    {
        const string global = """
            delegation:
              agentsDirectory: team
              agents:
                - name: writer
            """;
        const string project = """
            delegation:
              maxDepth: 3
              agents:
                - name: researcher
                  provider: gpustack
                  model: qwen3.8-27b
                  maxToolTurns: 8
            """;
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, "global.yaml"), global);
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), project);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.Delegation.AgentsDirectory.Should().Be("team");
        config.Delegation.MaxDepth.Should().Be(3);
        config.Delegation.Agents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new DelegatedAgentHostConfig { Name = "researcher", Provider = "gpustack", Model = "qwen3.8-27b", MaxToolTurns = 8 });
        ConfigurationManager.FindUnknownKeys(project).Should().BeEmpty();
    }

    private Task<IReadOnlyList<AITool>> CreateAsync(
        DelegationHostConfig config, IChatClient session, IChatClientFactory factory, IList<AITool> pool, IUsageLimiter? limiter = null) =>
        DelegationWiring.CreateToolsAsync(config, _root, session, factory, () => pool, "session-model", limiter,
            TestContext.Current.CancellationToken);

    private static async Task<string> Invoke(AITool tool) =>
        (await ((AIFunction)tool).InvokeAsync(new AIFunctionArguments { ["task"] = "write the note" }, TestContext.Current.CancellationToken))?.ToString() ?? "";

    private static IChatClient SessionClient(IToolCallPolicy policy, IHumanApprovalService? approval) =>
        CliServices.DecorateChatClient(new ScriptedChatClient(call: null), new ChatBehaviorConfig(),
            CliServices.CreateToolInvocationPipeline(policy, approval, loggerFactory: null));

    private static IChatClientFactory FactoryReturning(IChatClient client)
    {
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(client));
        factory.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(client));
        return factory;
    }

    private static IToolCallPolicy PolicyWith(PermissionAction verdict)
    {
        var policy = Substitute.For<IToolCallPolicy>();
        policy.Evaluate(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(verdict == PermissionAction.Allow
                ? RiskAssessment.Safe
                : RiskAssessment.Risky(RiskLevel.High, "test", verdict: verdict));
        return policy;
    }

    private sealed class CountingTool(string name) : AIFunction
    {
        public int Invocations { get; private set; }

        public override string Name => name;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            Invocations++;
            return ValueTask.FromResult<object?>("written");
        }
    }

    /// <summary>
    /// Answers the first request with <paramref name="call"/> (when given) and every later one with «done»; never runs a
    /// tool itself, as a plain provider client does not.
    /// </summary>
    private sealed class ScriptedChatClient(FunctionCallContent? call, UsageDetails? usage = null) : IChatClient
    {
        private int _calls;

        public List<string?> ModelIds { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ModelIds.Add(options?.ModelId);
            var first = _calls++ == 0;
            var message = first && call is not null
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(call.CallId, call.Name, call.Arguments)])
                : new ChatMessage(ChatRole.Assistant, "done");
            return Task.FromResult(new ChatResponse(message) { Usage = usage });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
