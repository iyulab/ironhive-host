using AwesomeAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;

namespace IronHive.Host.Tests.Server;

public class ContextEnrichmentTests
{
    [Fact]
    public void Both_fields_go_in_front_of_the_message_working_path_first()
    {
        var text = ContextEnrichment.Apply(new ContextUpdateRequest("/work", ["a.txt", "b.txt"]), "hello");

        text.Should().Be(string.Join(Environment.NewLine, "[WorkingPath: /work]", "[Selected: a.txt, b.txt]", "", "hello"));
    }

    [Fact]
    public void No_context_or_an_empty_one_leaves_the_message_as_it_is()
    {
        ContextEnrichment.Apply(null, "hello").Should().Be("hello");
        ContextEnrichment.Apply(new ContextUpdateRequest(null, []), "hello").Should().Be("hello");
        ContextEnrichment.Apply(new ContextUpdateRequest(null, [" ", ""]), "hello").Should().Be("hello");
    }

    [Fact]
    public void A_working_path_alone_keeps_the_earlier_prefix()
    {
        ContextEnrichment.Apply(new ContextUpdateRequest("/work"), "hello")
            .Should().Be(string.Join(Environment.NewLine, "[WorkingPath: /work]", "", "hello"));
    }
}
