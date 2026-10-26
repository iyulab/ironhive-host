using AwesomeAssertions;
using IronHive.Agent.Mcp;
using IronHive.Cli.Infrastructure;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// A long-running engine writes nothing else about its MCP plugins: the operator sees a server that is still not attached
/// only through these lines, on the error stream (standard output is the server mode's JSON Lines).
/// </summary>
public class McpDiagnosticsTests
{
    [Fact]
    public void AFailedAttempt_AndAConnection_EachWriteOneLine_NamingThePluginAndTheReason()
    {
        var manager = Substitute.For<IMcpPluginManager>();
        var error = new StringWriter();
        McpDiagnostics.Attach(manager, error);

        manager.PluginConnectionFailed += Raise.EventWith(manager, new McpPluginEventArgs { PluginName = "msg", ErrorMessage = "401 Unauthorized" });
        manager.PluginConnected += Raise.EventWith(manager, new McpPluginEventArgs { PluginName = "msg" });

        error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "ironhive: MCP plugin 'msg' is not connected: 401 Unauthorized",
            "ironhive: MCP plugin 'msg' connected");
    }

    [Fact]
    public void AFailureWithoutAMessage_StillWritesALine()
    {
        var manager = Substitute.For<IMcpPluginManager>();
        var error = new StringWriter();
        McpDiagnostics.Attach(manager, error);

        manager.PluginConnectionFailed += Raise.EventWith(manager, new McpPluginEventArgs { PluginName = "msg" });

        error.ToString().Should().Contain("'msg' is not connected: unknown error");
    }
}
