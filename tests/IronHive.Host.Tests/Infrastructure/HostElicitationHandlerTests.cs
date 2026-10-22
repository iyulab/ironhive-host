using AwesomeAssertions;
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
