using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>carryToolImages</c> in the CLI config reaches the provider config the CLI registers, for the two Chat Completions
/// endpoints a local vision model sits behind (LM Studio / llama-server, GPUStack); unset means off.
/// </summary>
public class CarryToolImagesWiringTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void LMStudio(bool? configured, bool expected) =>
        ServiceCollectionExtensions.CreateLMStudioConfig(new CliConfig.LMStudioConfig { CarryToolImages = configured })
            .CarryImageToolResultsAsUserMessage.Should().Be(expected);

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public void GpuStack(bool? configured, bool expected) =>
        ServiceCollectionExtensions.CreateGpuStackConfig(new CliConfig.GpuStackConfig { Endpoint = "http://gpu:80", ApiKey = "k", CarryToolImages = configured })
            .ToOpenAICompatible().CarryImageToolResultsAsUserMessage.Should().Be(expected);
}
