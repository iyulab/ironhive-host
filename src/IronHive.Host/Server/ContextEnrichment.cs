using System.Globalization;
using System.Text;
using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// The runners' context format: the last <see cref="ContextUpdateRequest"/> in front of a user message,
/// <c>[WorkingPath: …]</c> when it named a working path, then <c>[Selected: a, b]</c> when it named selected items.
/// A runner built with a handler that takes the context applies nothing itself; such a handler calls this where it
/// chooses (after checking the user's own text, for example), so it never keeps its own copy of the format.
/// </summary>
public static class ContextEnrichment
{
    /// <summary>
    /// <paramref name="userContent"/> with <paramref name="context"/>'s working path and selected items (blank ones
    /// skipped) in front of it, or <paramref name="userContent"/> unchanged when the context names neither. Every
    /// selected item is written; <see cref="Apply(ContextUpdateRequest?, string, ContextEnrichmentOptions?)"/> bounds them.
    /// </summary>
    public static string Apply(ContextUpdateRequest? context, string userContent) => Apply(context, userContent, null);

    /// <summary>
    /// Like <see cref="Apply(ContextUpdateRequest?, string)"/>, with the selected items bounded by
    /// <paramref name="options"/>: past <see cref="ContextEnrichmentOptions.MaxSelectedItems"/> the first items are
    /// written and a tail says how many more there are, with their folder when they share one
    /// (<c>[Selected: a, b (and 297 more in /w/reports)]</c>). <c>null</c> options write every item.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ContextEnrichmentOptions.MaxSelectedItems"/> is below 1.</exception>
    public static string Apply(ContextUpdateRequest? context, string userContent, ContextEnrichmentOptions? options)
    {
        var limit = options?.MaxSelectedItems;
        if (limit is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), limit, "MaxSelectedItems must be at least 1 (null writes every selected item).");
        }

        var selected = context?.SelectedItems?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? [];
        if (context?.WorkingPath is null && selected.Length == 0)
        {
            return userContent;
        }

        var sb = new StringBuilder();
        if (context?.WorkingPath is { } workingPath)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[WorkingPath: {workingPath}]");
        }

        if (selected.Length > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Selected: {SelectedList(selected, limit)}]");
        }

        sb.AppendLine();
        sb.Append(userContent);
        return sb.ToString();
    }

    private static string SelectedList(string[] selected, int? limit)
    {
        if (limit is not { } max || selected.Length <= max)
        {
            return string.Join(", ", selected);
        }

        var rest = selected.AsSpan(max);
        var folder = SharedFolder(rest);
        var tail = folder is null
            ? $"(and {rest.Length.ToString(CultureInfo.InvariantCulture)} more)"
            : $"(and {rest.Length.ToString(CultureInfo.InvariantCulture)} more in {folder})";
        return $"{string.Join(", ", selected.Take(max))} {tail}";
    }

    // The directory part (up to the last '/' or '\') the items all have, or null when one has none or they differ.
    private static string? SharedFolder(ReadOnlySpan<string> items)
    {
        string? shared = null;
        foreach (var item in items)
        {
            var cut = item.AsSpan().LastIndexOfAny('/', '\\');
            if (cut <= 0)
            {
                return null;
            }

            var folder = item[..cut];
            if (shared is null)
            {
                shared = folder;
            }
            else if (!string.Equals(shared, folder, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return shared;
    }
}

/// <summary>How <see cref="ContextEnrichment.Apply(ContextUpdateRequest?, string, ContextEnrichmentOptions?)"/> writes the context.</summary>
public sealed record ContextEnrichmentOptions
{
    /// <summary>
    /// The most selected items written in full, or <c>null</c> (the default) to write every one. Past it the line ends
    /// with a tail naming how many more there are and, when they share one, their folder. The number is the caller's
    /// policy (its model's context, its UI); the tail's wording is the library's.
    /// </summary>
    public int? MaxSelectedItems { get; init; }
}
