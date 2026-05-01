using System.Text.Json;
using FluentAssertions;
using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Tests.Config;

/// <summary>
/// Unit tests for <see cref="ChatBehaviorConfig"/> — Phase D-4 (consumer-tunable
/// FunctionInvokingChatClient caps, ecosystem ISSUE 2026-04-30 follow-up after Filer
/// cycle-699). Filer §6.4 asked for the iteration cap to be a config knob so consumers
/// can tune it per-model (small models with 4K windows want a lower cap; 16K+ models
/// can take a higher one) without forking the cli source.
/// </summary>
public class ChatBehaviorConfigTests
{
    [Fact]
    public void Defaults_MatchProductionDecoratorChainValues()
    {
        var config = new ChatBehaviorConfig();

        config.MaximumIterationsPerRequest.Should().Be(10,
            because: "must match the previous hard-coded value in ServiceCollectionExtensions to avoid behavior drift on consumers that don't override");
        config.MaximumConsecutiveErrorsPerRequest.Should().Be(3,
            because: "must match the previous hard-coded value");
    }

    [Fact]
    public void IronHiveConfig_ExposesChatBehaviorProperty_WithDefaultInstance()
    {
        var config = new IronHiveConfig();

        config.ChatBehavior.Should().NotBeNull(
            because: "ChatBehavior must be a non-null sub-config so consumers can tune values without null-checking");
        config.ChatBehavior.MaximumIterationsPerRequest.Should().Be(10);
        config.ChatBehavior.MaximumConsecutiveErrorsPerRequest.Should().Be(3);
    }

    [Fact]
    public void JsonRoundTrip_PreservesCustomValues()
    {
        var original = new ChatBehaviorConfig
        {
            MaximumIterationsPerRequest = 5,
            MaximumConsecutiveErrorsPerRequest = 2
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ChatBehaviorConfig>(json);

        restored.Should().NotBeNull();
        restored!.MaximumIterationsPerRequest.Should().Be(5);
        restored.MaximumConsecutiveErrorsPerRequest.Should().Be(2);
    }

    [Fact]
    public void IronHiveConfigJsonRoundTrip_PreservesCustomChatBehaviorValues()
    {
        var original = new IronHiveConfig();
        original.ChatBehavior.MaximumIterationsPerRequest = 15;
        original.ChatBehavior.MaximumConsecutiveErrorsPerRequest = 5;

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<IronHiveConfig>(json);

        restored.Should().NotBeNull();
        restored!.ChatBehavior.MaximumIterationsPerRequest.Should().Be(15,
            because: "settings.json round-trip must preserve the user's iteration cap override");
        restored.ChatBehavior.MaximumConsecutiveErrorsPerRequest.Should().Be(5);
    }
}
