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

    private static string SelectedLine(string text) => text.Split(Environment.NewLine)[0];

    private static ContextEnrichmentOptions Limit(int max) => new() { MaxSelectedItems = max };

    [Fact]
    public void Past_the_limit_the_tail_counts_the_rest_and_names_the_folder_they_share()
    {
        var items = new[] { "/w/a.txt", "/w/b.txt", "/w/reports/c.txt", "/w/reports/d.txt", "/w/reports/e.txt" };

        var text = ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(2));

        text.Should().Be(string.Join(
            Environment.NewLine, "[Selected: /w/a.txt, /w/b.txt (and 3 more in /w/reports)]", "", "hello"));
    }

    [Fact]
    public void Past_the_limit_items_in_different_folders_leave_the_tail_with_the_count_only()
    {
        var items = new[] { "/w/a.txt", "/w/x/b.txt", "/w/y/c.txt" };

        SelectedLine(ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(1)))
            .Should().Be("[Selected: /w/a.txt (and 2 more)]");
    }

    [Fact]
    public void Past_the_limit_names_without_a_folder_leave_the_tail_with_the_count_only()
    {
        var items = new[] { "a.txt", "b.txt", "c.txt" };

        SelectedLine(ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(1)))
            .Should().Be("[Selected: a.txt (and 2 more)]");
    }

    [Fact]
    public void Past_the_limit_a_backslash_folder_is_named_as_it_was_written()
    {
        var items = new[] { @"C:\w\a.txt", @"C:\w\reports\b.txt", @"C:\w\reports\c.txt" };

        SelectedLine(ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(1)))
            .Should().Be(@"[Selected: C:\w\a.txt (and 2 more in C:\w\reports)]");
    }

    [Fact]
    public void A_selection_at_or_under_the_limit_has_no_tail()
    {
        var items = new[] { "/w/a.txt", "/w/b.txt" };

        SelectedLine(ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(2)))
            .Should().Be("[Selected: /w/a.txt, /w/b.txt]");
        SelectedLine(ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(50)))
            .Should().Be("[Selected: /w/a.txt, /w/b.txt]");
    }

    [Fact]
    public void Blank_items_count_neither_toward_the_limit_nor_toward_the_rest()
    {
        var items = new[] { " ", "/w/a.txt", "", "/w/b.txt", "/w/c.txt" };

        SelectedLine(ContextEnrichment.Apply(new ContextUpdateRequest(null, items), "hello", Limit(1)))
            .Should().Be("[Selected: /w/a.txt (and 2 more in /w)]");
    }

    [Fact]
    public void No_options_or_no_limit_writes_every_item_as_the_two_argument_overload_does()
    {
        var context = new ContextUpdateRequest("/work", ["a.txt", "b.txt", "c.txt"]);
        var plain = ContextEnrichment.Apply(context, "hello");

        ContextEnrichment.Apply(context, "hello", null).Should().Be(plain);
        ContextEnrichment.Apply(context, "hello", new ContextEnrichmentOptions()).Should().Be(plain);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_limit_below_one_throws(int max)
    {
        var act = () => ContextEnrichment.Apply(new ContextUpdateRequest(null, ["a"]), "hello", Limit(max));

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*MaxSelectedItems*");
    }
}
