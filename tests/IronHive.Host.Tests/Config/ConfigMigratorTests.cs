using System.IO;
using FluentAssertions;
using IronHive.Host.Core.Config;
using Xunit;

namespace IronHive.Host.Tests.Config;

public class ConfigMigratorTests
{
    [Fact]
    public void MigrateIfNeeded_LegacySettingsPresentNoYaml_WritesConfigYaml()
    {
        using var tmp = new TempConfigDirs();
        File.WriteAllText(tmp.LegacySettingsPath,
            "{\"openai\":{\"apiKey\":\"legacy\",\"model\":\"gpt-x\"}}");

        var migrated = ConfigMigrator.MigrateIfNeeded(tmp.GlobalConfigPath, tmp.ProjectRoot, tmp.LegacySettingsPath);

        migrated.Should().BeTrue();
        File.Exists(tmp.GlobalConfigPath).Should().BeTrue();
        var loaded = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        loaded.OpenAI.ApiKey.Should().Be("legacy");
        loaded.OpenAI.Model.Should().Be("gpt-x");
    }

    [Fact]
    public void MigrateIfNeeded_ConfigYamlAlreadyExists_NoOp()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("openai:\n  apiKey: existing\n");
        File.WriteAllText(tmp.LegacySettingsPath, "{\"openai\":{\"apiKey\":\"legacy\"}}");

        ConfigMigrator.MigrateIfNeeded(tmp.GlobalConfigPath, tmp.ProjectRoot, tmp.LegacySettingsPath)
            .Should().BeFalse();
        File.ReadAllText(tmp.GlobalConfigPath).Should().Contain("existing"); // not overwritten
    }

    [Fact]
    public void MigrateIfNeeded_NoLegacyFile_NoOp()
    {
        using var tmp = new TempConfigDirs();
        ConfigMigrator.MigrateIfNeeded(tmp.GlobalConfigPath, tmp.ProjectRoot, tmp.LegacySettingsPath)
            .Should().BeFalse();
        File.Exists(tmp.GlobalConfigPath).Should().BeFalse();
    }
}
