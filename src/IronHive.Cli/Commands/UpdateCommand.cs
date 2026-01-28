using System.ComponentModel;
using IronHive.Cli.Core.Update;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Command to check for and install updates.
/// </summary>
public class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly IUpdateService _updateService;

    public UpdateCommand(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Check for updates without installing")]
        public bool CheckOnly { get; init; }

        [CommandOption("--force")]
        [Description("Force update even if already up to date")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[grey]Current version: [cyan]v{_updateService.CurrentVersion}[/][/]");
        AnsiConsole.WriteLine();

        // Check for updates
        UpdateInfo? updateInfo = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Checking for updates...", async _ =>
            {
                updateInfo = await _updateService.CheckForUpdateAsync();
            });

        if (updateInfo is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to check for updates.[/]");
            AnsiConsole.MarkupLine("[grey]Please check your network connection and try again.[/]");
            return 1;
        }

        if (!updateInfo.IsUpdateAvailable && !settings.Force)
        {
            AnsiConsole.MarkupLine("[green]You are already running the latest version.[/]");
            return 0;
        }

        // Display update info
        DisplayUpdateInfo(updateInfo);

        if (settings.CheckOnly)
        {
            return updateInfo.IsUpdateAvailable ? 2 : 0; // Exit code 2 = update available
        }

        // Confirm update
        if (!settings.Force && !AnsiConsole.Confirm("Do you want to install this update?"))
        {
            AnsiConsole.MarkupLine("[grey]Update cancelled.[/]");
            return 0;
        }

        // Perform update
        var result = await PerformUpdateAsync();

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Update failed: {Markup.Escape(result.Error ?? "Unknown error")}[/]");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Successfully updated to v{result.UpdatedVersion}![/]");

        if (result.RestartRequired)
        {
            AnsiConsole.MarkupLine("[yellow]Please restart ironhive to use the new version.[/]");
        }

        return 0;
    }

    private static void DisplayUpdateInfo(UpdateInfo info)
    {
        var panel = new Panel(new Rows(
            new Markup($"[grey]Current version:[/] [cyan]v{info.CurrentVersion}[/]"),
            new Markup($"[grey]Latest version:[/]  [green]v{info.LatestVersion}[/]" +
                (info.IsPrerelease ? " [yellow](prerelease)[/]" : ""))
        ))
        {
            Header = new PanelHeader("[yellow]Update Available[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("yellow")
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
        {
            AnsiConsole.MarkupLine("[grey]Release notes:[/]");
            // Truncate long release notes
            var notes = info.ReleaseNotes.Length > 500
                ? info.ReleaseNotes[..497] + "..."
                : info.ReleaseNotes;
            AnsiConsole.WriteLine(notes);
            AnsiConsole.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(info.ReleaseUrl))
        {
            AnsiConsole.MarkupLine($"[grey]More info:[/] [link={info.ReleaseUrl}]{info.ReleaseUrl}[/]");
            AnsiConsole.WriteLine();
        }
    }

    private async Task<UpdateResult> PerformUpdateAsync()
    {
        UpdateResult result = new() { Success = false };

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Updating...", maxValue: 100);

                var progress = new Progress<UpdateProgress>(p =>
                {
                    task.Description = p.Operation;
                    if (p.PercentComplete.HasValue)
                    {
                        task.Value = p.PercentComplete.Value;
                    }
                });

                result = await _updateService.UpdateAsync(progress);
                task.Value = 100;
            });

        return result;
    }
}

/// <summary>
/// Helper class for background update checking.
/// </summary>
public static class UpdateChecker
{
    private static UpdateInfo? _cachedUpdateInfo;
    private static Task<UpdateInfo?>? _checkTask;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// Starts a background check for updates.
    /// </summary>
    public static void StartBackgroundCheck(IUpdateService updateService)
    {
        _checkTask = Task.Run(async () =>
        {
            try
            {
                _cachedUpdateInfo = await updateService.CheckForUpdateAsync();
                return _cachedUpdateInfo;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Gets the cached update info if available.
    /// </summary>
    public static UpdateInfo? GetCachedUpdateInfo() => _cachedUpdateInfo;

    /// <summary>
    /// Waits for the background check to complete and returns the result.
    /// </summary>
    public static async Task<UpdateInfo?> WaitForCheckAsync(TimeSpan? timeout = null)
    {
        if (_checkTask is null)
        {
            return null;
        }

        try
        {
            if (timeout.HasValue)
            {
                var completed = await Task.WhenAny(_checkTask, Task.Delay(timeout.Value));
                if (completed == _checkTask)
                {
                    return await _checkTask;
                }
                return null;
            }

            return await _checkTask;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Displays update notification if an update is available.
    /// </summary>
    public static void DisplayUpdateNotificationIfAvailable()
    {
        var info = _cachedUpdateInfo;
        if (info?.IsUpdateAvailable == true)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Update Available[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine($"[grey]A new version ([green]v{info.LatestVersion}[/]) is available. Run [cyan]ironhive update[/] to update.[/]");
            AnsiConsole.WriteLine();
        }
    }
}
