using AwesomeAssertions;
using IronHive.Cli.Commands;
using IronHive.Host.Config;
using IronHive.Host.Update;
using NSubstitute;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// <c>update.check: false</c> (or <c>IRONHIVE_UPDATE_CHECK=false</c>) means the default command contacts no release source:
/// the background check is not started at all. Unset keeps the check on.
/// </summary>
public sealed class UpdateCheckSettingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "update-check-" + Guid.NewGuid().ToString("N"));
    private readonly string? _envBefore = Environment.GetEnvironmentVariable("IRONHIVE_UPDATE_CHECK");

    public UpdateCheckSettingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("IRONHIVE_UPDATE_CHECK", _envBefore);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ConfigurationManager Manager() => new(_root, Path.Combine(_root, "global.yaml"));

    private void WriteProjectConfig(string yaml)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".ironhive"));
        File.WriteAllText(Path.Combine(_root, ".ironhive", "config.yaml"), yaml);
    }

    [Fact]
    public void The_default_keeps_the_check_on()
    {
        Environment.SetEnvironmentVariable("IRONHIVE_UPDATE_CHECK", null);

        Manager().Load(forceReload: true).Update.Check.Should().BeTrue();
    }

    [Fact]
    public void The_file_can_turn_it_off()
    {
        Environment.SetEnvironmentVariable("IRONHIVE_UPDATE_CHECK", null);
        WriteProjectConfig("update:\n  check: false\n");

        Manager().Load(forceReload: true).Update.Check.Should().BeFalse();
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    public void The_environment_variable_wins_over_the_file(string value, bool expected)
    {
        WriteProjectConfig(expected ? "update:\n  check: false\n" : "update:\n  check: true\n");
        Environment.SetEnvironmentVariable("IRONHIVE_UPDATE_CHECK", value);

        Manager().Load(forceReload: true).Update.Check.Should().Be(expected);
    }

    [Fact]
    public void A_value_that_is_not_a_boolean_leaves_the_file_setting()
    {
        WriteProjectConfig("update:\n  check: false\n");
        Environment.SetEnvironmentVariable("IRONHIVE_UPDATE_CHECK", "0");

        Manager().Load(forceReload: true).Update.Check.Should().BeFalse();
    }

    [Fact]
    public async Task Off_contacts_no_release_source()
    {
        var service = Substitute.For<IUpdateService>();

        var started = UpdateChecker.StartBackgroundCheck(service, new UpdateHostConfig { Check = false });

        started.Should().BeFalse();
        await service.DidNotReceiveWithAnyArgs().CheckForUpdateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task On_asks_the_release_source_once()
    {
        var service = Substitute.For<IUpdateService>();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>()).Returns(new UpdateInfo { LatestVersion = new Version(1, 0), CurrentVersion = new Version(1, 0) });

        var started = UpdateChecker.StartBackgroundCheck(service, new UpdateHostConfig());
        await UpdateChecker.WaitForCheckAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        started.Should().BeTrue();
        await service.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>());
    }
}
