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
    private static readonly string[] Variables = ["OPENAI_API_KEY", "OPENAI_ENDPOINT"];

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
