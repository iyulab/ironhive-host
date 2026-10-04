using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>streamIdleTimeoutSeconds</c> in a provider block reaches the provider config the CLI registers, for every provider
/// the CLI wires; unset is no limit, and a non-positive value is refused naming the key.
/// </summary>
public class StreamIdleTimeoutWiringTests
{
    private static readonly TimeSpan NinetySeconds = TimeSpan.FromSeconds(90);

    public static TheoryData<int?, TimeSpan> Cases => new()
    {
        { 90, TimeSpan.FromSeconds(90) },
        { null, Timeout.InfiniteTimeSpan },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void LMStudio(int? configured, TimeSpan expected) =>
        ServiceCollectionExtensions.CreateLMStudioConfig(new CliConfig.LMStudioConfig { StreamIdleTimeoutSeconds = configured })
            .StreamIdleTimeout.Should().Be(expected);

    [Theory]
    [MemberData(nameof(Cases))]
    public void GpuStack_ReachesTheChatCompletionsConfig(int? configured, TimeSpan expected) =>
        ServiceCollectionExtensions.CreateGpuStackConfig(new CliConfig.GpuStackConfig { Endpoint = "http://gpu:80", ApiKey = "k", StreamIdleTimeoutSeconds = configured })
            .ToOpenAICompatible().StreamIdleTimeout.Should().Be(expected);

    [Fact]
    public void OpenAI() =>
        ServiceCollectionExtensions.CreateOpenAIConfig(new CliConfig.OpenAIConfig { ApiKey = "k", StreamIdleTimeoutSeconds = 90 })
            .StreamIdleTimeout.Should().Be(NinetySeconds);

    [Fact]
    public void Anthropic() =>
        ServiceCollectionExtensions.CreateAnthropicConfig(new CliConfig.AnthropicConfig { ApiKey = "k", StreamIdleTimeoutSeconds = 90 })
            .StreamIdleTimeout.Should().Be(NinetySeconds);

    [Fact]
    public void GoogleAI() =>
        ServiceCollectionExtensions.CreateGoogleAIConfig(new CliConfig.GoogleAIConfig { ApiKey = "k", StreamIdleTimeoutSeconds = 90 })
            .StreamIdleTimeout.Should().Be(NinetySeconds);

    [Fact]
    public void Xai() =>
        ServiceCollectionExtensions.CreateXaiConfig(new CliConfig.XaiConfig { ApiKey = "k", StreamIdleTimeoutSeconds = 90 })
            .StreamIdleTimeout.Should().Be(NinetySeconds);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_value_is_refused_naming_the_key(int configured)
    {
        var act = () => ServiceCollectionExtensions.CreateLMStudioConfig(new CliConfig.LMStudioConfig { StreamIdleTimeoutSeconds = configured });

        act.Should().Throw<InvalidOperationException>().WithMessage("lmstudio.streamIdleTimeoutSeconds must be a positive*");
    }
}
