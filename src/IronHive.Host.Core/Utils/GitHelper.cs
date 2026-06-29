using System.Diagnostics;
using System.Text;

namespace IronHive.Host.Core.Utils;

/// <summary>
/// Helper class for Git operations.
/// </summary>
public static class GitHelper
{
    /// <summary>
    /// Checks if the current directory is a Git repository.
    /// </summary>
    public static bool IsGitRepository(string? workingDirectory = null)
    {
        var dir = workingDirectory ?? Directory.GetCurrentDirectory();
        return Directory.Exists(Path.Combine(dir, ".git"));
    }

    /// <summary>
    /// Checks if Git is available on the system.
    /// </summary>
    public static bool IsGitAvailable()
    {
        try
        {
            var result = RunGit("--version");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current status of the repository.
    /// </summary>
    public static GitStatus GetStatus(string? workingDirectory = null)
    {
        var result = RunGit("status --porcelain", workingDirectory);
        if (result.ExitCode != 0)
        {
            return new GitStatus { HasChanges = false };
        }

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var staged = new List<string>();
        var unstaged = new List<string>();
        var untracked = new List<string>();

        foreach (var line in lines)
        {
            if (line.Length < 2)
            {
                continue;
            }

            var indexStatus = line[0];
            var workTreeStatus = line[1];
            var filePath = line.Substring(3).Trim();

            if (indexStatus == '?')
            {
                untracked.Add(filePath);
            }
            else if (indexStatus != ' ')
            {
                staged.Add(filePath);
            }

            if (workTreeStatus != ' ' && workTreeStatus != '?')
            {
                unstaged.Add(filePath);
            }
        }

        return new GitStatus
        {
            HasChanges = lines.Length > 0,
            StagedFiles = staged,
            UnstagedFiles = unstaged,
            UntrackedFiles = untracked
        };
    }

    /// <summary>
    /// Stages all changes (including untracked files).
    /// </summary>
    public static bool StageAll(string? workingDirectory = null)
    {
        var result = RunGit("add -A", workingDirectory);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Stages specific files.
    /// </summary>
    public static bool StageFiles(IEnumerable<string> files, string? workingDirectory = null)
    {
        var fileList = string.Join(" ", files.Select(f => $"\"{f}\""));
        var result = RunGit($"add {fileList}", workingDirectory);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Creates a commit with the given message.
    /// </summary>
    public static bool Commit(string message, string? workingDirectory = null)
    {
        // Escape double quotes in the message
        var escapedMessage = message.Replace("\"", "\\\"");
        var result = RunGit($"commit -m \"{escapedMessage}\"", workingDirectory);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Gets a diff summary of the current changes.
    /// </summary>
    public static string GetDiffSummary(string? workingDirectory = null)
    {
        var result = RunGit("diff --stat", workingDirectory);
        if (result.ExitCode != 0)
        {
            return string.Empty;
        }

        return result.Output;
    }

    /// <summary>
    /// Gets detailed diff of staged changes.
    /// </summary>
    public static string GetStagedDiff(string? workingDirectory = null)
    {
        var result = RunGit("diff --cached", workingDirectory);
        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    public static string? GetCurrentBranch(string? workingDirectory = null)
    {
        var result = RunGit("rev-parse --abbrev-ref HEAD", workingDirectory);
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Gets the last commit message.
    /// </summary>
    public static string? GetLastCommitMessage(string? workingDirectory = null)
    {
        var result = RunGit("log -1 --pretty=%B", workingDirectory);
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Gets recent commit messages (for style reference).
    /// </summary>
    public static IReadOnlyList<string> GetRecentCommitMessages(int count = 5, string? workingDirectory = null)
    {
        var result = RunGit($"log --oneline -n {count}", workingDirectory);
        if (result.ExitCode != 0)
        {
            return Array.Empty<string>();
        }

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static GitResult RunGit(string arguments, string? workingDirectory = null)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            return new GitResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }
        catch (Exception ex)
        {
            return new GitResult
            {
                ExitCode = -1,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    private struct GitResult
    {
        public int ExitCode;
        public string Output;
        public string Error;
    }
}

/// <summary>
/// Represents the status of a Git repository.
/// </summary>
public class GitStatus
{
    /// <summary>
    /// Whether there are any changes (staged, unstaged, or untracked).
    /// </summary>
    public bool HasChanges { get; init; }

    /// <summary>
    /// List of staged file paths.
    /// </summary>
    public IReadOnlyList<string> StagedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// List of unstaged (modified but not staged) file paths.
    /// </summary>
    public IReadOnlyList<string> UnstagedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// List of untracked file paths.
    /// </summary>
    public IReadOnlyList<string> UntrackedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Total count of changed files.
    /// </summary>
    public int TotalChangedFiles => StagedFiles.Count + UnstagedFiles.Count + UntrackedFiles.Count;
}
