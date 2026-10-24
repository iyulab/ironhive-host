using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using IronHive.Providers.OpenAI.Compatible;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>instructionsPlacement</c> in the CLI config reaches the provider config the CLI registers for the Chat Completions
/// endpoints; unset means <see cref="InstructionsPlacement.SystemAppend"/> (instructions at the end of the system message).
/// </summary>
public class InstructionsPlacementWiringTests
{
    [Theory]
    [InlineData(InstructionsPlacement.LeadingUserTurn, InstructionsPlacement.LeadingUserTurn)]
    [InlineData(InstructionsPlacement.SeparateSystemMessage, InstructionsPlacement.SeparateSystemMessage)]
    [InlineData(null, InstructionsPlacement.SystemAppend)]
    public void LMStudio(InstructionsPlacement? configured, InstructionsPlacement expected) =>
        ServiceCollectionExtensions.CreateLMStudioConfig(new CliConfig.LMStudioConfig { InstructionsPlacement = configured })
            .InstructionsPlacement.Should().Be(expected);

    [Theory]
    [InlineData(InstructionsPlacement.LeadingUserTurn, InstructionsPlacement.LeadingUserTurn)]
    [InlineData(null, InstructionsPlacement.SystemAppend)]
    public void Ollama(InstructionsPlacement? configured, InstructionsPlacement expected) =>
        ServiceCollectionExtensions.CreateOllamaConfig(new CliConfig.OllamaConfig { InstructionsPlacement = configured })
            .InstructionsPlacement.Should().Be(expected);

    [Theory]
    [InlineData(InstructionsPlacement.LeadingUserTurn, InstructionsPlacement.LeadingUserTurn)]
    [InlineData(null, InstructionsPlacement.SystemAppend)]
    public void OpenAICompatible(InstructionsPlacement? configured, InstructionsPlacement expected) =>
        ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
                new CliConfig.OpenAICompatibleHostConfig { Endpoint = "http://gw:8000/v1", Model = "m", InstructionsPlacement = configured })
            .InstructionsPlacement.Should().Be(expected);

    [Theory]
    [InlineData(InstructionsPlacement.LeadingUserTurn, InstructionsPlacement.LeadingUserTurn)]
    [InlineData(null, InstructionsPlacement.SystemAppend)]
    public void GpuStack(InstructionsPlacement? configured, InstructionsPlacement expected) =>
        ServiceCollectionExtensions.CreateGpuStackConfig(
                new CliConfig.GpuStackConfig { Endpoint = "http://gpu:80", ApiKey = "k", InstructionsPlacement = configured })
            .InstructionsPlacement.Should().Be(expected);
}
