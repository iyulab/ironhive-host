using AwesomeAssertions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// Pins <c>SetCommand.ShowValidKeys()</c>'s advertised top-level section keys against the
/// loader's real keys (<see cref="IronHiveConfig"/>'s <c>YamlMemberAttribute</c> aliases /
/// CamelCaseNamingConvention-derived names), so the CLI's `set`/`config` help text can never
/// silently drift from what <see cref="ConfigurationManager.Load"/> actually reads.
/// A drifted key (e.g. advertising "google" while the loader only reads "googleai") causes a
/// user-typed `ironhive set google.apiKey ...` to write a key the loader treats as unknown and
/// silently drops -- see ConfigurationManager.FindUnknownKeys.
/// </summary>
public class SetCommandDriftGuardTests
{
    /// <summary>
    /// The exact top-level section keys advertised by <c>SetCommand.ShowValidKeys()</c>
    /// (and mirrored as display labels in <c>ConfigCommand.ShowConfig()</c>), post-fix.
    /// Keep this array in sync with SetCommand.cs's validKeys dictionary keys.
    /// </summary>
    private static readonly string[] AdvertisedSectionKeys =
    [
        "openai",
        "anthropic",
        "googleai",
        "xai",
        "gpuStack",
        "ollama",
        "lmstudio",
        "lmsupply"
    ];

    [Fact]
    public void SetCommand_AdvertisedSectionKeys_AreAllValidLoaderKeys()
    {
        var yaml = string.Join("\n", AdvertisedSectionKeys.Select(k => $"{k}:\n  x: 1"));

        var unknown = ConfigurationManager.FindUnknownKeys(yaml);

        // The placeholder member "x" is not a real key; only the sections are under test here.
        unknown.Where(k => !k.Contains('.')).Should().BeEmpty(
            "every key SetCommand's help advertises must be a real loader-recognized section key");
    }

    [Theory]
    [InlineData("google")] // wrong: real key is "googleai"
    [InlineData("azure")] // never a provider the CLI registers
    [InlineData("azureopenai")] // removed: no IronHive provider served it; a config that still has it is warned about
    [InlineData("gpustack")] // wrong case: real key is "gpuStack"
    public void KnownStaleKeys_AreCorrectlyReportedAsUnknown(string staleKey)
    {
        var yaml = $"{staleKey}:\n  x: 1\n";

        var unknown = ConfigurationManager.FindUnknownKeys(yaml);

        unknown.Should().Contain(staleKey,
            "this key was the pre-fix (wrong) SetCommand advertisement and must not be a valid loader key");
    }
}
