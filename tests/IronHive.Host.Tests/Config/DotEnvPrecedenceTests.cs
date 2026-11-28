using AwesomeAssertions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// The project <c>.env</c> supplies variables the process does not have. A variable the process already has - the one a
/// host exported when it started the engine - wins over the file.
/// </summary>
[Collection(nameof(ProcessWorkingDirectoryScope))]
public sealed class DotEnvPrecedenceTests : IDisposable
{
    private static readonly string[] Variables =
    [
        "OPENAI_API_KEY", "OPENAI_ENDPOINT", "OPENAI_MODEL", "GOOGLEAI_API_KEY", "GOOGLE_API_KEY",
        "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy", "NO_PROXY", "no_proxy", "ALL_PROXY", "all_proxy",
    ];

    private readonly TempConfigDirs _dirs = new();
    private readonly (string Name, string? Value)[] _before =
        [.. Variables.Select(name => (name, Environment.GetEnvironmentVariable(name)))];

    public DotEnvPrecedenceTests()
    {
        foreach (var name in Variables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _before)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _dirs.Dispose();
    }

    private static void WriteDotEnv(TempConfigDirs dirs, params string[] lines) =>
        File.WriteAllLines(Path.Combine(dirs.ProjectRoot, ".env"), lines);

    private static ConfigurationManager Manager(TempConfigDirs dirs) => new(dirs.ProjectRoot, dirs.GlobalConfigPath);

