using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Agent.Mode;

/// <summary>
/// Filters available tools based on the current agent mode.
/// </summary>
public interface IModeToolFilter
{
    /// <summary>
    /// Filters tools based on the current mode.
    /// </summary>
    /// <param name="tools">All available tools</param>
    /// <param name="mode">Current agent mode</param>
    /// <returns>Tools permitted in the current mode</returns>
    IList<AITool> FilterTools(IList<AITool> tools, AgentMode mode);

    /// <summary>
    /// Checks if a tool is permitted in the current mode.
    /// </summary>
    /// <param name="toolName">Name of the tool</param>
    /// <param name="mode">Current agent mode</param>
    /// <returns>True if the tool is permitted</returns>
    bool IsToolPermitted(string toolName, AgentMode mode);

    /// <summary>
    /// Checks if a tool operation is considered risky and requires HITL approval.
    /// </summary>
    /// <param name="toolName">Name of the tool</param>
    /// <param name="arguments">Tool arguments</param>
    /// <returns>Risk assessment result</returns>
    RiskAssessment AssessRisk(string toolName, IDictionary<string, object?>? arguments);
}

/// <summary>
/// Result of risk assessment for a tool operation.
/// </summary>
public record RiskAssessment
{
    /// <summary>
    /// Whether the operation is considered risky.
    /// </summary>
    public bool IsRisky { get; init; }

    /// <summary>
    /// Risk level (Low, Medium, High, Critical).
    /// </summary>
    public RiskLevel Level { get; init; }

    /// <summary>
    /// Human-readable description of why this is risky.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Suggested user prompt for HITL approval.
    /// </summary>
    public string? ApprovalPrompt { get; init; }

    /// <summary>
    /// Creates a non-risky assessment.
    /// </summary>
    public static RiskAssessment Safe => new() { IsRisky = false, Level = RiskLevel.Low };

    /// <summary>
    /// Creates a risky assessment.
    /// </summary>
    public static RiskAssessment Risky(RiskLevel level, string reason, string? approvalPrompt = null) =>
        new() { IsRisky = true, Level = level, Reason = reason, ApprovalPrompt = approvalPrompt };
}

/// <summary>
/// Risk levels for tool operations.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Low risk - safe operations.
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk - may need review.
    /// </summary>
    Medium,

    /// <summary>
    /// High risk - requires explicit approval.
    /// </summary>
    High,

    /// <summary>
    /// Critical risk - destructive operations.
    /// </summary>
    Critical
}

/// <summary>
/// Default implementation of mode tool filter.
/// </summary>
public class ModeToolFilter : IModeToolFilter
{
    // Read-only tools allowed in Planning mode
    private static readonly HashSet<string> ReadOnlyTools =
    [
        "read_file",
        "glob",
        "grep",
        "list_directory"
    ];

    // Tools that can modify state (require Working mode)
    private static readonly HashSet<string> WriteTools =
    [
        "write_file",
        "shell",
        "create_directory",
        "delete_file",
        "move_file"
    ];

    // High-risk tools/operations
    private static readonly HashSet<string> HighRiskTools =
    [
        "delete_file",
        "shell" // Certain shell commands are high-risk
    ];

    /// <inheritdoc />
    public IList<AITool> FilterTools(IList<AITool> tools, AgentMode mode)
    {
        return mode switch
        {
            AgentMode.Idle => [], // No tools in idle
            AgentMode.Planning => tools.Where(t => IsToolPermitted(GetToolName(t), mode)).ToList(),
            AgentMode.Working => tools.ToList(), // All tools in working mode
            AgentMode.HumanInTheLoop => [], // No tools while waiting for approval
            _ => []
        };
    }

    /// <inheritdoc />
    public bool IsToolPermitted(string toolName, AgentMode mode)
    {
        return mode switch
        {
            AgentMode.Idle => false,
            AgentMode.Planning => ReadOnlyTools.Contains(toolName),
            AgentMode.Working => true, // All tools permitted (but may trigger HITL)
            AgentMode.HumanInTheLoop => false,
            _ => false
        };
    }

    /// <inheritdoc />
    public RiskAssessment AssessRisk(string toolName, IDictionary<string, object?>? arguments)
    {
        // Check if tool itself is high-risk
        if (toolName == "delete_file")
        {
            return RiskAssessment.Risky(
                RiskLevel.High,
                "File deletion is a destructive operation",
                "Allow deleting this file?");
        }

        // Shell commands need special assessment
        if (toolName == "shell" && arguments?.TryGetValue("command", out var cmd) == true)
        {
            var command = cmd?.ToString() ?? string.Empty;

            // Check for dangerous patterns
            if (IsDangerousShellCommand(command))
            {
                return RiskAssessment.Risky(
                    RiskLevel.Critical,
                    $"Potentially dangerous shell command: {TruncateCommand(command)}",
                    "Allow executing this command?");
            }

            // Check for elevated privileges
            if (RequiresElevation(command))
            {
                return RiskAssessment.Risky(
                    RiskLevel.High,
                    "Command requires elevated privileges",
                    "Allow executing with elevated privileges?");
            }
        }

        return RiskAssessment.Safe;
    }

    private static string GetToolName(AITool tool)
    {
        // AITool may be AIFunction or other types
        if (tool is AIFunction func)
        {
            return func.Name;
        }

        return tool.GetType().Name;
    }

    private static bool IsDangerousShellCommand(string command)
    {
        var lowerCommand = command.ToLowerInvariant();

        // Dangerous patterns
        var dangerousPatterns = new[]
        {
            "rm -rf",
            "del /s /q",
            "format ",
            "fdisk",
            "mkfs",
            ":(){:|:&};:", // Fork bomb
            "> /dev/sda",
            "dd if=",
            "chmod 777",
            "curl | sh",
            "wget | sh"
        };

        return dangerousPatterns.Any(p => lowerCommand.Contains(p));
    }

    private static bool RequiresElevation(string command)
    {
        var lowerCommand = command.ToLowerInvariant();

        var elevationPatterns = new[]
        {
            "sudo ",
            "runas ",
            "doas "
        };

        return elevationPatterns.Any(p => lowerCommand.StartsWith(p, StringComparison.Ordinal));
    }

    private static string TruncateCommand(string command)
    {
        const int maxLength = 50;
        return command.Length > maxLength
            ? command[..maxLength] + "..."
            : command;
    }
}
