using AwesomeAssertions;
using IronHive.Agent.Mcp;
using IronHive.Agent.Permissions;
using IronHive.Cli.Infrastructure;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The CLI's tool-call policy reaches MCP tools by origin: their servers' names (<c>summarize</c>) carry no prefix, so the
/// <c>mcp_tools</c> rules apply only through the plugin manager.
/// </summary>
public sealed class McpPermissionWiringTests
{
    private static PermissionConfig McpOnly() => new()
    {
        DefaultAction = PermissionAction.Deny,
        McpTools = [new PermissionRule { Pattern = "*", Action = PermissionAction.Allow }],
    };

    [Fact]
    public void WithAPluginManager_AnMcpTool_IsJudgedByTheMcpToolsRules()
    {
        var manager = Substitute.For<IMcpPluginManager>();
        manager.IsMcpTool("summarize").Returns(true);

        var policy = ServiceCollectionExtensions.CreateToolCallPolicy(McpOnly(), manager);

        policy.Evaluate("summarize", null).Verdict.Should().Be(PermissionAction.Allow);
        policy.Evaluate("fetch_inventory", null).Verdict.Should().Be(PermissionAction.Deny, "a tool no server listed stays under the other rules");
    }

    [Fact]
    public void WithoutAPluginManager_AServerNamedTool_FallsToTheDefault()
    {
        var policy = ServiceCollectionExtensions.CreateToolCallPolicy(McpOnly(), null);

        policy.Evaluate("summarize", null).Verdict.Should().Be(PermissionAction.Deny);
    }
}
