using System.Reflection;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using IronHive.Host.Protocol;

namespace IronHive.Host.Tests.Architecture;

/// <summary>
/// A protocol type is a promise to the client: an event the host says it can send, a request it says
/// it can act on. <c>ToolEndEvent</c> was declared from the start and emitted by nothing, so clients saw
/// tools start and never what they returned; <c>HitlRequestEvent</c> and its response request have
/// existed as long and no runner has ever sent the request. These facts pin every declared type to a
/// site in host source that constructs (events) or consumes (requests) it, and name the ones that
/// are still unconnected so adding a type without a site — or silently leaving one dead — fails here.
/// </summary>
public class ProtocolTypesHaveASiteTests
{
    /// <summary>
    /// Declared events nothing constructs yet. Each entry is a debt with its reason; removing an entry
    /// means the event now has an emit site.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownUnemittedEvents = new Dictionary<string, string>
    {
        ["HitlRequestEvent"] = "server-mode approval bridge to IHumanApprovalService is an open design item; until then Ask is refused with a reason",
        ["AgentSelectedEvent"] = "orchestration routing event with no producer in the host runners",
        ["PlanCreatedServerEvent"] = "planning events have no producer on the server path",
        ["PlanStepStartedServerEvent"] = "planning events have no producer on the server path",
        ["PlanStepCompletedServerEvent"] = "planning events have no producer on the server path",
        ["PlanCompletedServerEvent"] = "planning events have no producer on the server path",
        ["FallbackServerEvent"] = "provider retry/fallback notices are not relayed by the runners yet",
    };

    private static readonly string HostSourceRoot = FindHostSourceRoot();

    private static string FindHostSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IronHive.Host.slnx")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException("IronHive.Host.slnx not found above the test assembly")
            : Path.Combine(dir.FullName, "src");
    }

    private static IEnumerable<string> HostSourceFiles() =>
        Directory.EnumerateFiles(HostSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}IronHive.Host.Protocol{Path.DirectorySeparatorChar}"));

    private static List<string> DerivedTypeNames<TBase>() =>
        typeof(TBase).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType.Name)
            .ToList();

    [Fact]
    public void EveryDeclaredServerEvent_IsConstructedSomewhere_OrListedAsKnownUnemitted()
    {
        var sources = HostSourceFiles().Select(File.ReadAllText).ToList();
        var declared = DerivedTypeNames<ServerEvent>();
        declared.Should().NotBeEmpty();

        var unemitted = declared
            .Where(name => !sources.Any(s => s.Contains($"new {name}(", StringComparison.Ordinal)))
            .ToList();

        unemitted.Should().BeEquivalentTo(KnownUnemittedEvents.Keys,
            "every declared event needs an emit site in host source, or an entry in KnownUnemittedEvents with its reason; " +
            "an entry that now has a site must be removed so the roster stays a list of debts");
    }

    [Fact]
    public void EveryDeclaredServerRequest_IsConsumedSomewhere()
    {
        var sources = HostSourceFiles().Select(File.ReadAllText).ToList();
        var declared = DerivedTypeNames<ServerRequest>();
        declared.Should().NotBeEmpty();

        var unconsumed = declared
            .Where(name => !sources.Any(s =>
                s.Contains($"is {name}", StringComparison.Ordinal) ||
                s.Contains($"case {name}", StringComparison.Ordinal) ||
                s.Contains($"<{name}>", StringComparison.Ordinal)))
            .ToList();

        unconsumed.Should().BeEmpty("a request the client can send must be acted on by a runner");
    }
}
