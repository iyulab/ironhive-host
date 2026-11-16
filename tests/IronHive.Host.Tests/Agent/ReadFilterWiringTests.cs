using AwesomeAssertions;
using IndexThinking.Agents;
using IronHive.Agent.Loop;
using IronHive.Agent.Permissions;
using IronHive.Agent.Tools;
using IronHive.Host.Tests.Mocks;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace IronHive.Host.Tests.Agent;

/// <summary>
/// The agent's directory tools (<c>ListDirectory</c>, <c>GlobFiles</c>, <c>GrepFiles</c>) leave out what a
/// <see cref="FileToolOptions.ReadFilter"/> hides. The host's permission rules say which folders the model may not read;
/// these facts pin that the host hands that decision to the tools instead of leaving a search from the working directory to
/// walk into a folder the rules refuse for <c>ReadFile</c>. Each fact runs on a real folder.
/// </summary>
public class ReadFilterWiringTests : IDisposable
{
    private const string Needle = "needle-in-the-haystack";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"host-read-filter-{Guid.NewGuid():N}");

    public ReadFilterWiringTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".config-folder"));
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, ".config-folder", "secret.txt"), $"password {Needle}");
        File.WriteAllText(Path.Combine(_dir, "src", "ok.txt"), $"public {Needle}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }

        GC.SuppressFinalize(this);
    }

    private static PermissionConfig DenyConfigFolder(string workingDirectory) => new()
    {
        WorkingDirectory = workingDirectory,
        Read =
        [
            new PermissionRule { Pattern = ".config-folder/**", Action = PermissionAction.Deny },
            new PermissionRule { Pattern = "**", Action = PermissionAction.Allow },
        ],
    };

    private static async Task<string> Call(IEnumerable<AITool> tools, string name, Dictionary<string, object?> arguments)
    {
        var function = (AIFunction)tools.Single(t => (t as AIFunction)?.Name == name);
        return (await function.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken))!.ToString()!;
    }

    private static IronHive.Cli.Infrastructure.AgentLoopFactory Factory(FileToolOptions? fileToolOptions = null, PermissionConfig? permissions = null)
    {
        var clientFactory = Substitute.For<IronHive.Agent.Providers.IChatClientFactory>();
        clientFactory.CreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IChatClient>(new MockChatClient()));
        return new IronHive.Cli.Infrastructure.AgentLoopFactory(
            clientFactory, Substitute.For<IThinkingTurnManager>(), fileToolOptions: fileToolOptions, permissions: permissions);
    }

    [Fact]
    public async Task The_hosts_built_in_tools_carry_the_filter_of_the_options_to_the_directory_tools()
    {
        var options = new FileToolOptions { ReadFilter = path => !path.Contains(".config-folder", StringComparison.Ordinal) };

        var tools = IronHive.Host.Tools.BuiltInTools.GetAll(_dir, oopsService: null, webSearchTool: null, deepResearchTool: null, options);

        (await Call(tools, "ListDirectory", new() { ["path"] = ".", ["recursive"] = true })).Should().Contain("ok.txt").And.NotContain("secret.txt");
        (await Call(tools, "GrepFiles", new() { ["pattern"] = Needle, ["filePattern"] = "**/*.txt", ["path"] = "." })).Should().Contain("ok.txt").And.NotContain("secret.txt");
    }

    [Fact]
    public async Task A_folder_the_permission_rules_deny_is_left_out_of_what_the_directory_tools_list_and_search()
    {
        var created = await Factory(permissions: DenyConfigFolder(_dir)).CreateWithPipelineAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = _dir }, TestContext.Current.CancellationToken);

        var listing = await Call(created.Loop.Tools, "ListDirectory", new() { ["path"] = ".", ["recursive"] = true });
        var glob = await Call(created.Loop.Tools, "GlobFiles", new() { ["pattern"] = "**/*.txt", ["path"] = "." });
        var grep = await Call(created.Loop.Tools, "GrepFiles", new() { ["pattern"] = Needle, ["filePattern"] = "**/*.txt", ["path"] = "." });

        listing.Should().Contain("ok.txt").And.NotContain("secret.txt").And.NotContain(".config-folder");
        glob.Should().Contain("ok.txt").And.NotContain("secret.txt");
        grep.Should().Contain("ok.txt").And.NotContain("secret.txt").And.NotContain("password");
    }

    [Fact]
    public async Task A_filter_the_host_passes_itself_is_kept_over_the_one_derived_from_the_rules()
    {
        var own = new FileToolOptions { ReadFilter = path => !path.Contains("src", StringComparison.Ordinal) };

        var created = await Factory(own, DenyConfigFolder(_dir)).CreateWithPipelineAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = _dir }, TestContext.Current.CancellationToken);

        var listing = await Call(created.Loop.Tools, "ListDirectory", new() { ["path"] = ".", ["recursive"] = true });

        listing.Should().NotContain("ok.txt", "the host's own filter hides src");
        listing.Should().Contain("secret.txt", "and it is the only filter: the rules' one is not added on top");
    }

    [Fact]
    public async Task Without_permission_rules_nothing_is_hidden()
    {
        var created = await Factory().CreateWithPipelineAsync(
            new AgentLoopFactoryOptions { Model = "gpt-4o", WorkingDirectory = _dir }, TestContext.Current.CancellationToken);

        var listing = await Call(created.Loop.Tools, "ListDirectory", new() { ["path"] = ".", ["recursive"] = true });

        listing.Should().Contain("ok.txt").And.Contain("secret.txt");
    }
}
