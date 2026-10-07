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
    private readonly Channel<Func<CancellationToken, Task>> _turns = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationToken _session;
    private readonly Task _worker;
    private CancellationTokenSource? _current;

    /// <param name="session">Cancels every turn when the session ends.</param>
    public TurnQueue(CancellationToken session)
    {
        _session = session;
        _worker = Task.Run(RunAsync, CancellationToken.None);
    }

    /// <summary>
    /// Queues a turn; it starts when the turns before it have finished. The turn must not throw: it reports its own
    /// failure as events.
    /// </summary>
    public void Enqueue(Func<CancellationToken, Task> turn) => _turns.Writer.TryWrite(turn);

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

    /// <summary>Runs the queued turns to completion, then stops.</summary>
    public async ValueTask DisposeAsync()
    {
        _turns.Writer.TryComplete();
        await _worker;
    }

    private async Task RunAsync()
    {
        await foreach (var run in _turns.Reader.ReadAllAsync(CancellationToken.None))
        {
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
