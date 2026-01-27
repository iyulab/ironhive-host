using IronHive.Cli.Core.Agent.Mode;
using Spectre.Console;

namespace IronHive.Cli.Services;

/// <summary>
/// Console-based implementation of human approval service.
/// Uses Spectre.Console for rich terminal UI.
/// </summary>
public class ConsoleApprovalService : IHumanApprovalService
{
    private readonly HashSet<string> _autoApproved = [];

    /// <inheritdoc />
    public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        // Check auto-approval list
        var autoApproveKey = GetAutoApproveKey(request);
        if (_autoApproved.Contains(autoApproveKey))
        {
            return Task.FromResult(ApprovalResult.Approve());
        }

        // Display the approval request
        DisplayApprovalRequest(request);

        // Get user decision
        var result = PromptForApproval(request, cancellationToken);

        // Remember if user chose "always approve"
        if (result.Approved && result.AlwaysApprove)
        {
            _autoApproved.Add(autoApproveKey);
        }

        return Task.FromResult(result);
    }

    private static void DisplayApprovalRequest(ApprovalRequest request)
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

        var panel = new Panel(new Markup($"""
            [bold]Tool:[/] [cyan]{Markup.Escape(request.ToolName)}[/]
            [bold]Risk Level:[/] [{riskColor.ToMarkup()}]{request.RiskAssessment.Level}[/]
            [bold]Reason:[/] {Markup.Escape(request.RiskAssessment.Reason ?? "Unknown")}
            """))
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

        AnsiConsole.WriteLine();
    }

    private static ApprovalResult PromptForApproval(ApprovalRequest request, CancellationToken cancellationToken)
    {
        var prompt = request.RiskAssessment.ApprovalPrompt ?? "Allow this operation?";

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[yellow]{Markup.Escape(prompt)}[/]")
                .PageSize(5)
                .AddChoices([
                    "Yes, approve this time",
                    "Yes, always approve this tool",
                    "No, reject",
                    "No, reject and stop"
                ]));

        return choice switch
        {
            "Yes, approve this time" => ApprovalResult.Approve(),
            "Yes, always approve this tool" => ApprovalResult.Approve(alwaysApprove: true),
            "No, reject" => ApprovalResult.Reject("User rejected"),
            "No, reject and stop" => ApprovalResult.Reject("User rejected and stopped"),
            _ => ApprovalResult.Reject()
        };
    }

    private static string GetAutoApproveKey(ApprovalRequest request)
    {
        // Key is based on tool name only (not arguments)
        return $"tool:{request.ToolName}";
    }

    /// <summary>
    /// Clears all auto-approval entries.
    /// </summary>
    public void ClearAutoApprovals()
    {
        _autoApproved.Clear();
    }
}
