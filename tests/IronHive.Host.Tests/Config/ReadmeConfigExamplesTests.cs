using System.Text.RegularExpressions;
using AwesomeAssertions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// Every <c>```yaml</c> block in the README is a config example a user copies. A key the loader does not read is
/// ignored without effect, so an example that names one teaches a setting that does nothing.
/// </summary>
public partial class ReadmeConfigExamplesTests
{
    [Fact]
    public void ReadmeYamlExamples_UseOnlyKeysTheLoaderReads()
    {
        var readme = File.ReadAllText(FindReadme());
        var blocks = YamlBlock().Matches(readme).Select(m => m.Groups[1].Value).ToList();

        blocks.Should().NotBeEmpty("the README documents config.yaml with yaml examples");
        foreach (var block in blocks)
        {
            // FindUnknownKeys reports nothing for YAML it cannot parse, so the block must parse for the check to mean anything.
            YamlConfigSerializer.Deserialize<Dictionary<object, object>>(block).Should().NotBeEmpty($"README example parses:\n{block}");
            ConfigurationManager.FindUnknownKeys(block).Should().BeEmpty($"README example:\n{block}");
        }
    }

    /// <summary>
    /// A block fenced <c>```yaml permissions.yaml</c> is a permissions file example: it has to load as one (the loader refuses a
    /// file whose rules are not under <c>permissions:</c>, the shape the README once described).
    /// </summary>
    [Fact]
    public void ReadmePermissionsExamples_LoadAsPermissionFiles()
    {
        var readme = File.ReadAllText(FindReadme());
        var blocks = PermissionsBlock().Matches(readme).Select(m => m.Groups[1].Value).ToList();

        blocks.Should().NotBeEmpty("the README shows a permissions.yaml example");
        foreach (var block in blocks)
        {
            var path = Path.Combine(Path.GetTempPath(), $"readme-permissions-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(path, block);
            try
            {
                var load = () => IronHive.Agent.Permissions.PermissionConfigLoader.LoadFromYaml(path);
                load.Should().NotThrow($"README permissions example:\n{block}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static string FindReadme()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IronHive.Host.slnx")))
            {
                return Path.Combine(dir.FullName, "README.md");
            }
        }

        throw new InvalidOperationException("Repository root (IronHive.Host.slnx) not found above the test output.");
    }

    [GeneratedRegex(@"```yaml\r?\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex YamlBlock();

    [GeneratedRegex(@"```yaml permissions\.yaml\r?\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex PermissionsBlock();
}
