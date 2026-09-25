using AwesomeAssertions;
using IronHive.Agent.Permissions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

[CollectionDefinition(nameof(ProcessWorkingDirectoryScope), DisableParallelization = true)]
public sealed class ProcessWorkingDirectoryScope;

/// <summary>
/// The permission file belongs to the same project scope as config.yaml and .env: with a projectRoot,
/// nothing in the process working directory reaches the loaded config.
/// </summary>
[Collection(nameof(ProcessWorkingDirectoryScope))]
public sealed class ConfigurationManagerPermissionScopeTests : IDisposable
{
    private const string ValidPermissions = """
        { "permissions": { "read": [ { "pattern": "from-project-root", "action": "allow" } ] } }
        """;

    private readonly TempConfigDirs _dirs = new();
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "ihcwd-" + Guid.NewGuid().ToString("N"));
    private readonly string _previousCwd = Directory.GetCurrentDirectory();

    public ConfigurationManagerPermissionScopeTests()
    {
        Directory.CreateDirectory(Path.Combine(_workDir, ".ironhive"));
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousCwd);
        _dirs.Dispose();
        try { Directory.Delete(_workDir, true); } catch { }
    }

    private static void WritePermissions(string root, string content) =>
        File.WriteAllText(Path.Combine(root, ".ironhive", "permissions.json"), content);

    [Fact]
    public void Load_PermissionFileUnderProjectRoot_IsRead_WhileWorkingDirectoryIsElsewhere()
    {
        WritePermissions(_dirs.ProjectRoot, ValidPermissions);
        Directory.SetCurrentDirectory(_workDir);

        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.Permissions.Read.Should().ContainSingle().Which.Pattern.Should().Be("from-project-root");
        config.Permissions.WorkingDirectory.Should().Be(_dirs.ProjectRoot);
    }

    [Fact]
    public void Load_UnreadablePermissionFileInWorkingDirectory_DoesNotReachAConfigWithAProjectRoot()
    {
        WritePermissions(_workDir, "{ not json");
        Directory.SetCurrentDirectory(_workDir);

        var config = new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        config.Permissions.Read.Should().BeEquivalentTo(PermissionConfig.CreateDefault().Read);
    }

    [Fact]
    public void Load_UnreadablePermissionFileInWorkingDirectory_StillThrows_WithoutAProjectRoot()
    {
        // Positive control for the fact above: the same file is read (and refused) when the working
        // directory is the project scope.
        WritePermissions(_workDir, "{ not json");
        Directory.SetCurrentDirectory(_workDir);

        var load = () => new ConfigurationManager(projectRoot: null, globalConfigPath: _dirs.GlobalConfigPath).Load();

        load.Should().Throw<PermissionConfigException>();
    }

    [Fact]
    public void Load_UnreadablePermissionFileUnderProjectRoot_Throws()
    {
        WritePermissions(_dirs.ProjectRoot, "{ not json");
        Directory.SetCurrentDirectory(_workDir);

        var load = () => new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

        load.Should().Throw<PermissionConfigException>();
    }
}
