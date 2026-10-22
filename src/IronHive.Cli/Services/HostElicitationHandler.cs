using IronHive.Agent.Mcp;
using IronHive.Host.Server;
using ModelContextProtocol.Protocol;

namespace IronHive.Cli.Services;

/// <summary>
/// The CLI's answer to an MCP server's elicitation: a client on the wire answers while <c>run --server</c> has one attached
/// to the <see cref="HitlBridge"/>; otherwise the terminal does (<see cref="ConsoleElicitationHandler"/>).
/// </summary>
public sealed class HostElicitationHandler(HitlBridge bridge, ConsoleElicitationHandler console) : IMcpElicitationHandler
{
    /// <inheritdoc />
    public ValueTask<ElicitResult> ElicitAsync(string pluginName, ElicitRequestParams request, CancellationToken cancellationToken) =>
        bridge.IsAttached
            ? bridge.ElicitAsync(pluginName, request, cancellationToken)
            : console.ElicitAsync(pluginName, request, cancellationToken);
}
