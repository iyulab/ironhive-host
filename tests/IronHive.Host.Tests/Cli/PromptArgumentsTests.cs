using AwesomeAssertions;
using IronHive.Cli.Infrastructure;

namespace IronHive.Host.Tests.Cli;

/// <summary>
/// A prompt that starts with a dash (a markdown bullet) is the option's value: the parser would otherwise read it as an
/// option and end the run with «Option does not have a name» before the agent started.
/// </summary>
public class PromptArgumentsTests
{
    [Theory]
    [InlineData("-p")]
    [InlineData("--prompt")]
    public void ABulletPrompt_BecomesTheOptionsOwnValue(string option)
    {
        var args = PromptArguments.Normalize(["run", "--json", option, "- You are given a file.\n- Fix it.", "--provider", "gpustack"]);

        args.Should().Equal("run", "--json", "--prompt=- You are given a file.\n- Fix it.", "--provider", "gpustack");
    }

    [Theory]
    [InlineData("fix the bug")]
    [InlineData("-")]
    [InlineData("--json")]
    [InlineData("-x")]
    public void OtherValues_PassThroughUntouched(string value)
    {
        string[] args = ["run", "-p", value];

        PromptArguments.Normalize(args).Should().Equal(args);
    }

    [Fact]
    public void APromptAlreadyInTheEqualsForm_IsLeftAlone()
    {
        string[] args = ["run", "--prompt=- a bullet", "--json"];

        PromptArguments.Normalize(args).Should().Equal(args);
    }

    [Fact]
    public void AForgottenPromptFollowedByAnOption_IsNotSwallowed()
    {
        string[] args = ["run", "-p", "--json", "--provider", "gpustack"];

        PromptArguments.Normalize(args).Should().Equal(args, "an option name has no whitespace, so it is not taken for a prompt");
    }

    [Fact]
    public void NoArguments_AndATrailingOption_AreSafe()
    {
        PromptArguments.Normalize([]).Should().BeEmpty();
        PromptArguments.Normalize(["run", "-p"]).Should().Equal("run", "-p");
    }
}
