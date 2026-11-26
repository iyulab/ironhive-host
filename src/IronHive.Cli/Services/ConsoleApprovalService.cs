using IronHive.Agent.Mode;
using Spectre.Console;

namespace IronHive.Cli.Services;

/// <summary>
/// Console-based implementation of human approval service.
/// Uses Spectre.Console for rich terminal UI.
/// </summary>
/// <remarks>
/// Only a terminal can answer a prompt. When stdin or stdout is redirected — <c>run --server</c>
/// speaks JSON Lines on exactly those streams, and a piped one-shot run has nobody at the keyboard —
/// the request is rejected with a reason instead of being prompted, so a prompt never lands inside a
/// protocol stream and an unanswerable question never blocks the process.
/// <para>
/// «Always approve» is remembered for this process. For a tool the permission rules judge on a path (the file tools and
/// the directory tools) it covers the folder that was asked about and no other: a directory tool's folder is the one it is
/// pointed at - a later call pointed at that folder is covered whatever it looks for, including what it reaches below it,
/// while a call pointed at a subfolder is asked again - a file tool's is the one that holds the file, a move's is the
/// pair of folders it goes between. A call whose path is not known, or on Windows is written with a stream name or a device
/// prefix, and a search whose file pattern has a <c>..</c> segment, is asked every time and not offered the choice. Every
/// other tool keeps one memory for the whole tool.
/// </para>
/// </remarks>
public class ConsoleApprovalService : IHumanApprovalService
{
    private readonly HashSet<string> _autoApproved = [];
    private readonly Func<bool> _hasInteractiveConsole;
    private readonly bool _showProposalRecommendation;
    private readonly Func<string> _workingDirectory;
    private readonly Func<string, IReadOnlyList<string>, string> _choose;

    /// <summary>
    /// Creates the service, asking on the process console.
    /// </summary>
    public ConsoleApprovalService()
        : this(static () => !Console.IsInputRedirected && !Console.IsOutputRedirected)
    {
    }

    /// <summary>
    /// Creates the service with an explicit "can I prompt here" test — for hosts that know their
    /// surface better than the process streams do, and for tests.
    /// </summary>
    public ConsoleApprovalService(Func<bool> hasInteractiveConsole)
        : this(hasInteractiveConsole, showProposalRecommendation: false)
    {
    }

    /// <summary>
    /// Creates the service asking on the process console, and says whether a model's proposal shows the option the model
    /// recommends (<c>chatBehavior.showProposalRecommendation</c>; off by default).
    /// </summary>
    public ConsoleApprovalService(bool showProposalRecommendation)
        : this(static () => !Console.IsInputRedirected && !Console.IsOutputRedirected, showProposalRecommendation)
    {
    }

    /// <summary>Creates the service with an explicit "can I prompt here" test and the proposal display choice.</summary>
    public ConsoleApprovalService(Func<bool> hasInteractiveConsole, bool showProposalRecommendation)
        : this(hasInteractiveConsole, showProposalRecommendation, Directory.GetCurrentDirectory, Prompt)
    {
    }

    /// <summary>
    /// Creates the service asking on the process console, for tools that work in <paramref name="workingDirectory"/>:
    /// the folder «always approve» covers is resolved against it, so it has to be the directory the permission rules
    /// judge paths against.
    /// </summary>
    public ConsoleApprovalService(bool showProposalRecommendation, Func<string> workingDirectory)
        : this(static () => !Console.IsInputRedirected && !Console.IsOutputRedirected, showProposalRecommendation,
            workingDirectory, Prompt)
    {
    }

    internal ConsoleApprovalService(
        Func<bool> hasInteractiveConsole,
        bool showProposalRecommendation,
        Func<string> workingDirectory,
        Func<string, IReadOnlyList<string>, string> choose)
    {
        _hasInteractiveConsole = hasInteractiveConsole ?? throw new ArgumentNullException(nameof(hasInteractiveConsole));
        _showProposalRecommendation = showProposalRecommendation;
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _choose = choose ?? throw new ArgumentNullException(nameof(choose));
    }

    /// <summary>The directory the folder «always approve» covers is resolved against.</summary>
    internal string WorkingDirectory => _workingDirectory();

    /// <inheritdoc />
    public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        // Check auto-approval list
        var scope = AlwaysScopeOf(request.ToolName, request.Arguments, _workingDirectory());
        if (scope is not null && _autoApproved.Contains(scope.Key))
        {
            return Task.FromResult(ApprovalResult.Approve());
        }

        if (!_hasInteractiveConsole())
        {
            return Task.FromResult(ApprovalResult.Reject(
                $"'{request.ToolName}' requires approval but there is no interactive console to ask on"));
        }

        // Display the approval request
        DisplayApprovalRequest(request);

        // Get user decision
        var result = PromptForApproval(request, scope);

        // Remember if user chose "always approve"
        if (result.Approved && result.AlwaysApprove && scope is not null)
        {
            _autoApproved.Add(scope.Key);
        }

