using AwesomeAssertions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// For a <c>.env</c> that writes only names the process does not have, every variable ends up with the value the
/// library's ordinary load gives it - the one that sets each line as it goes, so a <c>${NAME}</c> reads what the file had
/// said by then. What differs from that load is only which names a file may set.
/// </summary>
[Collection(nameof(ProcessWorkingDirectoryScope))]
public sealed class DotEnvParityTests : IDisposable
{
    private readonly TempConfigDirs _dirs = new();

    public void Dispose() => _dirs.Dispose();

    public static TheoryData<string, string> Files => new()
    {
        { "plain", "PARITY_H=value\nPARITY_U=http://${PARITY_H}" },
        {
            "a repeated name, then another repeated name",
            "PARITY_K=a1\nPARITY_K=a2\nPARITY_E=http://${PARITY_K}\nPARITY_M=m1\nPARITY_M=m2"
        },
        { "a reference before the name is written", "PARITY_B=${PARITY_A}\nPARITY_A=a1\nPARITY_A=a2\nPARITY_C=${PARITY_B}" },
        { "a name appended to itself", "PARITY_V=a\nPARITY_V=${PARITY_V}b\nPARITY_V=${PARITY_V}c" },
        {
            "references between three lines of a name",
            "PARITY_H=1\nPARITY_U=${PARITY_H}\nPARITY_H=2\nPARITY_V=${PARITY_H}\nPARITY_H=3\nPARITY_W=${PARITY_H}"
        },
        { "a value over two lines", "PARITY_Q=\"first\nsecond\"\nPARITY_Q=\"${PARITY_Q}\nthird\"\nPARITY_R=${PARITY_Q}" },
        { "export and comments", "# a comment\nexport PARITY_X=1\n\nPARITY_Y=${PARITY_X} # trailing\nexport PARITY_X=2\nPARITY_Z=$PARITY_X" },
        { "single quotes", "PARITY_V=a\nPARITY_S='${PARITY_V}'\nPARITY_D=\"${PARITY_V}\"" },
        { "an empty line value", "PARITY_H=first\nPARITY_H=\nPARITY_U=x-${PARITY_H}" },
        { "names that differ in case", "PARITY_Host=x\nPARITY_U=${PARITY_HOST}\nPARITY_a=1\nPARITY_A=2\nPARITY_R=${PARITY_a}" },
        {
            "default and alternate forms",
            "PARITY_H=1\nPARITY_U=${PARITY_H-def}\nPARITY_H=2\nPARITY_V=${PARITY_H+alt}\nPARITY_W=${PARITY_MISSING-def}"
        },
    };

    // On Windows a reference finds a name written in another case, a required one too.
    [Fact]
    public void A_required_reference_in_another_case_loads_as_the_ordinary_load_reads_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Variable names ignore case on Windows only.");

        A_file_of_names_the_process_lacks_reads_as_the_ordinary_load_reads_it(
            "required, other case", "PARITY_Host=x\nPARITY_URL=http://${PARITY_HOST?}");
    }

    // The library's table of parsed values is process-wide and public: a load leaves nothing of the file in it.
    [Theory]
    [InlineData("PARITY_A=value")]
    [InlineData("PARITY_A=value\nPARITY_B=${PARITY_A}")]
    public void A_load_leaves_none_of_the_file_in_the_librarys_table(string content)
    {
        try
        {
            File.WriteAllText(Path.Combine(_dirs.ProjectRoot, ".env"), content);

            new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

            DotNetEnv.Env.FakeEnvVars.Should().BeEmpty();
        }
        finally
        {
            using var empty = new TempConfigDirs();
            new ConfigurationManager(empty.ProjectRoot, empty.GlobalConfigPath).Load();
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void A_file_of_names_the_process_lacks_reads_as_the_ordinary_load_reads_it(string shape, string content)
    {
        DotNetEnv.Env.FakeEnvVars.Clear();
        var names = DotNetEnv.Env.LoadContents(content, new DotNetEnv.LoadOptions(setEnvVars: false))
            .Select(entry => entry.Key).Distinct(StringComparer.Ordinal).ToList();
        names.Should().NotBeEmpty();
        Clear(names);

        DotNetEnv.Env.FakeEnvVars.Clear();
        DotNetEnv.Env.LoadContents(content);
        var ordinary = Read(names);
        Clear(names);

        try
        {
            File.WriteAllText(Path.Combine(_dirs.ProjectRoot, ".env"), content);
            new ConfigurationManager(_dirs.ProjectRoot, _dirs.GlobalConfigPath).Load();

            Read(names).Should().Equal(ordinary, shape);
        }
        finally
        {
            // A load of a folder without a file takes back what this one supplied.
            using var empty = new TempConfigDirs();
            new ConfigurationManager(empty.ProjectRoot, empty.GlobalConfigPath).Load();
            Clear(names);
        }
    }

    private static Dictionary<string, string?> Read(IEnumerable<string> names) =>
        names.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);

    private static void Clear(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
