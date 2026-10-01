using IronHive.Agent.Mode;
using IronHive.Host.Server;

namespace IronHive.Cli.Services;

/// <summary>
/// The CLI's approver: a client on the wire answers while <c>run --server</c> has one attached to the
/// <see cref="HitlBridge"/>; otherwise the terminal does (<see cref="ConsoleApprovalService"/>, which rejects when there is
/// no interactive console).
/// </summary>
public sealed class HostApprovalService(HitlBridge bridge, ConsoleApprovalService console) : IHumanApprovalService
{
    /// <inheritdoc />
    public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default) =>
        bridge.IsAttached
            ? bridge.RequestApprovalAsync(request, cancellationToken)
            : console.RequestApprovalAsync(request, cancellationToken);
}
