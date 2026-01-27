using IronHive.Cli.Core.Agent.Mode;

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
}
