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
/// CLI / server loops read the AGENTS.md files of their own working directory (on by default, off with
/// <c>agentsMd.enabled: false</c>), and what they read reaches the model as system instructions.
/// </summary>
public sealed class AgentsMdWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agentsmd-wiring-" + Guid.NewGuid().ToString("N"));

    public AgentsMdWiringTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo-a", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "repo-b", ".git"));
        File.WriteAllText(Path.Combine(_root, "repo-a", "AGENTS.md"), "Rule A: answer in haiku.");
        File.WriteAllText(Path.Combine(_root, "repo-b", "AGENTS.md"), "Rule B: answer in one word.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheAgentsMdSection_LoadsFromConfigYaml_AndIsARecognisedKey()
    {
        const string yaml = """
            agentsMd:
              enabled: false
              maxCharacters: 8000
            """;
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), yaml);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.AgentsMd.Enabled.Should().BeFalse();
        config.AgentsMd.MaxCharacters.Should().Be(8000);
        ConfigurationManager.FindUnknownTopLevelKeys(yaml).Should().BeEmpty();
    }

    [Fact]
    public async Task ByDefault_EachLoop_SendsItsOwnWorkingDirectorysAgentsMd()
    {
        var (factory, sent) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager());

        var a = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = Path.Combine(_root, "repo-a") }, TestContext.Current.CancellationToken);
        var b = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = Path.Combine(_root, "repo-b") }, TestContext.Current.CancellationToken);
        await a.RunAsync("hello", TestContext.Current.CancellationToken);
        await b.RunAsync("hello", TestContext.Current.CancellationToken);

        SystemText(sent[0]).Should().Contain("Rule A").And.NotContain("Rule B");
        SystemText(sent[1]).Should().Contain("Rule B").And.NotContain("Rule A");
    }

    [Fact]
    public async Task Disabled_NoAgentsMdIsSent()
    {
        var (factory, sent) = RecordingFactory();
        var loops = new AgentLoopFactory(factory, TurnManager(), agentsMd: new AgentsMdHostConfig { Enabled = false });

        var a = await loops.CreateAsync(new AgentLoopFactoryOptions { WorkingDirectory = Path.Combine(_root, "repo-a") }, TestContext.Current.CancellationToken);
        await a.RunAsync("hello", TestContext.Current.CancellationToken);

        SystemText(sent[0]).Should().NotContain("Rule A");
    }

    private static string SystemText(IEnumerable<ChatMessage> messages) =>
        string.Join("\n", messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text));

    private static IThinkingTurnManager TurnManager()
    {
        var services = new ServiceCollection();
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();
        return services.BuildServiceProvider().GetRequiredService<IThinkingTurnManager>();
    }

    /// <summary>A chat client factory whose clients answer "ok" and record the messages of each request.</summary>
    private static (IChatClientFactory Factory, List<List<ChatMessage>> Sent) RecordingFactory()
    {
        var sent = new List<List<ChatMessage>>();
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var client = Substitute.For<IChatClient>();
            client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    sent.Add([.. call.Arg<IEnumerable<ChatMessage>>()]);
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
                });
            return client;
        });
        return (factory, sent);
    }
}
