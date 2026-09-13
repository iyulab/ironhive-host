using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Cli.Services;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The console approval service is the one <c>IHumanApprovalService</c> this host ships. It was
/// registered from the start and, until IronHive.Agent 0.12.0, never called; now that the gate calls
/// it, the one thing it must never do is prompt into a stream that is not a terminal.
/// </summary>
public class ConsoleApprovalServiceTests
{
    private static ApprovalRequest Request(string tool = "WriteFile") => new()
    {
        ToolName = tool,
        Arguments = new Dictionary<string, object?> { ["path"] = "app.json" },
        RiskAssessment = RiskAssessment.Risky(RiskLevel.Medium, "Configuration file", "Allow this operation?")
    };

    [Fact]
    public async Task WithoutAnInteractiveConsole_RejectsWithAReason_InsteadOfPrompting()
    {
        // run --server speaks JSON Lines on stdin/stdout; a prompt there would corrupt the stream.
        var service = new ConsoleApprovalService(hasInteractiveConsole: () => false);

        var result = await service.RequestApprovalAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Approved);
        Assert.Contains("WriteFile", result.RejectionReason);
        Assert.Contains("no interactive console", result.RejectionReason);
    }

    [Fact]
    public async Task ThroughTheGate_ANonInteractiveRejection_ReachesTheModelAsAResult_NotAnException()
    {
        var filter = new ModeToolFilter(PermissionConfig.CreateDefault());
        var invoker = ApprovalGatedFunctionInvoker.Create(filter, new ConsoleApprovalService(() => false));
        var ran = false;
        var function = Microsoft.Extensions.AI.AIFunctionFactory.Create((string path, string content) => { ran = true; return "ok"; }, "WriteFile");
        var context = new Microsoft.Extensions.AI.FunctionInvocationContext
        {
            Function = function,
            Arguments = new Microsoft.Extensions.AI.AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "app.json", ["content"] = "{}" }),
            CallContent = new Microsoft.Extensions.AI.FunctionCallContent("c1", "WriteFile")
        };

        var result = await invoker(context, TestContext.Current.CancellationToken);

        Assert.False(ran);
        Assert.Contains("Approval rejected", result!.ToString());
        Assert.Contains("no interactive console", result.ToString());
    }
}
