using AwesomeAssertions;
using IronHive.Host.Config;
using CliServices = IronHive.Cli.Infrastructure.ServiceCollectionExtensions;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// <see cref="ProviderBlocks"/> is the one list of remote providers: every block has a registration, a config section the
/// loader and <c>ironhive set</c> accept, and its aliases resolve to it — so adding a provider in one place cannot leave
/// another list behind.
/// </summary>
public class ProviderBlocksTests
{
    [Fact]
    public void EveryBlock_HasARegistration_AndNoRegistrationLacksABlock()
    {
        var factories = CliServices.ProviderFactories(new IronHiveConfig());

        factories.Keys.Should().BeEquivalentTo(ProviderBlocks.All.Select(p => p.Name));
    }

    [Fact]
    public void EveryBlockSection_IsALoaderKey()
    {
        var yaml = string.Join("\n", ProviderBlocks.All.Select(p => $"{p.Section}:\n  model: m"));

        ConfigurationManager.FindUnknownKeys(yaml).Should().BeEmpty();
    }

    [Fact]
    public void EveryBlockSection_IsAdvertisedBySet()
    {
        SetCommandDriftGuardTests.AdvertisedSectionKeys.Should().Contain(ProviderBlocks.All.Select(p => p.Section));
    }

    [Theory]
    [InlineData("claude", "anthropic")]
    [InlineData("GEMINI", "google")]
    [InlineData("openai-compatible", "openaiCompatible")]
    [InlineData("gpustack", "gpustack")]
    public void Find_ResolvesNamesAndAliases(string name, string expected) =>
        ProviderBlocks.Find(name)!.Name.Should().Be(expected);

    [Fact]
    public void TheDefaultOrder_StartsWithTheOrganizationGateways() =>
        ProviderBlocks.All.Select(p => p.Name).Take(3).Should().Equal("gpustack", "openaiCompatible", "openai");
}
