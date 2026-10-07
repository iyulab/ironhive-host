using IronHive.Agent.Mode;
using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// Hears each approval request of a <see cref="HitlBridge"/> when it starts waiting and when it is settled, so a host can
/// keep the wait somewhere that outlives the process (<see cref="Session.SessionTurnRecorder"/> writes it to the session).
/// </summary>
public interface IApprovalWaitLog
{
    /// <summary>The request is about to be sent to the client and will wait for its answer.</summary>
    /// <param name="request">The call being asked about.</param>
    /// <param name="sent">The event the client receives, with the id it answers with.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task WaitingAsync(ApprovalRequest request, HitlRequestEvent sent, CancellationToken cancellationToken);

    /// <summary>The request was settled - answered, rejected or timed out. Not called when the wait is cancelled.</summary>
    /// <param name="requestId">The id of the settled request.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SettledAsync(string requestId, CancellationToken cancellationToken);
}
