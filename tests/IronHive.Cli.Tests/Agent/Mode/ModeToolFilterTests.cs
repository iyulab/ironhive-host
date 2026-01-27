using IronHive.Cli.Core.Agent.Mode;
using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Tests.Agent.Mode;

/// <summary>
/// Tests for ModeToolFilter risk assessment and tool filtering.
/// </summary>
public class ModeToolFilterTests
{
    private readonly ModeToolFilter _filter = new();

    [Theory]
    [InlineData("read_file", AgentMode.Planning, true)]
    [InlineData("glob", AgentMode.Planning, true)]
    [InlineData("grep", AgentMode.Planning, true)]
    [InlineData("list_directory", AgentMode.Planning, true)]
    [InlineData("write_file", AgentMode.Planning, false)]
    [InlineData("shell", AgentMode.Planning, false)]
    [InlineData("delete_file", AgentMode.Planning, false)]
    public void IsToolPermitted_PlanningMode_FiltersCorrectly(string toolName, AgentMode mode, bool expected)
    {
        var result = _filter.IsToolPermitted(toolName, mode);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("read_file", AgentMode.Working, true)]
    [InlineData("write_file", AgentMode.Working, true)]
    [InlineData("shell", AgentMode.Working, true)]
    [InlineData("delete_file", AgentMode.Working, true)]
    public void IsToolPermitted_WorkingMode_AllowsAllTools(string toolName, AgentMode mode, bool expected)
    {
        var result = _filter.IsToolPermitted(toolName, mode);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("read_file", AgentMode.Idle, false)]
    [InlineData("write_file", AgentMode.Idle, false)]
    [InlineData("read_file", AgentMode.HumanInTheLoop, false)]
    [InlineData("write_file", AgentMode.HumanInTheLoop, false)]
    public void IsToolPermitted_IdleOrHITL_DeniesAllTools(string toolName, AgentMode mode, bool expected)
    {
        var result = _filter.IsToolPermitted(toolName, mode);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AssessRisk_DeleteFile_ReturnsHighRisk()
    {
        var result = _filter.AssessRisk("delete_file", null);

        Assert.True(result.IsRisky);
        Assert.Equal(RiskLevel.High, result.Level);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void AssessRisk_SafeShellCommand_ReturnsSafe()
    {
        var args = new Dictionary<string, object?> { ["command"] = "ls -la" };

        var result = _filter.AssessRisk("shell", args);

        Assert.False(result.IsRisky);
        Assert.Equal(RiskLevel.Low, result.Level);
    }

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("sudo rm -rf .")]
    [InlineData("del /s /q c:\\")]
    [InlineData("format c:")]
    public void AssessRisk_DangerousShellCommand_ReturnsCriticalRisk(string command)
    {
        var args = new Dictionary<string, object?> { ["command"] = command };

        var result = _filter.AssessRisk("shell", args);

        Assert.True(result.IsRisky);
        Assert.True(result.Level >= RiskLevel.High);
    }

    [Theory]
    [InlineData("sudo apt update")]
    [InlineData("runas /user:admin cmd")]
    [InlineData("doas pkg install")]
    public void AssessRisk_ElevatedCommand_ReturnsHighRisk(string command)
    {
        var args = new Dictionary<string, object?> { ["command"] = command };

        var result = _filter.AssessRisk("shell", args);

        Assert.True(result.IsRisky);
        Assert.True(result.Level >= RiskLevel.High);
    }

    [Fact]
    public void AssessRisk_ReadFile_ReturnsSafe()
    {
        var result = _filter.AssessRisk("read_file", null);

        Assert.False(result.IsRisky);
        Assert.Equal(RiskLevel.Low, result.Level);
    }

    [Fact]
    public void RiskAssessment_Safe_CreatesNonRiskyAssessment()
    {
        var result = RiskAssessment.Safe;

        Assert.False(result.IsRisky);
        Assert.Equal(RiskLevel.Low, result.Level);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void RiskAssessment_Risky_CreatesRiskyAssessment()
    {
        var result = RiskAssessment.Risky(RiskLevel.High, "Test reason", "Test prompt");

        Assert.True(result.IsRisky);
        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal("Test reason", result.Reason);
        Assert.Equal("Test prompt", result.ApprovalPrompt);
    }

    #region Approval Whitelist Tests

    [Fact]
    public void AssessRisk_AutoApprovedTool_ReturnsSafe()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedTools = ["delete_file"],
            AlwaysPromptForCritical = false
        };
        var filter = new ModeToolFilter(config);

        var result = filter.AssessRisk("delete_file", null);

        Assert.False(result.IsRisky);
    }

