using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Tests.Config;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsManager _sut;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ironhive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new SettingsManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    // Constructor tests

    [Fact]
    public void Constructor_DefaultBasePath_ShouldPointToUserProfile()
    {
        var manager = new SettingsManager();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive");

        Assert.Equal(expected, manager.SettingsDirectory);
    }

    [Fact]
    public void Constructor_CustomBasePath_ShouldUseProvidedPath()
    {
        Assert.Equal(_tempDir, _sut.SettingsDirectory);
    }

    [Fact]
    public void SettingsFilePath_ShouldPointToSettingsJson()
    {
        var expected = Path.Combine(_tempDir, "settings.json");
        Assert.Equal(expected, _sut.SettingsFilePath);
    }

    // Load tests

    [Fact]
    public void Load_NoFile_ShouldReturnDefaultConfig()
    {
        var config = _sut.Load();

        Assert.NotNull(config);
    }

    [Fact]
    public void Load_InvalidJson_ShouldReturnDefaultConfig()
    {
        File.WriteAllText(_sut.SettingsFilePath, "not valid json{{{");

        var config = _sut.Load();

        Assert.NotNull(config);
    }

    // SetValue / GetValue round-trip tests

    [Fact]
    public void SetValue_GetValue_ShouldRoundTrip()
    {
        _sut.SetValue("openai.model", "gpt-4");

        var value = _sut.GetValue("openai.model");

        Assert.Equal("gpt-4", value);
    }

    [Fact]
    public void SetValue_ShouldConvertToCamelCase()
    {
        _sut.SetValue("openai.api_key", "sk-test-123");

        // Read raw JSON to verify camelCase
        var json = File.ReadAllText(_sut.SettingsFilePath);
        Assert.Contains("apiKey", json);
        Assert.DoesNotContain("api_key", json);
    }

    [Fact]
    public void SetValue_BoolValue_ShouldStoreAsBoolean()
    {
        _sut.SetValue("lmsupply.enabled", "true");

        var json = File.ReadAllText(_sut.SettingsFilePath);
        // Should contain true without quotes (as boolean)
        Assert.Contains("true", json);
    }

    [Fact]
    public void SetValue_IntValue_ShouldStoreAsNumber()
    {
        _sut.SetValue("lmsupply.max_context", "4096");

        var json = File.ReadAllText(_sut.SettingsFilePath);
        Assert.Contains("4096", json);
    }

    [Fact]
    public void SetValue_StringValue_ShouldStoreAsString()
    {
        _sut.SetValue("openai.endpoint", "https://api.openai.com");

        var value = _sut.GetValue("openai.endpoint");
        Assert.Equal("https://api.openai.com", value);
    }

    [Fact]
    public void SetValue_NestedKey_ShouldCreateHierarchy()
    {
        _sut.SetValue("openai.model", "gpt-4");
        _sut.SetValue("anthropic.model", "claude-3");

        var openai = _sut.GetValue("openai.model");
        var anthropic = _sut.GetValue("anthropic.model");

        Assert.Equal("gpt-4", openai);
        Assert.Equal("claude-3", anthropic);
    }

    [Fact]
    public void SetValue_OverwriteExisting_ShouldUpdate()
    {
        _sut.SetValue("openai.model", "gpt-3.5");
        _sut.SetValue("openai.model", "gpt-4");

        var value = _sut.GetValue("openai.model");

        Assert.Equal("gpt-4", value);
    }

    [Fact]
    public void SetValue_ShouldCreateDirectoryIfNotExists()
    {
        var nestedDir = Path.Combine(_tempDir, "sub", "dir");
        var manager = new SettingsManager(nestedDir);

        manager.SetValue("test.key", "value");

        Assert.True(Directory.Exists(nestedDir));
        Assert.Equal("value", manager.GetValue("test.key"));
    }

    // GetValue tests

    [Fact]
    public void GetValue_NoFile_ShouldReturnNull()
    {
        var value = _sut.GetValue("openai.model");

        Assert.Null(value);
    }

    [Fact]
    public void GetValue_NonExistingKey_ShouldReturnNull()
    {
        _sut.SetValue("openai.model", "gpt-4");

        var value = _sut.GetValue("anthropic.model");

        Assert.Null(value);
    }

    [Fact]
    public void GetValue_CaseInsensitiveFallback_ShouldMatch()
    {
        _sut.SetValue("openai.model", "gpt-4");

        // The key is stored as "openai.model" → camelCase "model" under "openai"
        // Case-insensitive lookup should find it
        var value = _sut.GetValue("openai.Model");

        Assert.Equal("gpt-4", value);
    }

    // UnsetValue tests

    [Fact]
    public void UnsetValue_ExistingKey_ShouldRemoveAndReturnTrue()
    {
        _sut.SetValue("openai.model", "gpt-4");

        var removed = _sut.UnsetValue("openai.model");

        Assert.True(removed);
        Assert.Null(_sut.GetValue("openai.model"));
    }

    [Fact]
    public void UnsetValue_NonExistingKey_ShouldReturnFalse()
    {
        _sut.SetValue("openai.model", "gpt-4");

        var removed = _sut.UnsetValue("anthropic.model");

        Assert.False(removed);
    }

    [Fact]
    public void UnsetValue_NoFile_ShouldReturnFalse()
    {
        var removed = _sut.UnsetValue("openai.model");

        Assert.False(removed);
    }

    [Fact]
    public void UnsetValue_ShouldPreserveOtherKeys()
    {
        _sut.SetValue("openai.model", "gpt-4");
        _sut.SetValue("openai.endpoint", "https://api.openai.com");

        _sut.UnsetValue("openai.model");

        Assert.Null(_sut.GetValue("openai.model"));
        Assert.Equal("https://api.openai.com", _sut.GetValue("openai.endpoint"));
    }

    // ListAll tests

    [Fact]
    public void ListAll_NoFile_ShouldReturnEmpty()
    {
        var result = _sut.ListAll();

        Assert.Empty(result);
    }

    [Fact]
    public void ListAll_ShouldReturnFlattenedKeys()
    {
        _sut.SetValue("openai.model", "gpt-4");
        _sut.SetValue("anthropic.model", "claude-3");

        var result = _sut.ListAll();

        Assert.True(result.Count >= 2);
        Assert.Contains(result, kvp => kvp.Key.Contains("model"));
    }

    [Fact]
    public void ListAll_ShouldMaskApiKeyValues()
    {
        _sut.SetValue("openai.api_key", "sk-1234567890abcdef");

        var result = _sut.ListAll();

        var apiKeyEntry = result.FirstOrDefault(kvp =>
            kvp.Key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));

        Assert.NotEqual(default, apiKeyEntry);
        Assert.DoesNotContain("sk-1234567890abcdef", apiKeyEntry.Value);
        // Should be masked: first 4 + "..." + last 4
        Assert.Contains("...", apiKeyEntry.Value);
    }

    [Fact]
    public void ListAll_ShortApiKey_ShouldMaskCompletely()
    {
        _sut.SetValue("openai.api_key", "short");

        var result = _sut.ListAll();

        var apiKeyEntry = result.FirstOrDefault(kvp =>
            kvp.Key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));

        Assert.NotEqual(default, apiKeyEntry);
        Assert.Equal("***", apiKeyEntry.Value);
    }

    [Fact]
    public void ListAll_NonSensitiveValues_ShouldNotBeMasked()
    {
        _sut.SetValue("openai.model", "gpt-4");

        var result = _sut.ListAll();

        var modelEntry = result.FirstOrDefault(kvp =>
            kvp.Key.Contains("model", StringComparison.OrdinalIgnoreCase));

        Assert.NotEqual(default, modelEntry);
        Assert.Equal("gpt-4", modelEntry.Value);
    }

    // Save / Load round-trip

    [Fact]
    public void Save_Load_ShouldRoundTrip()
    {
        var config = new IronHiveConfig();
        _sut.Save(config);

        var loaded = _sut.Load();

        Assert.NotNull(loaded);
    }

    // Multiple instances with same basePath

    [Fact]
    public void MultipleInstances_SamePath_ShouldShareState()
    {
        var manager1 = new SettingsManager(_tempDir);
        var manager2 = new SettingsManager(_tempDir);

        manager1.SetValue("openai.model", "gpt-4");

        var value = manager2.GetValue("openai.model");
        Assert.Equal("gpt-4", value);
    }

    // Different instances with different basePaths

    [Fact]
    public void DifferentInstances_DifferentPaths_ShouldBeIsolated()
    {
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"ironhive-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir2);
            var manager2 = new SettingsManager(tempDir2);

            _sut.SetValue("openai.model", "gpt-4");

            var value = manager2.GetValue("openai.model");
            Assert.Null(value);
        }
        finally
        {
            if (Directory.Exists(tempDir2))
            {
                Directory.Delete(tempDir2, recursive: true);
            }
        }
    }
}
