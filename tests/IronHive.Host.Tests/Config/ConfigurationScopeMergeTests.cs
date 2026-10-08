using AwesomeAssertions;
using IronHive.Agent.Permissions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// How the global and project <c>config.yaml</c> combine: a key the project file sets wins, a key it does not set keeps the
/// global value — for every section and every member type, including numbers and booleans. <c>permissions</c> is the one
/// section a scope replaces as a whole (a project's permission rules are a complete policy).
/// </summary>
public class ConfigurationScopeMergeTests
{
    [Fact]
    public void AValueTypeTheProjectDoesNotSet_KeepsTheGlobalValue()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("compaction:\n  targetRatio: 0.6\n  protectRecentTokens: 12345\nollama:\n  enabled: true\nlmstudio:\n  enabled: true\n");
        tmp.WriteProject("openai:\n  model: project-model\n");

        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();

        config.Compaction.TargetRatio.Should().Be(0.6f);
        config.Compaction.ProtectRecentTokens.Should().Be(12345);
        config.Ollama.Enabled.Should().BeTrue();
        config.LMStudio.Enabled.Should().BeTrue();
        config.OpenAI.Model.Should().Be("project-model");
    }

    [Fact]
    public void AValueTheProjectSets_WinsOverTheGlobalValue_AndTheRestOfTheSectionStays()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("compaction:\n  targetRatio: 0.6\n  protectRecentTokens: 12345\n");
        tmp.WriteProject("compaction:\n  targetRatio: 0.5\n");

        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();

        config.Compaction.TargetRatio.Should().Be(0.5f);
        config.Compaction.ProtectRecentTokens.Should().Be(12345);
    }

    [Fact]
    public void TheApprovalSection_IsRead()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("approval:\n  timeoutSeconds: 1234\n");

        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();

        config.Approval.TimeoutSeconds.Should().Be(1234);
    }

    [Fact]
    public void EverySection_ReachesTheConfig_FromTheGlobalFile()
    {
        // A non-default value for one member of every section: a section the loader forgets reads as its default.
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("""
            gpuStack: { model: g }
            openai: { model: o }
            anthropic: { model: a }
            googleai: { model: gg }
            xai: { model: x }
            lmsupply: { generatorModel: l }
            ollama: { model: ol }
            lmstudio: { model: ls }
            compaction: { targetRatio: 0.55 }
            budget: { maxSessionTokens: 777 }
            agentsMd: { enabled: false }
            toolRetrieval: { enabled: true }
            webSearch: { defaultMaxResults: 7 }
            deepResearch: { enabled: true }
            chatBehavior: { maxOutputTokens: 999 }
            advisor: { maxCalls: 9 }
            skills: { maxMetadataCharacters: 4321 }
            delegation: { maxDepth: 4 }
            approval: { timeoutSeconds: 42 }
            """);

        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();

        config.GpuStack.Model.Should().Be("g");
        config.OpenAI.Model.Should().Be("o");
        config.Anthropic.Model.Should().Be("a");
        config.GoogleAI.Model.Should().Be("gg");
        config.Xai.Model.Should().Be("x");
        config.LMSupply.GeneratorModel.Should().Be("l");
        config.Ollama.Model.Should().Be("ol");
        config.LMStudio.Model.Should().Be("ls");
        config.Compaction.TargetRatio.Should().Be(0.55f);
        config.Budget.MaxSessionTokens.Should().Be(777);
        config.AgentsMd.Enabled.Should().BeFalse();
        config.ToolRetrieval.Enabled.Should().BeTrue();
        config.WebSearch.DefaultMaxResults.Should().Be(7);
        config.DeepResearch.Enabled.Should().BeTrue();
        config.ChatBehavior.MaxOutputTokens.Should().Be(999);
        config.Advisor.MaxCalls.Should().Be(9);
        config.Skills.MaxMetadataCharacters.Should().Be(4321);
        config.Delegation.MaxDepth.Should().Be(4);
        config.Approval.TimeoutSeconds.Should().Be(42);
    }

    [Fact]
    public void AProjectBudgetSection_ReplacesTheGlobalOneWhole()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("budget:\n  maxSessionTokens: 1000\n  warningThreshold: 0.5\n  stopOnLimit: false\n");
        tmp.WriteProject("budget:\n  maxSessionCost: 2.5\n");

        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();

        config.Budget.MaxSessionCost.Should().Be(2.5m);
        config.Budget.MaxSessionTokens.Should().Be(0, "the project's budget is the whole budget");
        config.Budget.StopOnLimit.Should().BeTrue("the threshold and stop flag belong to the scope's own limits");
    }

    [Fact]
    public void AProjectPermissionsSection_ReplacesTheGlobalOneWhole()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("permissions:\n  read:\n    - pattern: \"global/**\"\n      action: Allow\n  defaultAction: Deny\n");
        tmp.WriteProject("permissions:\n  edit:\n    - pattern: \"src/**\"\n      action: Allow\n");

        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();

        config.Permissions.Edit.Should().ContainSingle().Which.Pattern.Should().Be("src/**");
        config.Permissions.Read.Should().BeEmpty("the project's rules are the whole policy, not added to the global ones");
        config.Permissions.DefaultAction.Should().Be(PermissionAction.Ask);
    }
}
