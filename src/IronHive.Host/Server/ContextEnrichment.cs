using System.Globalization;
using System.Text;
using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// Puts the last <see cref="ContextUpdateRequest"/> in front of a user message: <c>[WorkingPath: …]</c> when it named a
/// working path, then <c>[Selected: a, b]</c> when it named selected items. Both runners use it, so a host can leave
/// enrichment to the runner instead of rebuilding it.
/// </summary>
internal static class ContextEnrichment
{
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
