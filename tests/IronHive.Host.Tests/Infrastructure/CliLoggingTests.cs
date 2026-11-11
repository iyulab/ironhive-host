using AwesomeAssertions;
using IronHive.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The README says a CLI run's log output goes to stderr. The container the CLI builds had <c>ILogger</c> but no provider, so every
/// message the host logs - an MCP plugin that failed to load, a skill discovery refused - was silently dropped.
/// </summary>
public class CliLoggingTests
{
    [Fact]
    public void TheContainer_HasAConsoleProvider_AndWarningsAreEnabledByDefault()
    {
        var services = new ServiceCollection();
        services.AddIronHiveServices();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<ILoggerProvider>().Should().NotBeEmpty("without a provider every ILogger message is dropped");
        var logger = provider.GetRequiredService<ILogger<AgentLoopFactory>>();
        logger.IsEnabled(LogLevel.Warning).Should().Be(
            IronHive.Cli.Infrastructure.ServiceCollectionExtensions.ResolveLogLevel(Environment.GetEnvironmentVariable("IRONHIVE_LOG_LEVEL")) <= LogLevel.Warning);
        logger.IsEnabled(LogLevel.Critical).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, LogLevel.Warning)]
    [InlineData("", LogLevel.Warning)]
    [InlineData("nonsense", LogLevel.Warning)]
    [InlineData("42", LogLevel.Warning)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("TRACE", LogLevel.Trace)]
    [InlineData("None", LogLevel.None)]
    public void IronHiveLogLevel_ChoosesTheLevel_AndAnUnknownValueKeepsWarning(string? value, LogLevel expected)
        => IronHive.Cli.Infrastructure.ServiceCollectionExtensions.ResolveLogLevel(value).Should().Be(expected);
}
