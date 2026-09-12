using AwesomeAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.AI;
using Xunit;

namespace IronHive.Host.Tests.Server;

/// <summary>
/// A client can restrict one turn to a subset of the agent's tools, a tool mode, a reasoning effort
/// or sampling values; everything it leaves unset stays on the loop's configuration. Restrictions
/// that cannot be honoured are errors, never silent drops.
/// </summary>
public class TurnOptionsMapperTests
{
    private static readonly IReadOnlyList<AITool> Registered =
    [
        AIFunctionFactory.Create(() => "read", "ReadFile"),
        AIFunctionFactory.Create(() => "write", "WriteFile"),
        AIFunctionFactory.Create(() => "search", "Search")
    ];

    [Fact]
    public void NoOptions_MeansNoOverride()
    {
        TurnOptionsMapper.ToChatOptions(null, Registered).Should().BeNull(
            "a request without options must run the loop exactly as before options existed");
    }

    [Fact]
    public void ToolNames_SelectTheSubsetOfRegisteredTools_ByName()
    {
        var chat = TurnOptionsMapper.ToChatOptions(new TurnOptions(ToolNames: ["ReadFile", "Search"]), Registered)!;

        chat.Tools.Should().NotBeNull();
        chat.Tools!.Select(t => t.Name).Should().Equal("ReadFile", "Search");
        chat.Tools.Should().OnlyContain(t => Registered.Contains(t), "the same instances the loop registered, not copies");
        chat.ToolMode.Should().BeNull("unset fields keep the loop's value");
        chat.Temperature.Should().BeNull();
    }

    [Fact]
    public void EmptyToolNames_MeansNoToolsThisTurn()
    {
        var chat = TurnOptionsMapper.ToChatOptions(new TurnOptions(ToolNames: []), Registered)!;

        chat.Tools.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void UnknownToolName_IsAnError_NotASilentDrop()
    {
        var act = () => TurnOptionsMapper.ToChatOptions(new TurnOptions(ToolNames: ["ReadFile", "Nope"]), Registered);

        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("Nope").And.Contain("ReadFile");
    }

    [Theory]
    [InlineData("auto", typeof(AutoChatToolMode))]
    [InlineData("none", typeof(NoneChatToolMode))]
    [InlineData("require_any", typeof(RequiredChatToolMode))]
    public void ToolMode_MapsTheDocumentedValues(string mode, Type expected)
    {
        var chat = TurnOptionsMapper.ToChatOptions(new TurnOptions(ToolMode: mode), Registered)!;

        chat.ToolMode.Should().BeOfType(expected);
    }

    [Fact]
    public void ToolMode_RequireSpecific_NamesARegisteredTool()
    {
        var chat = TurnOptionsMapper.ToChatOptions(new TurnOptions(ToolMode: "require:WriteFile"), Registered)!;

        chat.ToolMode.Should().BeOfType<RequiredChatToolMode>().Which.RequiredFunctionName.Should().Be("WriteFile");
    }

    [Theory]
    [InlineData("require:Nope")]
    [InlineData("sometimes")]
    public void ToolMode_Unknown_IsAnError(string mode)
    {
        var act = () => TurnOptionsMapper.ToChatOptions(new TurnOptions(ToolMode: mode), Registered);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReasoningEffort_AndSampling_MapThrough()
    {
        var chat = TurnOptionsMapper.ToChatOptions(
            new TurnOptions(ReasoningEffort: "high", Temperature: 0.2f, MaxOutputTokens: 512), Registered)!;

        chat.Reasoning.Should().NotBeNull();
        chat.Reasoning!.Effort.Should().Be(ReasoningEffort.High);
        chat.Temperature.Should().Be(0.2f);
        chat.MaxOutputTokens.Should().Be(512);
        chat.Tools.Should().BeNull("tools were not mentioned, so the loop's tools stay");
    }

    [Fact]
    public void ReasoningEffort_Unknown_IsAnError()
    {
        var act = () => TurnOptionsMapper.ToChatOptions(new TurnOptions(ReasoningEffort: "max"), Registered);

        act.Should().Throw<ArgumentException>();
    }
}
