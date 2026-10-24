using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>instructionsAsSystemMessage</c> in the CLI config reaches the provider config the CLI registers for the Chat
/// Completions endpoints; unset means off (instructions at the end of the system message).
/// </summary>
public class InstructionsAsSystemMessageWiringTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void LMStudio(bool? configured, bool expected) =>
        ServiceCollectionExtensions.CreateLMStudioConfig(new CliConfig.LMStudioConfig { InstructionsAsSystemMessage = configured })
            .InstructionsAsSeparateSystemMessage.Should().Be(expected);

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public void Ollama(bool? configured, bool expected) =>
        ServiceCollectionExtensions.CreateOllamaConfig(new CliConfig.OllamaConfig { InstructionsAsSystemMessage = configured })
            .InstructionsAsSeparateSystemMessage.Should().Be(expected);

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public void OpenAICompatible(bool? configured, bool expected) =>
        ServiceCollectionExtensions.CreateOpenAICompatibleConfig(
                new CliConfig.OpenAICompatibleHostConfig { Endpoint = "http://gw:8000/v1", Model = "m", InstructionsAsSystemMessage = configured })
            .InstructionsAsSeparateSystemMessage.Should().Be(expected);
}
