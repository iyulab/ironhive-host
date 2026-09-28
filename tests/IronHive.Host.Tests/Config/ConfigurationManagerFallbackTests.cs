using AwesomeAssertions;
using IronHive.Agent.Permissions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// Three configuration promises the README makes that the loader did not keep: config.yaml's <c>permissions</c> section
/// (merged, then replaced by the permission-file lookup even when there was no file), <c>LMSUPPLY_ENABLED=false</c>
/// (re-enabled whenever no remote provider was configured), and a skills root written <c>~/…</c> (resolved as a
/// directory literally named «~» under the working directory).
/// </summary>
[Collection(nameof(ProcessWorkingDirectoryScope))]
public sealed class ConfigurationManagerFallbackTests : IDisposable
{
    private readonly TempConfigDirs _dirs = new();
    private readonly string? _previousLmSupply = Environment.GetEnvironmentVariable("LMSUPPLY_ENABLED");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LMSUPPLY_ENABLED", _previousLmSupply);
        _dirs.Dispose();
    }

    private const string ConfigPermissions = """
        permissions:
          read:
            - pattern: "docs/**"
              action: Allow
          defaultAction: Deny
        """;

    [Fact]
    public void ConfigYamlPermissions_Apply_WhenTheProjectHasNoPermissionFile()
    {
        _dirs.WriteGlobal(ConfigPermissions);

        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.Permissions.Read.Should().ContainSingle().Which.Pattern.Should().Be("docs/**");
        config.Permissions.DefaultAction.Should().Be(PermissionAction.Deny);
        config.Permissions.WorkingDirectory.Should().Be(_dirs.ProjectRoot);
    }

    [Fact]
    public void AProjectPermissionFile_TakesPrecedenceOverConfigYaml()
    {
        _dirs.WriteGlobal(ConfigPermissions);
        File.WriteAllText(Path.Combine(_dirs.ProjectRoot, ".ironhive", "permissions.json"),
            """{ "permissions": { "read": [ { "pattern": "from-file", "action": "allow" } ] } }""");

        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.Permissions.Read.Should().ContainSingle().Which.Pattern.Should().Be("from-file");
    }

    [Fact]
    public void WithNeitherSource_TheDefaultRulesApply()
    {
        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.Permissions.Read.Should().BeEquivalentTo(PermissionConfig.CreateDefault().Read);
    }

    [Fact]
    public void LmSupplyEnabledFalse_KeepsLocalInferenceOff_EvenWithNoRemoteProvider()
    {
        Environment.SetEnvironmentVariable("LMSUPPLY_ENABLED", "false");

        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.LMSupply.Enabled.Should().BeFalse();
    }

    [Fact]
    public void WithoutLmSupplyEnabled_LocalInferenceIsTheFallback()
    {
        Environment.SetEnvironmentVariable("LMSUPPLY_ENABLED", null);

        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.LMSupply.Enabled.Should().BeTrue("no remote provider is configured in an empty config");
    }

    [Fact]
    public void ASkillsRootUnderTilde_IsTheHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var skills = new SkillsHostConfig { Roots = ["~/.ironhive/skills", "~", ".ironhive/skills"] };

        var roots = skills.ToSkillsConfig(_dirs.ProjectRoot).Roots;

        roots.Should().Equal(
            Path.GetFullPath(Path.Combine(home, ".ironhive/skills")),
            Path.GetFullPath(home),
            Path.GetFullPath(".ironhive/skills", _dirs.ProjectRoot));
    }
}
