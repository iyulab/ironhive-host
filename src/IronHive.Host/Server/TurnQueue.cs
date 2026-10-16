using System.Threading.Channels;
using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// Runs a server session's turns one after another on its own task, so the request loop never waits for a turn.
/// The loop keeps reading while a turn runs — in particular the <see cref="HitlResponseRequest"/> a waiting turn needs,
/// even when another <see cref="UserMessageRequest"/> arrived before it.
/// </summary>
internal sealed class TurnQueue : IAsyncDisposable
{
    private readonly Channel<(Func<CancellationToken, Task> Run, Func<Task>? Dropped)> _turns =
        Channel.CreateUnbounded<(Func<CancellationToken, Task> Run, Func<Task>? Dropped)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationToken _session;
    private readonly Task _worker;
    private CancellationTokenSource? _current;
    private volatile bool _dropQueued;

    /// <param name="session">Cancels every turn when the session ends.</param>
    public TurnQueue(CancellationToken session)
    {
        _session = session;
        _worker = Task.Run(RunAsync, CancellationToken.None);
    }

    /// <summary>
    /// Queues a turn; it starts when the turns before it have finished. The turn must not throw: it reports its own
    /// failure as events. <paramref name="dropped"/> runs instead when the queue is stopped before the turn starts
    /// (<see cref="SuspendCurrent"/>); it must not throw either.
    /// </summary>
    public void Enqueue(Func<CancellationToken, Task> turn, Func<Task>? dropped = null) => _turns.Writer.TryWrite((turn, dropped));

    /// <summary>Cancels the turn that is running, if any. Queued turns still run.</summary>
    public void CancelCurrent()
    {
        try
        {
            Volatile.Read(ref _current)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The turn ended between the read and the cancel: nothing left to cancel
        }
    }

    /// <summary>
    /// Stops the running turn - one that waits for an approval the session keeps on record - and drops the turns queued
    /// behind it: they would run on a history that ends with the suspended turn's unanswered call. Each dropped turn's
    /// <c>dropped</c> callback runs instead.
    /// </summary>
    public void SuspendCurrent()
    {
        _dropQueued = true;
        CancelCurrent();
    }

    /// <summary>Runs the queued turns to completion (or drops them after <see cref="SuspendCurrent"/>), then stops.</summary>
    public async ValueTask DisposeAsync()
    {
        _turns.Writer.TryComplete();
        await _worker;
    }

    private async Task RunAsync()
    {
        await foreach (var (run, dropped) in _turns.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (_dropQueued)
            {
                if (dropped is not null)
                {
                    await dropped();
                }

                continue;
            }

            using var turn = CancellationTokenSource.CreateLinkedTokenSource(_session);
            Volatile.Write(ref _current, turn);
            try
            {
                await run(turn.Token);
            }
            finally
            {
                Volatile.Write(ref _current, null);
            }
        }
    }
}
