namespace IronHive.Host.Config;

/// <summary>
/// Puts a project <c>.env</c> into the process environment without replacing anything the process already has: what the
/// host exported wins over what a folder's file says. What an earlier load took from a file is taken back first, so the
/// file being loaded - after an edit, or for another folder - is read against the host's environment alone.
/// </summary>
internal static class ProjectEnvFile
{
    // The variables a .env file supplied to this process, each with the value the environment then held. A variable whose
    // value changed since then was set by someone else and is the process's own. (A value set to the very text the file
    // gave cannot be told apart from the file's.)
    private static readonly Dictionary<string, string> Supplied =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    // Read in either case by the HTTP stack and by the tools the engine starts, the lower-case spelling often first.
    private static readonly HashSet<string> ProxyNames =
        new(["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY"], StringComparer.OrdinalIgnoreCase);

    private static readonly Lock Gate = new();

    /// <param name="path">The folder's <c>.env</c>; it need not exist.</param>
    /// <param name="read">
    /// Reads the environment the file has just been put into. It runs before any other load can change it: another load
    /// takes back and supplies the same process variables.
    /// </param>
    public static void LoadAndRead(string path, Action read)
    {
        lock (Gate)
        {
            TakeBack();
            if (File.Exists(path))
            {
                Supply(path);
            }

            read();
        }
    }

    /// <summary>
    /// The value of a setting that has two variable names. A name the process set itself comes before a name a
    /// <c>.env</c> file supplied, whichever of the two it is; otherwise <paramref name="name"/> comes first. Call it only
    /// from the read that <see cref="LoadAndRead"/> runs.
    /// </summary>
    public static string? ReadEither(string name, string alias)
    {
        var first = Environment.GetEnvironmentVariable(name);
        var second = Environment.GetEnvironmentVariable(alias);
        var aliasWins = first is null || (second is not null && Supplied.ContainsKey(name) && !Supplied.ContainsKey(alias));
        return aliasWins ? second : first;
    }

    private static bool ProcessHas(string name) =>
        Environment.GetEnvironmentVariable(name) is not null
        || (ProxyNames.Contains(name) && Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && !Supplied.ContainsKey(key)));

    // Parsed only: nothing is set while the file is read. A "${NAME}" reads the environment, and where that lacks the
    // name, the first line of this file that set it.
    private static List<KeyValuePair<string, string>> Parse(string content)
    {
        // DotNetEnv keeps the values it has parsed in a static table and reads "${NAME}" from it, also across files.
        DotNetEnv.Env.FakeEnvVars.Clear();
        return [.. DotNetEnv.Env.LoadContents(content, new DotNetEnv.LoadOptions(setEnvVars: false, clobberExistingVars: false))];
    }

    private static void Supply(string path)
    {
        var content = File.ReadAllText(path);

        try
        {
            // A file that does not parse fails here, before anything is set.
            var entries = Parse(content);

            // With a reference ("$"), one entry at a time, each read with the entries before it already in the
            // environment: a "${NAME}" then reads what the file had said by that entry, or the process's own value
            // where it has one. Without one, no value depends on another and one reading is enough.
            var refers = content.Contains('$');
            for (var entry = 0; entry < entries.Count; entry++)
            {
                var (name, value) = refers ? Parse(content)[entry] : entries[entry];

                // A variable set to nothing is set: that is how a host keeps a file from supplying it. One this load
                // has supplied is replaced by a later line of the same file.
                if (Supplied.ContainsKey(name) || !ProcessHas(name))
                {
                    Environment.SetEnvironmentVariable(name, value);
                    // What the environment holds, not what the file said: it keeps a value only up to a NUL.
                    Supplied[name] = Environment.GetEnvironmentVariable(name) ?? value;
                }
            }
        }
        finally
        {
            // The library's table is process-wide and public: nothing of this file stays in it.
            DotNetEnv.Env.FakeEnvVars.Clear();
        }
    }

    private static void TakeBack()
    {
        foreach (var (name, given) in Supplied)
        {
            if (Environment.GetEnvironmentVariable(name) == given)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        Supplied.Clear();
    }
}
