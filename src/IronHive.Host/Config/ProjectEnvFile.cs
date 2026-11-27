namespace IronHive.Host.Config;

/// <summary>
/// Puts a project <c>.env</c> into the process environment without replacing anything the process already has: what the
/// host exported wins over what a folder's file says. What an earlier load took from a file is taken back first, so the
/// file being loaded - after an edit, or for another folder - is read against the host's environment alone.
/// </summary>
internal static class ProjectEnvFile
{
    // The variables a .env file supplied to this process, each with the value it gave. A variable whose value changed since
    // then was set by someone else and is the process's own.
    private static readonly Dictionary<string, string> Supplied =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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

    private static void Supply(string path)
    {
        // DotNetEnv keeps the values it has parsed in a static table and reads "${NAME}" from it, also across files.
        DotNetEnv.Env.FakeEnvVars.Clear();

        // Parsed only; with nothing clobbered, a "${NAME}" in the file reads the process's value where it has one.
        var entries = DotNetEnv.Env.Load(path, new DotNetEnv.LoadOptions(setEnvVars: false, clobberExistingVars: false));
        foreach (var (name, value) in entries)
        {
            // A variable set to nothing is set: that is how a host keeps a file from supplying it. One this load has
            // supplied is replaced by a later line of the same file.
            if (Environment.GetEnvironmentVariable(name) is null || Supplied.ContainsKey(name))
            {
                Environment.SetEnvironmentVariable(name, value);
                Supplied[name] = value;
            }
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
