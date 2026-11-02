using AwesomeAssertions;
using IronHive.Agent.Mode;
using IronHive.Agent.Permissions;
using IronHive.Cli.Services;
using IronHive.Host.Tools;

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
    public void TheModelsProposal_IsShownAsPlainText_NeverReadAsMarkup()
    {
        ModelAuthoredProposal.TryCreate(
            [new ProposalOption("a", "[red]Write[/] it", "keeps [bold]the[/] file"), new ProposalOption("b", "Skip")],
            "a",
            "Is [link=https://example.test]a backup[/] needed?",
            out var proposal).Should().BeTrue();

        var markup = ConsoleApprovalService.ProposalMarkup(proposal!);

        // The model's brackets are escaped, so they print as brackets; the markup that remains is the host's own.
        markup.Should().Contain("[[red]]Write[[/]] it").And.Contain("keeps [[bold]]the[[/]] file")
            .And.Contain("[[link=https://example.test]]a backup[[/]]");
        markup.Should().Contain("[bold]Recommends:[/] [[red]]Write[[/]] it").And.Contain("[bold]Asks:[/]");
        // It must still be valid markup: Spectre parses it without throwing and shows the model's brackets literally.
        new Spectre.Console.Markup(markup).Should().NotBeNull();
    }

    [Fact]
    public void AProposalWithOnlyAQuestion_ShowsTheQuestion_AndNoRecommendation()
    {
        ModelAuthoredProposal.TryCreate(null, null, "Which environment?", out var proposal).Should().BeTrue();

        var markup = ConsoleApprovalService.ProposalMarkup(proposal!);

        markup.Should().Be("[bold]Asks:[/] Which environment?");
    }

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
        var filter = new ToolCallPolicy(PermissionConfig.CreateDefault());
        var pipeline = HostChatPipeline.CreateToolInvocationPipeline(
            filter, new ConsoleApprovalService(() => false), loggerFactory: null);
        var ran = false;
        var function = Microsoft.Extensions.AI.AIFunctionFactory.Create((string path, string content) => { ran = true; return "ok"; }, "WriteFile");
        var context = new Microsoft.Extensions.AI.FunctionInvocationContext
        {
            Function = function,
            Arguments = new Microsoft.Extensions.AI.AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "app.json", ["content"] = "{}" }),
            CallContent = new Microsoft.Extensions.AI.FunctionCallContent("c1", "WriteFile")
        };

        var result = await pipeline.InvokeAsync(context, TestContext.Current.CancellationToken);

        Assert.False(ran);
        Assert.Contains("Approval rejected", result!.ToString());
        Assert.Contains("no interactive console", result.ToString());
    }
}
