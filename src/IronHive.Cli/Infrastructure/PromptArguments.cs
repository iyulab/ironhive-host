namespace IronHive.Cli.Infrastructure;

/// <summary>
/// The command-line parser reads every argument that starts with a dash as an option, so a prompt that begins with one —
/// a markdown bullet (<c>- You are given ...</c>), a quoted list — ended <c>run -p "&lt;prompt&gt;"</c> with
/// «Option does not have a name» before the agent started. A task statement written as a bullet list is an ordinary prompt.
/// </summary>
internal static class PromptArguments
{
    /// <summary>
    /// Rewrites <c>-p &lt;text&gt;</c> / <c>--prompt &lt;text&gt;</c> to <c>--prompt=&lt;text&gt;</c> when the text starts with a dash
    /// and contains whitespace — so it is prose, not an option name — which the parser reads whole as the option's value.
    /// Everything else is passed through untouched (a value without a leading dash, an option after a forgotten value,
    /// an <c>--prompt=...</c> already in that form).
    /// </summary>
    public static string[] Normalize(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "-p" or "--prompt" && i + 1 < args.Length && IsProse(args[i + 1]))
            {
                result.Add("--prompt=" + args[i + 1]);
                i++;
                continue;
            }

            result.Add(args[i]);
        }

        return [.. result];
    }

    // An option name never contains whitespace; a sentence does.
    private static bool IsProse(string argument) =>
        argument.Length > 0 && argument[0] == '-' && argument.Any(char.IsWhiteSpace);
}
