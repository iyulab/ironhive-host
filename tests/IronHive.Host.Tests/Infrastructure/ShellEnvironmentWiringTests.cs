using AwesomeAssertions;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Agent.Loop;
using IronHive.Agent.Providers;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using IronHive.Host.Tests.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The commands the model runs through the shell tool do not see the provider API keys the host reads (removed by
/// default), the <c>shell.environment</c> section widens or narrows that, and everything else is still inherited.
/// </summary>
/// <remarks>These tests set provider variables on the process, so they run outside the parallel collections.</remarks>
[Collection(nameof(ProcessWorkingDirectoryScope))]
public sealed class ShellEnvironmentWiringTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("shell-env-wiring").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Echo(string name) => OperatingSystem.IsWindows() ? $"echo %{name}%" : $"echo ${name}";

    [Fact]
    public void TheShellSection_LoadsFromConfigYaml_AndIsARecognisedKey()
    {
        const string yaml = """
            shell:
              environment:
                remove: [DATABASE_PASSWORD]
                passthrough: [OPENAI_API_KEY]
            """;
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), yaml);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        config.Shell.Environment.Remove.Should().Equal("DATABASE_PASSWORD");
        config.Shell.Environment.Passthrough.Should().Equal("OPENAI_API_KEY");
        ConfigurationManager.FindUnknownKeys(yaml).Should().BeEmpty();
    }

    [Fact]
    public void ByDefault_EveryProviderCredentialVariable_IsRemoved()
    {
        var options = new ShellHostConfig().ToToolOptions();

        options.RemoveEnvironmentVariables.Should().BeEquivalentTo(ConfigurationManager.ProviderCredentialVariables);
    }

    [Fact]
    public void Remove_Adds_And_Passthrough_Keeps_EvenWhenBothNameIt()
    {
        var shell = new ShellHostConfig
        {
            Environment = new ShellEnvironmentHostConfig
            {
                Remove = ["DATABASE_PASSWORD", "OPENAI_API_KEY"],
                Passthrough = ["OPENAI_API_KEY"],
            },
        };

        var removed = shell.ToToolOptions().RemoveEnvironmentVariables;

        removed.Should().Contain("DATABASE_PASSWORD").And.Contain("ANTHROPIC_API_KEY");
        removed.Should().NotContain("OPENAI_API_KEY");
    }

    /// <summary>
    /// The list is the variables the host reads keys from: with each of them set, every provider section's API key is
    /// filled. A provider whose key variable is read but not listed would leave its key empty here.
    /// </summary>
    [Fact]
    public void ProviderCredentialVariables_CoverEveryProviderApiKeyTheHostReads()
    {
        using var env = new ScopedEnv([.. ConfigurationManager.ProviderCredentialVariables.Select(name => (name, "key-from-" + name))]);

        var config = new ConfigurationManager(_root, Path.Combine(_root, "global.yaml")).Load(forceReload: true);

        var keyed = typeof(IronHiveConfig).GetProperties()
            .Select(p => (Section: p.Name, ApiKey: p.PropertyType.GetProperty("ApiKey")?.GetValue(p.GetValue(config)) as string,
                HasKey: p.PropertyType.GetProperty("ApiKey") is not null))
            .Where(s => s.HasKey)
            .ToList();
        keyed.Should().NotBeEmpty();
        keyed.Should().AllSatisfy(s => s.ApiKey.Should().StartWith("key-from-", $"the {s.Section} key must come from a listed variable"));
    }

    [Fact]
    public async Task ThroughTheFactory_AShellCommand_DoesNotSeeTheProviderKey_ButSeesTheRest()
    {
        var plain = "IRONHIVE_SHELL_PLAIN_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var env = new ScopedEnv(("OPENAI_API_KEY", "sk-provider-secret-d14"), (plain, "plain-value-d14"));

        var results = await RunCommandsThroughFactory(new ShellHostConfig(), Echo("OPENAI_API_KEY"), Echo(plain));

        results[0].Should().NotContain("sk-provider-secret-d14");
        results[1].Should().Contain("plain-value-d14");
    }

    [Fact]
    public async Task ThroughTheFactory_Passthrough_KeepsTheProviderKey()
    {
        using var env = new ScopedEnv(("OPENAI_API_KEY", "sk-provider-secret-d14"));
        var shell = new ShellHostConfig { Environment = new ShellEnvironmentHostConfig { Passthrough = ["OPENAI_API_KEY"] } };

        var results = await RunCommandsThroughFactory(shell, Echo("OPENAI_API_KEY"));

        results[0].Should().Contain("sk-provider-secret-d14");
    }

    /// <summary>
    /// Builds a loop with <see cref="AgentLoopFactory"/> and runs each command through the shell tool the loop offers the
    /// model — the tool instance the CLI and <c>run --server</c> loops call — returning what each call gives back.
    /// </summary>
    private async Task<List<string>> RunCommandsThroughFactory(ShellHostConfig shell, params string[] commands)
    {
        var factory = Substitute.For<IChatClientFactory>();
        factory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IChatClient>());
        var loops = new AgentLoopFactory(factory, TurnManager(), shell: shell);

        var created = await loops.CreateWithPipelineAsync(new AgentLoopFactoryOptions { WorkingDirectory = _root }, TestContext.Current.CancellationToken);
        var execute = created.Loop.Tools.OfType<AIFunction>().Single(t => t.Name == "ExecuteCommand");

        var results = new List<string>();
        foreach (var command in commands)
        {
            var result = await execute.InvokeAsync(new AIFunctionArguments { ["command"] = command }, TestContext.Current.CancellationToken);
            results.Add(result?.ToString() ?? string.Empty);
        }

        return results;
    }

    private static IThinkingTurnManager TurnManager()
    {
        var services = new ServiceCollection();
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();
        return services.BuildServiceProvider().GetRequiredService<IThinkingTurnManager>();
    }
}