    [Fact]
    public void A_variable_the_process_has_wins_over_the_project_env_file()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "key-from-host");
        Environment.SetEnvironmentVariable("OPENAI_ENDPOINT", "http://from-host.invalid");
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://from-file.invalid", "OPENAI_API_KEY=key-from-file");

        var config = Manager(_dirs).Load();

        config.OpenAI.Endpoint.Should().Be("http://from-host.invalid");
        config.OpenAI.ApiKey.Should().Be("key-from-host");
        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT").Should().Be("http://from-host.invalid");
    }

    // The positive control: the file is read, and what the process lacks comes from it.
    [Fact]
    public void A_variable_the_process_lacks_comes_from_the_project_env_file()
    {
        WriteDotEnv(_dirs, "OPENAI_API_KEY=key-from-file", "OPENAI_ENDPOINT=http://from-file.invalid");

        var config = Manager(_dirs).Load();

        config.OpenAI.Endpoint.Should().Be("http://from-file.invalid");
        config.OpenAI.ApiKey.Should().Be("key-from-file");
    }

    // A host can blank a variable so that a project file cannot supply it.
    [Fact]
    public void A_variable_the_process_set_to_nothing_is_not_filled_from_the_file()
    {
        Environment.SetEnvironmentVariable("OPENAI_ENDPOINT", string.Empty);
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://from-file.invalid");

        var config = Manager(_dirs).Load();

        config.OpenAI.Endpoint.Should().NotBe("http://from-file.invalid");
        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT").Should().BeEmpty();
    }

    [Fact]
    public void A_reload_reads_what_the_env_file_says_now()
    {
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://first.invalid");
        var manager = Manager(_dirs);
        manager.Load();
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://second.invalid");

        manager.Load(forceReload: true).OpenAI.Endpoint.Should().Be("http://second.invalid");
    }

    [Fact]
    public void Another_projects_env_file_replaces_what_an_earlier_one_supplied()
    {
        using var other = new TempConfigDirs();
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://first-project.invalid");
        WriteDotEnv(other, "OPENAI_ENDPOINT=http://second-project.invalid");
        Manager(_dirs).Load();

        Manager(other).Load().OpenAI.Endpoint.Should().Be("http://second-project.invalid");
    }

    // A "${NAME}" in one project's file reads this file and the process, never what another project's file said.
    [Fact]
    public void A_reference_in_a_file_does_not_read_another_projects_file()
    {
        using var other = new TempConfigDirs();
        WriteDotEnv(_dirs, "OPENAI_API_KEY=key-of-the-first-project");
        WriteDotEnv(other, "OPENAI_API_KEY=own", "OPENAI_ENDPOINT=http://${OPENAI_API_KEY}.invalid");
        Manager(_dirs).Load();

        Manager(other).Load().OpenAI.Endpoint.Should().Be("http://own.invalid");
    }

    [Fact]
    public void What_a_file_supplied_is_gone_when_the_loaded_folder_has_no_env_file()
    {
        using var other = new TempConfigDirs();
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://first-project.invalid");
        Manager(_dirs).Load();

        Manager(other).Load().OpenAI.Endpoint.Should().NotBe("http://first-project.invalid");
        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT").Should().BeNull();
    }

    // Loads take back and supply process variables; a load running beside another must still read its own folder's.
    [Fact]
    public void Loads_running_beside_each_other_each_read_their_own_env_file()
    {
        using var other = new TempConfigDirs();
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://first-project.invalid");
        WriteDotEnv(other, "OPENAI_ENDPOINT=http://second-project.invalid");
        var wrong = 0;

        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var (dirs, expected) = i % 2 == 0
                ? (_dirs, "http://first-project.invalid")
                : (other, "http://second-project.invalid");
            if (Manager(dirs).Load().OpenAI.Endpoint != expected)
            {
                Interlocked.Increment(ref wrong);
            }
        });

        wrong.Should().Be(0);
    }

    // Two names say one setting: the one the process set wins, whichever of the two it is and whatever the file says
    // under the other.
    [Theory]
    [InlineData("GOOGLEAI_API_KEY=key-from-file")]
    [InlineData("GOOGLEAI_API_KEY=")]
    public void The_google_key_the_process_has_wins_over_the_other_name_in_the_env_file(string line)
    {
        Environment.SetEnvironmentVariable("GOOGLE_API_KEY", "key-from-host");
        WriteDotEnv(_dirs, line);

        Manager(_dirs).Load().GoogleAI.ApiKey.Should().Be("key-from-host");
    }

    // The positive control for the two names: with neither in the process, the file's name is read.
    [Fact]
    public void The_google_key_comes_from_the_env_file_when_the_process_has_neither_name()
    {
        WriteDotEnv(_dirs, "GOOGLE_API_KEY=key-from-file");

        Manager(_dirs).Load().GoogleAI.ApiKey.Should().Be("key-from-file");
    }

    // The environment keeps a value only up to a NUL. What a file supplied is taken back all the same.
    [Fact]
    public void A_value_with_a_nul_escape_is_taken_back_with_the_rest()
    {
        using var other = new TempConfigDirs();
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=\"http://first-project.invalid\\x00\"");
        Manager(_dirs).Load();

        Manager(other).Load();

        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT").Should().BeNull();
    }

    // Proxy names are read in either case, and the lower-case spelling first: a file must not add the other spelling
    // beside the one the host exported. Windows has one variable for both spellings.
    [Theory]
    [InlineData("HTTPS_PROXY", "https_proxy")]
    [InlineData("HTTP_PROXY", "http_proxy")]
    [InlineData("NO_PROXY", "no_proxy")]
    [InlineData("all_proxy", "ALL_PROXY")]
    public void A_proxy_variable_the_process_has_is_not_added_in_the_other_case(string exported, string inFile)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows variable names ignore case.");
        Environment.SetEnvironmentVariable(exported, "http://from-host.invalid");
        WriteDotEnv(_dirs, $"{inFile}=http://from-file.invalid");

        Manager(_dirs).Load();

        Environment.GetEnvironmentVariable(inFile).Should().BeNull();
        Environment.GetEnvironmentVariable(exported).Should().Be("http://from-host.invalid");
    }

    // Without a proxy of its own, the process takes the file's, in both spellings if the file writes both.
    [Fact]
    public void A_proxy_variable_the_process_lacks_comes_from_the_env_file()
    {
        WriteDotEnv(_dirs, "https_proxy=http://lower.invalid", "HTTPS_PROXY=http://lower.invalid");

        Manager(_dirs).Load();

        Environment.GetEnvironmentVariable("https_proxy").Should().Be("http://lower.invalid");
        Environment.GetEnvironmentVariable("HTTPS_PROXY").Should().Be("http://lower.invalid");
    }

    // Only what still has the value the file gave is taken back; a value set since then is the process's own.
    [Fact]
    public void A_value_the_process_set_after_a_load_is_not_taken_back()
    {
        WriteDotEnv(_dirs, "OPENAI_ENDPOINT=http://from-file.invalid");
        var manager = Manager(_dirs);
        manager.Load();
        Environment.SetEnvironmentVariable("OPENAI_ENDPOINT", "http://set-later.invalid");

        manager.Load(forceReload: true).OpenAI.Endpoint.Should().Be("http://set-later.invalid");
        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT").Should().Be("http://set-later.invalid");
    }

    // A later line of one file wins over an earlier one, and a reference reads the later line.
    [Fact]
    public void A_reference_after_a_repeated_name_reads_the_later_line()
    {
        WriteDotEnv(_dirs,
            "OPENAI_API_KEY=first",
            "OPENAI_API_KEY=second",
            "OPENAI_ENDPOINT=http://${OPENAI_API_KEY}.invalid");

        Manager(_dirs).Load().OpenAI.Endpoint.Should().Be("http://second.invalid");
    }

    // A reference reads what the file had said by then: the earlier line before a name is written again, the later one
    // after it.
    [Fact]
    public void A_reference_between_two_lines_of_a_name_reads_the_earlier_one()
    {
        WriteDotEnv(_dirs,
            "OPENAI_API_KEY=first",
            "OPENAI_ENDPOINT=http://${OPENAI_API_KEY}.invalid",
            "OPENAI_API_KEY=second",
            "OPENAI_MODEL=model-${OPENAI_API_KEY}");

        var config = Manager(_dirs).Load();

        config.OpenAI.Endpoint.Should().Be("http://first.invalid");
        config.OpenAI.Model.Should().Be("model-second");
    }

    // A later line of one file still wins over an earlier one, and may refer to it.
    [Fact]
    public void Within_one_file_the_last_line_wins_and_may_refer_to_an_earlier_one()
    {
        WriteDotEnv(_dirs,
            "OPENAI_API_KEY=key",
            "OPENAI_ENDPOINT=http://one.invalid",
            "OPENAI_ENDPOINT=http://${OPENAI_API_KEY}.invalid");

        Manager(_dirs).Load().OpenAI.Endpoint.Should().Be("http://key.invalid");
    }
}
