using System.ComponentModel;
using System.Text.Json;
using IronHive.Cli.Core.Session;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Command to manage sessions.
/// </summary>
public class SessionsCommand : AsyncCommand<SessionsCommand.Settings>
{
    private readonly ISessionManager _sessionManager;

    public SessionsCommand(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[action]")]
        [Description("Action: list (default), delete <id>")]
        [DefaultValue("list")]
        public string Action { get; init; } = "list";

        [CommandArgument(1, "[id]")]
        [Description("Session ID (for delete action)")]
        public string? SessionId { get; init; }

        [CommandOption("-n|--limit <COUNT>")]
        [Description("Number of sessions to show")]
        [DefaultValue(10)]
        public int Limit { get; init; } = 10;

        [CommandOption("-o|--output <FORMAT>")]
        [Description("Output format: text (default), json")]
        public string? OutputFormat { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var projectPath = Directory.GetCurrentDirectory();
        var outputFormat = settings.OutputFormat?.ToLowerInvariant() ?? "text";

        return settings.Action.ToLowerInvariant() switch
        {
            "list" => await ListSessionsAsync(projectPath, settings.Limit, outputFormat),
            "delete" => await DeleteSessionAsync(settings.SessionId, outputFormat),
            _ => await ListSessionsAsync(projectPath, settings.Limit, outputFormat)
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private async Task<int> ListSessionsAsync(string projectPath, int limit, string outputFormat)
    {
        var sessions = await _sessionManager.ListSessionsAsync(projectPath, limit);

        // JSON output
        if (outputFormat == "json")
        {
            var jsonSessions = sessions.Select(s => new
            {
                id = s.Id,
                status = s.Status.ToString().ToLowerInvariant(),
                model = s.Model,
                created = s.CreatedAt.ToString("o"),
                messageCount = s.MessageCount,
                firstMessage = s.FirstUserMessage
            }).ToArray();

            Console.WriteLine(JsonSerializer.Serialize(jsonSessions, JsonOptions));
            return 0;
        }

        // Text output (default)
        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No sessions found for this project.[/]");
            return 0;
        }

        AnsiConsole.Write(new Rule($"[yellow]Sessions ({sessions.Count})[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]ID[/]").Width(20))
            .AddColumn(new TableColumn("[grey]Status[/]").Width(10))
            .AddColumn(new TableColumn("[grey]Model[/]").Width(20))
            .AddColumn(new TableColumn("[grey]Created[/]").Width(20))
            .AddColumn(new TableColumn("[grey]Messages[/]").Width(10))
            .AddColumn(new TableColumn("[grey]First Message[/]"));

        foreach (var session in sessions)
        {
            var statusColor = session.Status switch
            {
                SessionStatus.Active => "green",
                SessionStatus.Ended => "grey",
                SessionStatus.Error => "red",
                SessionStatus.Forked => "yellow",
                _ => "grey"
            };

            var statusText = session.Status.ToString().ToLowerInvariant();

            table.AddRow(
                $"[cyan]{session.Id}[/]",
                $"[{statusColor}]{statusText}[/]",
                Markup.Escape(session.Model),
                session.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                session.MessageCount.ToString(),
                Markup.Escape(session.FirstUserMessage ?? "[grey]<empty>[/]")
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[grey]Usage:[/]");
        AnsiConsole.MarkupLine("  [cyan]ironhive -c[/]              Continue most recent session");
        AnsiConsole.MarkupLine("  [cyan]ironhive -r <id>[/]         Resume specific session");
        AnsiConsole.MarkupLine("  [cyan]ironhive sessions delete <id>[/]  Delete a session");

        return 0;
    }

    private async Task<int> DeleteSessionAsync(string? sessionId, string outputFormat)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            if (outputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = "Session ID required", code = 1 }, JsonOptions));
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error: Session ID required for delete action.[/]");
                AnsiConsole.MarkupLine("[grey]Usage: ironhive sessions delete <session-id>[/]");
            }
            return 1;
        }

        // Verify session exists
        var session = await _sessionManager.LoadSessionAsync(sessionId);
        if (session is null)
        {
            if (outputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = $"Session not found: {sessionId}", code = 1 }, JsonOptions));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Session not found: {sessionId}[/]");
            }
            return 1;
        }

        // For JSON output, skip confirmation and delete directly
        if (outputFormat == "json")
        {
            await _sessionManager.DeleteSessionAsync(sessionId);
            Console.WriteLine(JsonSerializer.Serialize(new { deleted = sessionId, success = true }, JsonOptions));
            return 0;
        }

        // Confirm deletion (text mode only)
        var confirm = AnsiConsole.Confirm($"Delete session [cyan]{sessionId}[/]?", defaultValue: false);
        if (!confirm)
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return 0;
        }

        await _sessionManager.DeleteSessionAsync(sessionId);
        AnsiConsole.MarkupLine($"[green]Session deleted: {sessionId}[/]");

        return 0;
    }
}
