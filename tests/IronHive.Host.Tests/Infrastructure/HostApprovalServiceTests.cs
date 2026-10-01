using AwesomeAssertions;
using IronHive.Agent.Mode;
using IronHive.Cli.Services;
using IronHive.Host.Protocol;
using IronHive.Host.Server;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The CLI's approver: the terminal answers unless <c>run --server</c> has a client attached to the bridge.
/// </summary>
public class HostApprovalServiceTests
{
    private static ApprovalRequest Request() => new()
    {
        ToolName = "WriteFile",
        RiskAssessment = RiskAssessment.Risky(RiskLevel.Medium, "Configuration file"),
    };

    [Fact]
    public async Task WithNoClientAttached_TheConsoleAnswers()
    {
        using var bridge = new HitlBridge();
        var service = new HostApprovalService(bridge, new ConsoleApprovalService(() => false));

        var result = await service.RequestApprovalAsync(Request(), TestContext.Current.CancellationToken);

        result.Approved.Should().BeFalse();
        result.RejectionReason.Should().Contain("no interactive console");
    }

    [Fact]
    public async Task WithAClientAttached_TheClientAnswers()
    {
        using var bridge = new HitlBridge();
        using var attachment = bridge.Attach((evt, _) =>
        {
            bridge.Resolve(new HitlResponseRequest(true, Id: ((HitlRequestEvent)evt).Id));
            return Task.CompletedTask;
        });
        var service = new HostApprovalService(bridge, new ConsoleApprovalService(() => false));

        var result = await service.RequestApprovalAsync(Request(), TestContext.Current.CancellationToken);

        result.Approved.Should().BeTrue();
    }
}
