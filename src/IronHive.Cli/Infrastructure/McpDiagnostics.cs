using IronHive.Agent.Mcp;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// The operator's trace of MCP plugin connections in a long-running engine: one line on the error stream for each attempt that
/// fails and each plugin that connects. Standard output is the command's own (server mode writes JSON Lines to it), so the lines go
/// to standard error, as the CLI's other diagnostics do.
/// </summary>
internal static class McpDiagnostics
{
    /// <summary>Starts writing to <paramref name="error"/> for as long as <paramref name="manager"/> lives.</summary>
    internal static void Attach(IMcpPluginManager manager, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(error);
        manager.PluginConnectionFailed += (_, e) =>
            error.WriteLine($"ironhive: MCP plugin '{e.PluginName}' is not connected: {e.ErrorMessage ?? "unknown error"}");
        manager.PluginConnected += (_, e) =>
            error.WriteLine($"ironhive: MCP plugin '{e.PluginName}' connected");
    }
}
