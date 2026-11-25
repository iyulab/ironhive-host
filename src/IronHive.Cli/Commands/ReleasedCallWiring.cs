using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Host.Server;
using IronHive.Host.Session;

namespace IronHive.Cli.Commands;

/// <summary>
/// How <c>run --server</c> wires MCP calls that are released to wait for the person (<c>mcp.elicitation.deferAfterSeconds</c>).
/// </summary>
internal static class ReleasedCallWiring
{
    /// <summary>
    /// Turns releasing on when the option is set, before the plugins connect (the plugin manager reads the delay then), and
    /// follows the manager's released calls. Returns the subscription, or null with the option unset or no bridge or manager.
    /// </summary>
    public static IDisposable? Observe(HitlBridge? bridge, IMcpPluginManager? plugins, int? deferAfterSeconds)
    {
        if (bridge is null || plugins is null || deferAfterSeconds is not { } seconds)
        {
            return null;
        }

        if (seconds <= 0)
        {
            throw new InvalidOperationException("mcp.elicitation.deferAfterSeconds must be positive; leave it unset to keep a call in its turn.");
        }

        bridge.ElicitationDeferAfter = TimeSpan.FromSeconds(seconds);
        return bridge.Observe(plugins);
    }

    /// <summary>The turn the answer to a released call's question opens.</summary>
    public static Func<AwaitingInputAnswer, CancellationToken, IAsyncEnumerable<AgentResponseChunk>> Turn(
        IAgentLoop loop, HitlBridge bridge, IMcpPluginManager plugins, SessionTurnRecorder? recorder, TextWriter diagnostics) =>
        (answer, token) => AwaitingInputTurn.RunAsync(
            loop, answer, bridge, plugins, recorder,
            ex => diagnostics.WriteLine($"awaiting input: the released call was not sent again ({ex.GetType().Name}: {ex.Message})"),
            cancellationToken: token);

    /// <summary>
    /// Hands the released calls a previous process recorded back to the bridge: their questions are sent again and wait for
    /// the answer. An entry this host cannot read (a newer or damaged one) must not keep the session from starting: it is
    /// closed, and the person is not asked about a call that cannot be sent again.
    /// </summary>
    public static async Task RestoreAsync(
        HitlBridge bridge, SessionTurnRecorder? recorder, IReadOnlyList<AwaitingInputEntry> entries, TextWriter diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            if (AwaitingInputWait.TryFromTranscript(entry, out var wait))
            {
                bridge.RestoreAwaitingInput(wait!);
                continue;
            }

            await diagnostics.WriteLineAsync($"awaiting input: the logged call {entry.ToolUseId} cannot be read and is closed");
            if (recorder is not null)
            {
                await ((IAwaitingInputLog)recorder).SettledAsync(entry.RequestId, cancellationToken);
            }
        }
    }
}
