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
        ElicitAsync(pluginName, null, request, cancellationToken);

    /// <inheritdoc />
    /// <remarks>The call-aware overload is the one the plugin manager uses; forwarding it keeps the call (and the model's proposal
    /// for it) on the way to whoever answers - the wire client or the terminal.</remarks>
    public ValueTask<ElicitResult> ElicitAsync(string pluginName, McpCallInfo? call, ElicitRequestParams request, CancellationToken cancellationToken) =>
        bridge.IsAttached
            ? bridge.ElicitAsync(pluginName, call, request, cancellationToken)
            : console.ElicitAsync(pluginName, call, request, cancellationToken);
}
