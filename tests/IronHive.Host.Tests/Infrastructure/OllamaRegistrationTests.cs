using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The CLI's <c>ollama</c> section reaches an OpenAI-compatible provider on Ollama's <c>/v1</c> surface — with or without
/// the path in the endpoint — and sends no key (Ollama needs none).
/// </summary>
public class OllamaRegistrationTests
{
    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://localhost:11434/")]
    [InlineData("http://localhost:11434/v1")]
    public void Endpoint_resolves_to_the_v1_surface(string endpoint)
    {
        var config = ServiceCollectionExtensions.CreateOllamaConfig(new CliConfig.OllamaConfig { Endpoint = endpoint });

        config.ToOpenAI().BaseUrl.TrimEnd('/').Should().Be("http://localhost:11434/v1");
        config.ApiKey.Should().BeNull();
    }
}
