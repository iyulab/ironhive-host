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
    /// skipped) in front of it, or <paramref name="userContent"/> unchanged when the context names neither.
    /// </summary>
    public static string Apply(ContextUpdateRequest? context, string userContent)
    {
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
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Selected: {string.Join(", ", selected)}]");
        }

        sb.AppendLine();
        sb.Append(userContent);
        return sb.ToString();
    }
}