        return Task.FromResult(result);
    }

    private void DisplayApprovalRequest(ApprovalRequest request)
    {
        AnsiConsole.WriteLine();

        // Create panel with risk info
        var riskColor = request.RiskAssessment.Level switch
        {
            RiskLevel.Critical => Color.Red,
            RiskLevel.High => Color.Orange1,
            RiskLevel.Medium => Color.Yellow,
            _ => Color.Grey
        };

        // A delegated agent's calls come through this same prompt; say who asked.
        var askedBy = request.DelegatedBy is { } agent
            ? Environment.NewLine + $"[bold]Requested by:[/] delegated agent [cyan]{Markup.Escape(agent)}[/]"
            : string.Empty;
        var panel = new Panel(new Markup($"""
            [bold]Tool:[/] [cyan]{Markup.Escape(request.ToolName)}[/]
            [bold]Risk Level:[/] [{riskColor.ToMarkup()}]{request.RiskAssessment.Level}[/]
            [bold]Reason:[/] {Markup.Escape(request.RiskAssessment.Reason ?? "Unknown")}
            """ + askedBy))
            .Header($"[{riskColor.ToMarkup()}]APPROVAL REQUIRED[/]")
            .Border(BoxBorder.Double)
            .BorderColor(riskColor);

        AnsiConsole.Write(panel);

        // Show arguments if available
        if (request.Arguments is not null && request.Arguments.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("Argument")
                .AddColumn("Value");

            foreach (var (key, value) in request.Arguments)
            {
                var valueStr = value?.ToString() ?? "(null)";
                // Truncate long values
                if (valueStr.Length > 60)
                {
                    valueStr = valueStr[..57] + "...";
                }
                table.AddRow(Markup.Escape(key), Markup.Escape(valueStr));
            }

            AnsiConsole.Write(table);
        }

        // What the model proposed is its own claim, not something this host checked: its own panel, plainly labelled,
        // apart from the tool, risk and arguments above - and every character of it escaped as plain text.
        if (request.ModelProposal is { } proposal)
        {
            AnsiConsole.Write(new Panel(new Markup(ProposalMarkup(proposal, _showProposalRecommendation)))
                .Header("[grey]MODEL'S PROPOSAL - written by the model, not checked[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey));
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// The model's proposal as Spectre markup: the model's text is escaped, never read as markup. The option the model
    /// recommends is named only when <paramref name="showRecommendation"/> is set - by default the options and their
    /// reasons are listed in the order the model gave them and the person decides before reading which one it prefers.
    /// </summary>
    internal static string ProposalMarkup(ModelAuthoredProposal proposal, bool showRecommendation = false)
    {
        var lines = new List<string>();
        if (showRecommendation
            && proposal.RecommendedOptionId is { } recommended
            && proposal.Options.FirstOrDefault(o => o.Id == recommended) is { } chosen)
        {
            lines.Add($"[bold]Recommends:[/] {Markup.Escape(chosen.Label)} [grey]({Markup.Escape(chosen.Id)})[/]");
        }

        foreach (var option in proposal.Options)
        {
            lines.Add(option.Rationale.Length == 0
                ? $"[grey]{Markup.Escape(option.Id)}[/] {Markup.Escape(option.Label)}"
                : $"[grey]{Markup.Escape(option.Id)}[/] {Markup.Escape(option.Label)} - {Markup.Escape(option.Rationale)}");
        }

        if (proposal.CriterionQuestion is { } question)
        {
            lines.Add($"[bold]Asks:[/] {Markup.Escape(question)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private ApprovalResult PromptForApproval(ApprovalRequest request, AlwaysScope? scope)
    {
        var prompt = request.RiskAssessment.ApprovalPrompt ?? "Allow this operation?";

        const string once = "Yes, approve this time";
        const string reject = "No, reject";
        const string rejectAndStop = "No, reject and stop";

        // A choice is rendered as markup, and a folder name may hold brackets.
        var always = scope is null ? null : Markup.Escape(scope.Choice);
        string[] choices = always is null ? [once, reject, rejectAndStop] : [once, always, reject, rejectAndStop];

        var choice = _choose($"[yellow]{Markup.Escape(prompt)}[/]", choices);

        if (always is not null && choice == always)
        {
            return ApprovalResult.Approve(alwaysApprove: true);
        }

        return choice switch
        {
            once => ApprovalResult.Approve(),
            reject => ApprovalResult.Reject("User rejected"),
            rejectAndStop => ApprovalResult.Reject("User rejected and stopped"),
            _ => ApprovalResult.Reject()
        };
    }

    private static string Prompt(string title, IReadOnlyList<string> choices) =>
        AnsiConsole.Prompt(new SelectionPrompt<string>().Title(title).PageSize(5).AddChoices(choices));

    /// <summary>What an «always approve» answer to one call would cover.</summary>
    /// <param name="Key">The memory entry: two calls with the same key are covered by the same answer.</param>
    /// <param name="Choice">The words the console offers, naming what is covered (plain text, not yet escaped).</param>
    internal sealed record AlwaysScope(string Key, string Choice);

    // The built-in tools the permission rules judge on a path, in both spellings the rules read as one tool. A test holds
    // this list against the tool-call policy, so a new path tool cannot fall back to the tool-wide memory unnoticed.
    private static readonly HashSet<string> DirectoryTools = new(StringComparer.Ordinal)
    {
        "ListDirectory", "list_directory", "GlobFiles", "glob_files", "GrepFiles", "grep_files",
    };

    private static readonly HashSet<string> FileTools = new(StringComparer.Ordinal)
    {
        "ReadFile", "read_file", "WriteFile", "write_file", "EditFile", "edit_file", "DeleteFile", "delete_file",
    };

    private static readonly HashSet<string> MoveTools = new(StringComparer.Ordinal) { "MoveFile", "move_file" };

    /// <summary>
    /// What «always approve» covers for a call, or <see langword="null"/> when the call cannot be remembered because the
    /// path it is judged on is not known or is not the one the text names, or the call looks outside its folder.
    /// </summary>
    /// <remarks>
    /// A path is resolved the way the permission rules resolve it: <see cref="Path.GetFullPath(string)"/>, a relative path
    /// against the tools' directory. The rules judge that result as it stands, a trailing separator included (a pattern
    /// such as <c>x/**</c> matches <c>x/</c> and not <c>x</c>), except that on Windows they first strip a device prefix and
    /// stream names - such a call is not remembered. So two paths the rules judge differently never share a key; one folder
    /// written two ways is asked about twice. The key holds the folder itself, never "everything under it": a subfolder can
    /// be asked about by a rule of its own.
    /// </remarks>
    internal static AlwaysScope? AlwaysScopeOf(
        string toolName, IDictionary<string, object?>? arguments, string workingDirectory)
    {
        const string choice = "Yes, always approve this tool";

        if (DirectoryTools.Contains(toolName))
        {
            if (ClimbsOut(toolName, arguments))
            {
                return null;
            }

            // The rules read no path as the working directory.
            var argument = Argument(arguments, "path");
            if (Resolve(string.IsNullOrEmpty(argument) ? "." : argument, workingDirectory) is not { } full)
            {
                return null;
            }

            // The key keeps a trailing separator, because the rules do; only the words drop it.
            return new AlwaysScope(PathKey(toolName, full),
                $"{choice} on the folder {Path.TrimEndingDirectorySeparator(full)}, including what a call reaches below it");
        }

        if (FileTools.Contains(toolName))
        {
            return FolderOf(Argument(arguments, "path"), workingDirectory) is { } folder
                ? new AlwaysScope(PathKey(toolName, folder), $"{choice} for files in {folder}")
                : null;
        }

        if (MoveTools.Contains(toolName))
        {
            return FolderOf(Argument(arguments, "source"), workingDirectory) is { } from
                && FolderOf(Argument(arguments, "destination"), workingDirectory) is { } to
                ? new AlwaysScope(PathKey(toolName, from, to), $"{choice} for moves from {from} to {to}")
                : null;
        }

        return new AlwaysScope($"tool:{toolName}", choice);
    }

    // The glob that picks a search's files may climb out of the folder the call is pointed at ("../**"). (The text
    // GrepFiles looks for is not a path.)
    private static bool ClimbsOut(string toolName, IDictionary<string, object?>? arguments)
    {
        var glob = toolName switch
        {
            "GlobFiles" or "glob_files" => Argument(arguments, "pattern"),
            "GrepFiles" or "grep_files" => Argument(arguments, "filePattern"),
            _ => null,
        };
        return glob?.Split('/', '\\').Contains("..") == true;
    }

    // NUL is in no path, so the parts cannot run into each other.
    private static string PathKey(string toolName, params string[] folders) =>
        $"path:{toolName}\0{string.Join('\0', folders)}";

    // The argument as the tool-call policy reads it.
    private static string? Argument(IDictionary<string, object?>? arguments, string key) =>
        arguments is not null && arguments.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string? FolderOf(string? path, string workingDirectory) =>
        !string.IsNullOrEmpty(path) && Resolve(path, workingDirectory) is { } full
            ? Path.GetDirectoryName(full) ?? full
            : null;

    private static string? Resolve(string path, string workingDirectory)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory));
            var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            return OperatingSystem.IsWindows() && IsRewrittenByTheRules(full) ? null : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // A device prefix, or a stream name (a colon past the drive): the rules judge what is left without it.
    private static bool IsRewrittenByTheRules(string full) =>
        full.StartsWith(@"\\?\", StringComparison.Ordinal)
        || full.StartsWith(@"\\.\", StringComparison.Ordinal)
        || full.StartsWith(@"\??\", StringComparison.Ordinal)
        || full.AsSpan(Path.GetPathRoot(full)?.Length ?? 0).Contains(':');

    /// <summary>
    /// Clears all auto-approval entries.
    /// </summary>
    public void ClearAutoApprovals()
    {
        _autoApproved.Clear();
    }
}
