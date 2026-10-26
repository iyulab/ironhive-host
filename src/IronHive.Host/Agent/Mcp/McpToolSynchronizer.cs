using System.Runtime.CompilerServices;
using IronHive.Agent.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IronHive.Agent.Mcp;

/// <summary>
/// Keeps the tool lists of running loops in step with the plugins behind them. A plugin that connects after the loop was
/// built (the server was not ready at start, or was lost and came back) has its tools added to every tracked
/// <see cref="LiveToolList"/>, replacing the tools of its earlier connection, which are bound to a client that is gone; a
/// plugin that disconnects has its tools taken out, so the model is not offered a tool nobody can answer.
/// </summary>
/// <remarks>
/// The lists are held weakly: a loop that ends does not stay alive through this class. It listens to the manager's events
/// for as long as it lives, which is the factory's, so it is not disposable on its own account.
/// </remarks>
public sealed partial class McpToolSynchronizer
{
    private readonly IMcpPluginManager _manager;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private readonly List<WeakReference<LiveToolList>> _lists = [];
    private readonly ConditionalWeakTable<LiveToolList, Dictionary<string, AITool[]>> _owned = new();
    private Task _last = Task.CompletedTask;

    /// <param name="manager">The plugin manager whose connect and disconnect events are followed.</param>
    /// <param name="logger">Optional logger.</param>
    public McpToolSynchronizer(IMcpPluginManager manager, ILogger? logger = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger;
        _manager.PluginConnected += (_, e) => Queue(() => RefreshAsync(e.PluginName));
        _manager.PluginDisconnected += (_, e) => Queue(() => Task.Run(() => Withdraw(e.PluginName)));
    }

    /// <summary>
    /// Starts following <paramref name="list"/>: <paramref name="seeded"/> are the tools of each plugin that were put in it,
    /// which a later connection of that plugin replaces.
    /// </summary>
    public void Track(LiveToolList list, IReadOnlyDictionary<string, IReadOnlyList<AITool>> seeded)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(seeded);
        var owned = new Dictionary<string, AITool[]>(StringComparer.Ordinal);
        foreach (var (plugin, tools) in seeded)
        {
            owned[plugin] = [.. tools];
        }

        lock (_gate)
        {
            _lists.RemoveAll(reference => !reference.TryGetTarget(out _));
            _owned.AddOrUpdate(list, owned);
            _lists.Add(new WeakReference<LiveToolList>(list));
        }
    }

    /// <summary>Completes when every change queued so far has been applied.</summary>
    internal Task Idle()
    {
        lock (_gate)
        {
            return _last;
        }
    }

    private void Queue(Func<Task> work)
    {
        lock (_gate)
        {
            if (_lists.Count == 0)
            {
                return;
            }

            _last = _last.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task RefreshAsync(string plugin)
    {
        IReadOnlyList<AITool> tools;
        try
        {
            tools = await _manager.GetToolsAsync(plugin);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRefreshFailed(_logger, plugin, ex);
            return;
        }

        var applied = Apply(plugin, tools);
        if (applied > 0)
        {
            LogToolsOffered(_logger, plugin, tools.Count, applied);
        }
    }

    private void Withdraw(string plugin) => Apply(plugin, []);

    private int Apply(string plugin, IReadOnlyList<AITool> tools)
    {
        var applied = 0;
        lock (_gate)
        {
            foreach (var reference in _lists)
            {
                if (!reference.TryGetTarget(out var list) || !_owned.TryGetValue(list, out var owned))
                {
                    continue;
                }

                list.Replace(owned.GetValueOrDefault(plugin) ?? [], tools);
                owned[plugin] = [.. tools];
                applied++;
            }
        }

        return applied;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP plugin '{Plugin}' is connected: its {Count} tool(s) are offered to {Loops} running loop(s)")]
    private static partial void LogToolsOffered(ILogger? logger, string plugin, int count, int loops);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP plugin '{Plugin}' connected, but its tools could not be listed")]
    private static partial void LogRefreshFailed(ILogger? logger, string plugin, Exception ex);
}
