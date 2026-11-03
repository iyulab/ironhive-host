using AwesomeAssertions;
using IronHive.Agent.Mcp;
using IronHive.Agent.Mode;
using IronHive.Cli.Services;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using ModelContextProtocol.Protocol;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The CLI's answer to an MCP server's URL-mode elicitation: the terminal asks unless <c>run --server</c> has a client
/// attached to the bridge.
/// </summary>
public class HostElicitationHandlerTests
{
    private static ElicitRequestParams Request() => new()
    {
        Mode = "url",
        Message = "Approve the payment.",
        Url = "https://approvals.example.test/apr_1",
    };

    [Fact]
    public async Task WithNoClientAttached_AndNoTerminal_ItIsCancelledWithoutAsking()
    {
        using var bridge = new HitlBridge();
        var asked = false;
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(() => false, _ => { asked = true; return ConsoleElicitationHandler.Done; }));

        var result = await handler.ElicitAsync("payments", Request(), TestContext.Current.CancellationToken);

        result.Action.Should().Be("cancel");
        asked.Should().BeFalse();
    }

    [Theory]
    [InlineData(ConsoleElicitationHandler.Done, "accept")]
    [InlineData(ConsoleElicitationHandler.Decline, "decline")]
    public async Task AtATerminal_ThePersonsChoice_IsTheAnswer_AndThePromptShowsTheLink(string choice, string action)
    {
        using var bridge = new HitlBridge();
        string? shown = null;
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(() => true, title => { shown = title; return choice; }));

        var result = await handler.ElicitAsync("payments", Request(), TestContext.Current.CancellationToken);

        result.Action.Should().Be(action);
        shown.Should().Contain("payments").And.Contain("Approve the payment.").And.Contain("https://approvals.example.test/apr_1");
    }

    private static ModelAuthoredProposal Proposal()
    {
        ModelAuthoredProposal.TryCreate([new ProposalOption("a", "Pay now", "matches the order")], "a", null, out var proposal).Should().BeTrue();
        return proposal!;
    }

    [Fact]
    public async Task AtATerminal_TheModelsProposalForTheCall_IsShownApartAndLabelled_AsPlainText()
    {
        using var bridge = new HitlBridge();
        string? shown = null;
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(() => true, title => { shown = title; return ConsoleElicitationHandler.Done; }));
        ModelAuthoredProposal.TryCreate([new ProposalOption("a", "Pay [red]now[/]", "matches the order")], "a", null, out var markupy).Should().BeTrue();

        await handler.ElicitAsync("payments", new McpCallInfo("send_payment", "call_42") { ModelProposal = markupy }, Request(), TestContext.Current.CancellationToken);

        shown.Should().Contain("Approve the payment.", "the server's own words are still there")
            .And.Contain("written by the model, not checked")
            .And.NotContain("Recommends:", "the model's preference is not put in front of the question unless asked for")
            .And.Contain("Pay [[red]]now[[/]]", "the model's text is escaped, never read as markup");
    }

    [Fact]
    public async Task AtATerminal_WithTheRecommendationSettingOn_TheRecommendedOptionIsNamed()
    {
        using var bridge = new HitlBridge();
        string? shown = null;
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(
            () => true, title => { shown = title; return ConsoleElicitationHandler.Done; }, showProposalRecommendation: true));

        await handler.ElicitAsync("payments", new McpCallInfo("send_payment", "call_42") { ModelProposal = Proposal() }, Request(), TestContext.Current.CancellationToken);

        shown.Should().Contain("Recommends:").And.Contain("Pay now");
    }

    [Fact]
    public async Task AtATerminal_WithoutAProposal_ThePromptIsWhatItWas()
    {
        using var bridge = new HitlBridge();
        string? shown = null;
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(() => true, title => { shown = title; return ConsoleElicitationHandler.Done; }));

        await handler.ElicitAsync("payments", new McpCallInfo("send_payment", "call_42"), Request(), TestContext.Current.CancellationToken);

        shown.Should().NotContain("MODEL'S PROPOSAL");
    }

    [Fact]
    public async Task WithAClientAttached_TheCallAndItsProposalReachTheClient()
    {
        using var bridge = new HitlBridge();
        ElicitationRequestEvent? seen = null;
        using var attachment = bridge.Attach((evt, _) =>
        {
            seen = (ElicitationRequestEvent)evt;
            bridge.ResolveElicitation(new ElicitationResponseRequest("accept", seen.Id));
            return Task.CompletedTask;
        });
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(() => true, _ => ConsoleElicitationHandler.Decline));

        await handler.ElicitAsync("payments", new McpCallInfo("send_payment", "call_42") { ModelProposal = Proposal() }, Request(), TestContext.Current.CancellationToken);

        seen.Should().NotBeNull();
        seen!.CallId.Should().Be("call_42", "the CLI handler used to forward only the call-less overload and dropped the call");
        seen.Tool.Should().Be("send_payment");
        seen.ModelProposal.Should().NotBeNull();
    }

    [Fact]
    public async Task WithAClientAttached_TheClientAnswers()
    {
        using var bridge = new HitlBridge();
        using var attachment = bridge.Attach((evt, _) =>
        {
            bridge.ResolveElicitation(new ElicitationResponseRequest("accept", ((ElicitationRequestEvent)evt).Id));
            return Task.CompletedTask;
        });
        var handler = new HostElicitationHandler(bridge, new ConsoleElicitationHandler(() => true, _ => ConsoleElicitationHandler.Decline));

        var result = await handler.ElicitAsync("payments", Request(), TestContext.Current.CancellationToken);

        result.Action.Should().Be("accept");
    }
}
