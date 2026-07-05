using System.Reflection;
using FluentAssertions;
using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Architecture;

/// <summary>
/// D14 (ISSUE-ironhive-host-20260705-130001): host previously forked and diverged from
/// three <c>IronHive.Agent</c> clusters (SubAgent, Tools.SubAgentTool/TodoTool, Ironbees).
/// Regression teeth so the fork does not silently grow back — host must consume the
/// canonical agent types rather than re-defining them under its own namespace.
/// </summary>
public class HostAgentDedupeConventionTests
{
    private static readonly Assembly HostAssembly = typeof(IronHiveConfig).Assembly;
    private static readonly Assembly CliAssembly = typeof(IronbeesIntegrationExtensions).Assembly;

    private static readonly string[] BannedFullNames =
    [
        "IronHive.Host.Agent.SubAgent.ISubAgentService",
        "IronHive.Host.Agent.SubAgent.SubAgentContext",
        "IronHive.Host.Agent.SubAgent.SubAgentResult",
        "IronHive.Host.Agent.SubAgent.SubAgentService",
        "IronHive.Host.Agent.SubAgent.SubAgentType",
        "IronHive.Host.Config.SubAgentConfig",
        "IronHive.Host.Config.ExploreAgentConfig",
        "IronHive.Host.Config.GeneralAgentConfig",
        "IronHive.Host.Tools.SubAgentTool",
        "IronHive.Host.Tools.TodoTool",
        "IronHive.Host.Ironbees.ChatClientFrameworkAdapter",
        "IronHive.Host.Ironbees.OrchestratedAgentLoop",
        "IronHive.Host.Ironbees.IronbeesServiceCollectionExtensions",
    ];

    [Fact]
    public void HostAssembly_DoesNotRedefineDeduplicatedAgentTypes()
    {
        var hostTypeNames = HostAssembly.GetTypes().Select(t => t.FullName).ToHashSet();
        var cliTypeNames = CliAssembly.GetTypes().Select(t => t.FullName).ToHashSet();

        foreach (var banned in BannedFullNames)
        {
            hostTypeNames.Should().NotContain(banned,
                $"{banned} was deduplicated onto IronHive.Agent's canonical type (D14) and must not reappear in IronHive.Host");
            cliTypeNames.Should().NotContain(banned,
                $"{banned} was deduplicated onto IronHive.Agent's canonical type (D14) and must not reappear in IronHive.Cli");
        }
    }

    [Fact]
    public void HostAssembly_HasNoIronbeesOrAgentSubAgentNamespace()
    {
        // Broader net than the exact-name list above: the whole IronHive.Host.Ironbees and
        // IronHive.Host.Agent.SubAgent namespaces were retired wholesale in favor of
        // IronHive.Agent.Ironbees / IronHive.Agent.SubAgent.
        var offendingTypes = HostAssembly.GetTypes()
            .Where(t => t.Namespace is "IronHive.Host.Ironbees" or "IronHive.Host.Agent.SubAgent")
            .Select(t => t.FullName)
            .ToList();

        offendingTypes.Should().BeEmpty(
            "these namespaces were retired in D14 in favor of the IronHive.Agent canonical types");
    }
}
