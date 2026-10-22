using IronHive.Agent.Mcp;
using ModelContextProtocol.Protocol;
using Spectre.Console;

namespace IronHive.Cli.Services;

/// <summary>
/// Puts an MCP server's URL-mode elicitation in front of the person at the terminal: shows the server's message and URL and
/// asks whether they finished there.
/// </summary>
/// <remarks>
/// Like <see cref="ConsoleApprovalService"/>, only a terminal can answer: with stdin or stdout redirected the request is
/// answered <c>cancel</c> without prompting. A request that is not URL mode is answered <c>decline</c>.
/// </remarks>
public sealed class ConsoleElicitationHandler : IMcpElicitationHandler
{
    internal const string Done = "Done - I finished at the link";
    internal const string Decline = "Decline";

    private readonly Func<bool> _hasInteractiveConsole;
    private readonly Func<string, string> _choose;

    /// <summary>Creates the handler, asking on the process console.</summary>
    public ConsoleElicitationHandler()
        : this(static () => !Console.IsInputRedirected && !Console.IsOutputRedirected, Prompt)
    {
    }

    /// <summary>Creates the handler with an explicit «can I prompt here» test and chooser (for tests).</summary>
    internal ConsoleElicitationHandler(Func<bool> hasInteractiveConsole, Func<string, string> choose)
    {
        _hasInteractiveConsole = hasInteractiveConsole ?? throw new ArgumentNullException(nameof(hasInteractiveConsole));
        _choose = choose ?? throw new ArgumentNullException(nameof(choose));
    }

    /// <inheritdoc />
    public ValueTask<ElicitResult> ElicitAsync(string pluginName, ElicitRequestParams request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(request.Mode, "url", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
        }

        if (!_hasInteractiveConsole())
        {
            return ValueTask.FromResult(new ElicitResult { Action = "cancel" });
        }

        var title = $"[yellow]{Markup.Escape(pluginName)}[/] needs you: {Markup.Escape(request.Message ?? string.Empty)}"
            + (request.Url is { Length: > 0 } url ? $"\n[link]{Markup.Escape(url)}[/]" : string.Empty);
        var choice = _choose(title);
        return ValueTask.FromResult(new ElicitResult { Action = choice == Done ? "accept" : "decline" });
    }

    private static string Prompt(string title)
    {
        AnsiConsole.WriteLine();
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices([Done, Decline]));
    }
}
