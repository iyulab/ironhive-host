using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IronHive.Host.Tests.Server;

/// <summary>
/// A host adds an event type of its own to the <see cref="ServerEvent"/> stream with a type-info modifier: the runner writes
/// it under the host's discriminator, and a reader with the same modifier reads it back. The built-in events are unchanged.
/// </summary>
public class ServerEventExtensionTests
{
    // The README's example, as written there.
    private sealed record CitationEvent(string Source, int Page) : ServerEvent;

    // Options this class uses once before handing them to the runner — STJ installs its reflection resolver on first use.
    private static readonly JsonSerializerOptions UsedOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly JsonSerializerOptions PlainOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static void AddAppEvents(JsonTypeInfo info)
    {
        if (info.Type == typeof(ServerEvent))
        {
            info.PolymorphismOptions!.DerivedTypes.Add(new JsonDerivedType(typeof(CitationEvent), "citation"));
        }
    }

    [Fact]
    public async Task A_registered_event_type_is_written_with_its_discriminator_and_reads_back()
    {
        var runner = new AgentServerRunner(
            (_, ct) => Events([new TextDeltaEvent("answer"), new CitationEvent("guide.pdf", 3)], ct),
            Substitute.For<ILogger<AgentServerRunner>>(),
            typeInfoModifiers: [AddAppEvents]);
        var input = new StringReader("""{"type":"user_message","content":"q"}""" + "\n" + """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, TestContext.Current.CancellationToken);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        lines.Should().Contain("""{"type":"citation","source":"guide.pdf","page":3}""");
        lines.Should().Contain("""{"type":"text_delta","content":"answer"}""");

        var reader = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { AddAppEvents } },
        };
        var read = lines.Select(l => JsonSerializer.Deserialize<ServerEvent>(l, reader)).ToList();
        read.Should().ContainEquivalentOf(new CitationEvent("guide.pdf", 3));
        read.Should().ContainEquivalentOf(new TextDeltaEvent("answer"));
    }

    [Fact]
    public async Task The_modifier_applies_when_the_options_already_carry_a_resolver()
    {
        // Options that have been used once carry STJ's reflection resolver (the runner's own default options do after the
        // first serialization in the process). The modifier used to be combined behind it and never ran.
        _ = JsonSerializer.Serialize<ServerEvent>(new TextDeltaEvent("warm"), UsedOptions);
        var runner = new AgentServerRunner(
            (_, ct) => Events([new CitationEvent("guide.pdf", 3)], ct),
            Substitute.For<ILogger<AgentServerRunner>>(),
            jsonOptions: UsedOptions,
            typeInfoModifiers: [AddAppEvents]);
        var input = new StringReader("""{"type":"user_message","content":"q"}""" + "\n" + """{"type":"shutdown"}""");
        using var output = new StringWriter();

        await runner.RunAsync(input, output, TestContext.Current.CancellationToken);

        output.ToString().Should().Contain("""{"type":"citation","source":"guide.pdf","page":3}""");
    }

    [Fact]
    public void Without_the_modifier_the_event_type_is_refused()
    {
        var act = () => JsonSerializer.Serialize<ServerEvent>(new CitationEvent("guide.pdf", 3), PlainOptions);

        act.Should().Throw<NotSupportedException>();
    }

    private static async IAsyncEnumerable<ServerEvent> Events(
        ServerEvent[] events, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }
        await Task.CompletedTask;
    }
}
