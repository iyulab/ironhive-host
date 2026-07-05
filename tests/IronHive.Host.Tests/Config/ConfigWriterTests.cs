using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

public class ConfigWriterTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"configwriter-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private string EnvFile(string name = ".env") => Path.Combine(_tempDir, name);

    // SetValue tests

    [Fact]
    public void SetValue_NewFile_ShouldCreateFileWithKeyValue()
    {
        var path = EnvFile();

        var result = ConfigWriter.SetValue(path, "openai.apikey", "sk-test-123");

        Assert.True(result);
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("OPENAI_API_KEY=sk-test-123", content);
    }

    [Fact]
    public void SetValue_ExistingKey_ShouldUpdateValue()
    {
        var path = EnvFile();
        File.WriteAllText(path, "OPENAI_API_KEY=old-value\n");

        ConfigWriter.SetValue(path, "openai.apikey", "new-value");

        var lines = File.ReadAllLines(path);
        Assert.Single(lines, l => l.Contains("OPENAI_API_KEY"));
        Assert.Contains("OPENAI_API_KEY=new-value", lines);
    }

    [Fact]
    public void SetValue_ShouldPreserveComments()
    {
        var path = EnvFile();
        File.WriteAllText(path, "# This is a comment\nOPENAI_API_KEY=old\n# Another comment\n");

        ConfigWriter.SetValue(path, "openai.apikey", "updated");

        var content = File.ReadAllText(path);
        Assert.Contains("# This is a comment", content);
        Assert.Contains("# Another comment", content);
        Assert.Contains("OPENAI_API_KEY=updated", content);
    }

    [Fact]
    public void SetValue_NewKey_ShouldAppend()
    {
        var path = EnvFile();
        File.WriteAllText(path, "OPENAI_API_KEY=key1\n");

        ConfigWriter.SetValue(path, "anthropic.apikey", "key2");

        var content = File.ReadAllText(path);
        Assert.Contains("OPENAI_API_KEY=key1", content);
        Assert.Contains("ANTHROPIC_API_KEY=key2", content);
    }

    [Fact]
    public void SetValue_DotNotationKey_ShouldMapToEnvVar()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "gpustack.endpoint", "http://localhost:8080");

        var content = File.ReadAllText(path);
        Assert.Contains("GPUSTACK_ENDPOINT=http://localhost:8080", content);
    }

    [Fact]
    public void SetValue_AlreadyEnvVarKey_ShouldUseAsIs()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "MY_CUSTOM_VAR", "value");

        var content = File.ReadAllText(path);
        Assert.Contains("MY_CUSTOM_VAR=value", content);
    }

    [Fact]
    public void SetValue_ValueWithSpaces_ShouldQuote()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "SOME_VAR", "hello world");

        var content = File.ReadAllText(path);
        Assert.Contains("SOME_VAR=\"hello world\"", content);
    }

    [Fact]
    public void SetValue_ValueWithHash_ShouldQuote()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "SOME_VAR", "value#comment");

        var content = File.ReadAllText(path);
        Assert.Contains("SOME_VAR=\"value#comment\"", content);
    }

    [Fact]
    public void SetValue_ValueWithQuotes_ShouldEscapeAndQuote()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "SOME_VAR", "he said \"hi\"");

        var content = File.ReadAllText(path);
        Assert.Contains("SOME_VAR=\"he said \\\"hi\\\"\"", content);
    }

    [Fact]
    public void SetValue_EmptyValue_ShouldQuote()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "SOME_VAR", "");

        var content = File.ReadAllText(path);
        Assert.Contains("SOME_VAR=\"\"", content);
    }

    [Fact]
    public void SetValue_UnknownDotKey_ShouldFallbackToUpperUnderscore()
    {
        var path = EnvFile();

        ConfigWriter.SetValue(path, "custom.my_setting", "val");

        var content = File.ReadAllText(path);
        Assert.Contains("CUSTOM_MY_SETTING=val", content);
    }

    [Fact]
    public void SetValue_SubDirectory_ShouldCreateDirectories()
    {
        var path = Path.Combine(_tempDir, "sub", "dir", ".env");

        ConfigWriter.SetValue(path, "SOME_VAR", "value");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SetValue_NullPath_ShouldThrow()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConfigWriter.SetValue(null!, "key", "value"));
    }

    [Fact]
    public void SetValue_NullKey_ShouldThrow()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConfigWriter.SetValue(EnvFile(), null!, "value"));
    }

    [Fact]
    public void SetValue_WhitespaceKey_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ConfigWriter.SetValue(EnvFile(), "  ", "value"));
    }

    [Fact]
    public void SetValue_CaseInsensitiveKeyMatch_ShouldUpdate()
    {
        var path = EnvFile();
        File.WriteAllText(path, "openai_api_key=old\n");

        ConfigWriter.SetValue(path, "OPENAI_API_KEY", "new");

        var lines = File.ReadAllLines(path);
        var matchingLines = lines.Where(l =>
            l.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("openai_api_key", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(matchingLines);
    }

    [Theory]
    [InlineData("openai.api_key", "OPENAI_API_KEY")]
    [InlineData("anthropic.api_key", "ANTHROPIC_API_KEY")]
    [InlineData("google.apikey", "GOOGLE_API_KEY")]
    [InlineData("azure.endpoint", "AZURE_OPENAI_ENDPOINT")]
    [InlineData("gpustack.embedding_model", "GPUSTACK_EMBEDDING_MODEL")]
    [InlineData("lmsupply.max_context_length", "LMSUPPLY_MAX_CONTEXT")]
    [InlineData("xai.model", "XAI_MODEL")]
    public void SetValue_KnownDotNotation_ShouldMapCorrectly(string dotKey, string expectedEnvKey)
    {
        var path = EnvFile($".env-{dotKey.Replace('.', '-')}");

        ConfigWriter.SetValue(path, dotKey, "test-value");

        var content = File.ReadAllText(path);
        Assert.Contains($"{expectedEnvKey}=test-value", content);
    }

    // RemoveValue tests

    [Fact]
    public void RemoveValue_ExistingKey_ShouldRemoveAndReturnTrue()
    {
        var path = EnvFile();
        File.WriteAllText(path, "KEY_A=val1\nKEY_B=val2\nKEY_C=val3\n");

        var result = ConfigWriter.RemoveValue(path, "KEY_B");

        Assert.True(result);
        var content = File.ReadAllText(path);
        Assert.DoesNotContain("KEY_B", content);
        Assert.Contains("KEY_A=val1", content);
        Assert.Contains("KEY_C=val3", content);
    }

    [Fact]
    public void RemoveValue_NonExistingKey_ShouldReturnFalse()
    {
        var path = EnvFile();
        File.WriteAllText(path, "KEY_A=val1\n");

        var result = ConfigWriter.RemoveValue(path, "KEY_B");

        Assert.False(result);
    }

    [Fact]
    public void RemoveValue_MissingFile_ShouldReturnFalse()
    {
        var path = EnvFile("nonexistent.env");

        var result = ConfigWriter.RemoveValue(path, "KEY_A");

        Assert.False(result);
    }

    [Fact]
    public void RemoveValue_DotNotation_ShouldConvertAndRemove()
    {
        var path = EnvFile();
        File.WriteAllText(path, "OPENAI_API_KEY=sk-test\n");

        var result = ConfigWriter.RemoveValue(path, "openai.apikey");

        Assert.True(result);
        var content = File.ReadAllText(path);
        Assert.DoesNotContain("OPENAI_API_KEY", content);
    }

    [Fact]
    public void RemoveValue_PreservesComments()
    {
        var path = EnvFile();
        File.WriteAllText(path, "# comment\nKEY_A=val\n# end\n");

        ConfigWriter.RemoveValue(path, "KEY_A");

        var content = File.ReadAllText(path);
        Assert.Contains("# comment", content);
        Assert.Contains("# end", content);
    }

    [Fact]
    public void RemoveValue_NullPath_ShouldThrow()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConfigWriter.RemoveValue(null!, "key"));
    }

    [Fact]
    public void RemoveValue_NullKey_ShouldThrow()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConfigWriter.RemoveValue(EnvFile(), null!));
    }

    // GetValidKeys tests

    [Fact]
    public void GetValidKeys_ShouldReturnGroupedSections()
    {
        var keys = ConfigWriter.GetValidKeys();

        Assert.True(keys.Count > 0);
        Assert.Contains("gpustack", keys.Keys);
        Assert.Contains("openai", keys.Keys);
        Assert.Contains("anthropic", keys.Keys);
        Assert.Contains("google", keys.Keys);
        Assert.Contains("azure", keys.Keys);
        Assert.Contains("xai", keys.Keys);
        Assert.Contains("lmsupply", keys.Keys);
    }

    [Fact]
    public void GetValidKeys_EachSectionShouldHaveKeys()
    {
        var keys = ConfigWriter.GetValidKeys();

        foreach (var section in keys)
        {
            Assert.True(section.Value.Count > 0,
                $"Section '{section.Key}' should have at least one key");
        }
    }

    [Fact]
    public void GetValidKeys_AllKeysShouldContainDot()
    {
        var keys = ConfigWriter.GetValidKeys();

        foreach (var section in keys)
        {
            foreach (var key in section.Value)
            {
                Assert.Contains(".", key);
            }
        }
    }

    [Fact]
    public void GetValidKeys_ShouldNotContainDuplicatesPerSection()
    {
        var keys = ConfigWriter.GetValidKeys();

        foreach (var section in keys)
        {
            var distinctCount = section.Value.Distinct().Count();
            Assert.Equal(section.Value.Count, distinctCount);
        }
    }
}