    [Fact]
    public void AssessRisk_AutoApprovedTool_StillPromptsForCritical()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedTools = ["delete_file"],
            AlwaysPromptForCritical = true // Default
        };
        var filter = new ModeToolFilter(config);

        var result = filter.AssessRisk("delete_file", null);

        // delete_file is high risk but not critical, so it should still be risky
        Assert.True(result.IsRisky);
    }

    [Fact]
    public void AssessRisk_AutoApprovedPath_ReturnsSafe()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedPaths = ["*.tmp", "obj/**"]
        };
        var filter = new ModeToolFilter(config);

        var args = new Dictionary<string, object?> { ["path"] = "test.tmp" };
        var result = filter.AssessRisk("delete_file", args);

        Assert.False(result.IsRisky);
    }

    [Fact]
    public void AssessRisk_AutoApprovedCommand_ReturnsSafe()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedCommands = ["git *", "dotnet build"]
        };
        var filter = new ModeToolFilter(config);

        var args = new Dictionary<string, object?> { ["command"] = "git status" };
        var result = filter.AssessRisk("shell", args);

        Assert.False(result.IsRisky);
    }

    [Fact]
    public void AssessRisk_AutoApprovedCommand_StillBlocksDangerous()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedCommands = ["*"] // Allow everything
        };
        var filter = new ModeToolFilter(config);

        // Dangerous commands should still be blocked
        var args = new Dictionary<string, object?> { ["command"] = "rm -rf /" };
        var result = filter.AssessRisk("shell", args);

        Assert.True(result.IsRisky);
        Assert.Equal(RiskLevel.Critical, result.Level);
    }

    [Fact]
    public void AssessRisk_NonWhitelistedPath_StillRisky()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedPaths = ["*.tmp"]
        };
        var filter = new ModeToolFilter(config);

        var args = new Dictionary<string, object?> { ["path"] = "important.cs" };
        var result = filter.AssessRisk("delete_file", args);

        Assert.True(result.IsRisky);
    }

    #endregion

    #region ApprovalConfig Tests

    [Theory]
    [InlineData("git status", "git *", true)]
    [InlineData("git commit -m test", "git *", true)]
    [InlineData("dotnet build", "dotnet build", true)]
    [InlineData("dotnet test", "dotnet build", false)]
    [InlineData("npm install", "npm *", true)]
    [InlineData("yarn install", "npm *", false)]
    public void ApprovalConfig_IsCommandAutoApproved_MatchesPatterns(string command, string pattern, bool expected)
    {
        var config = new ApprovalConfig
        {
            AutoApprovedCommands = [pattern]
        };

        var result = config.IsCommandAutoApproved(command);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("test.tmp", "*.tmp", true)]
    [InlineData("obj/Debug/test.dll", "obj/**", true)]
    [InlineData("bin/Release/app.exe", "bin/**", true)]
    [InlineData("src/Program.cs", "*.tmp", false)]
    [InlineData("src/obj/test.dll", "obj/**", false)] // obj must be at root
    public void ApprovalConfig_IsPathAutoApproved_MatchesPatterns(string path, string pattern, bool expected)
    {
        var config = new ApprovalConfig
        {
            AutoApprovedPaths = [pattern]
        };

        var result = config.IsPathAutoApproved(path);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApprovalConfig_IsToolAutoApproved_CaseInsensitive()
    {
        var config = new ApprovalConfig
        {
            AutoApprovedTools = ["Shell", "DELETE_FILE"]
        };

        Assert.True(config.IsToolAutoApproved("shell"));
        Assert.True(config.IsToolAutoApproved("SHELL"));
        Assert.True(config.IsToolAutoApproved("delete_file"));
        Assert.False(config.IsToolAutoApproved("read_file"));
    }

    #endregion
}
