using FluentAssertions;
using IronHive.Cli.Infrastructure;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// Tests for ServiceCollectionExtensions.NormalizeEndpoint.
/// Validates endpoint URL normalization with various suffix and path combinations.
/// </summary>
public class NormalizeEndpointTests
{
    [Theory]
    [InlineData("http://host", null, "http://host/")]
    [InlineData("http://host/", null, "http://host/")]
    [InlineData("http://host///", null, "http://host/")]
    public void NoSuffix_EnsuresTrailingSlash(string endpoint, string? suffix, string expected)
    {
        ServiceCollectionExtensions.NormalizeEndpoint(endpoint, suffix)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("http://host", "v1-openai", "http://host/v1-openai/")]
    [InlineData("http://host/", "v1-openai", "http://host/v1-openai/")]
    [InlineData("http://host:8080", "v1-openai", "http://host:8080/v1-openai/")]
    public void GpuStack_AppendsSuffix_WhenNotPresent(string endpoint, string suffix, string expected)
    {
        ServiceCollectionExtensions.NormalizeEndpoint(endpoint, suffix)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("http://host/v1-openai", "v1-openai", "http://host/v1-openai/")]
    [InlineData("http://host/v1-openai/", "v1-openai", "http://host/v1-openai/")]
    [InlineData("http://host/V1-OPENAI", "v1-openai", "http://host/V1-OPENAI/")]
    public void GpuStack_NoChange_WhenSuffixAlreadyPresent(string endpoint, string suffix, string expected)
    {
        ServiceCollectionExtensions.NormalizeEndpoint(endpoint, suffix)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("http://host/v1", "v1-openai", "http://host/v1-openai/")]
    [InlineData("http://host/v1/", "v1-openai", "http://host/v1-openai/")]
    [InlineData("http://host:8080/v1", "v1-openai", "http://host:8080/v1-openai/")]
    [InlineData("http://172.19.10.10/v1", "v1-openai", "http://172.19.10.10/v1-openai/")]
    public void GpuStack_ReplacesPrefixSegment_WhenV1Provided(string endpoint, string suffix, string expected)
    {
        ServiceCollectionExtensions.NormalizeEndpoint(endpoint, suffix)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("http://localhost:11434", "api", "http://localhost:11434/api/")]
    [InlineData("http://localhost:11434/", "api", "http://localhost:11434/api/")]
    [InlineData("http://localhost:11434/api", "api", "http://localhost:11434/api/")]
    [InlineData("http://localhost:11434/api/", "api", "http://localhost:11434/api/")]
    public void Ollama_HandlesApiSuffix(string endpoint, string suffix, string expected)
    {
        ServiceCollectionExtensions.NormalizeEndpoint(endpoint, suffix)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", null, "https://api.openai.com/v1/")]
    [InlineData("https://api.openai.com/v1/", null, "https://api.openai.com/v1/")]
    public void OpenAI_NoSuffix_PreservesPath(string endpoint, string? suffix, string expected)
    {
        ServiceCollectionExtensions.NormalizeEndpoint(endpoint, suffix)
            .Should().Be(expected);
    }
}
