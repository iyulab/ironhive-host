using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Tests.Config;

public class SettingsManagerTests : IDisposable
{
    private readonly string? _backupContent;
    private readonly bool _fileExisted;

    public SettingsManagerTests()
    {
        // Back up existing settings file if any
        _fileExisted = File.Exists(SettingsManager.SettingsFilePath);
        if (_fileExisted)
        {
            _backupContent = File.ReadAllText(SettingsManager.SettingsFilePath);
        }

        // Start with a clean slate
        if (File.Exists(SettingsManager.SettingsFilePath))
        {
            File.Delete(SettingsManager.SettingsFilePath);
        }
    }

    public void Dispose()
    {
        // Restore original settings
        if (_fileExisted && _backupContent is not null)
        {
            var dir = Path.GetDirectoryName(SettingsManager.SettingsFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(SettingsManager.SettingsFilePath, _backupContent);
        }
        else if (File.Exists(SettingsManager.SettingsFilePath))
        {
            File.Delete(SettingsManager.SettingsFilePath);
        }
        GC.SuppressFinalize(this);
    }

    // Path tests

    [Fact]
    public void SettingsDirectory_ShouldPointToUserProfile()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive");

        Assert.Equal(expected, SettingsManager.SettingsDirectory);
    }

    [Fact]
    public void SettingsFilePath_ShouldPointToSettingsJson()
    {
        Assert.EndsWith("settings.json", SettingsManager.SettingsFilePath);
        Assert.Contains(".ironhive", SettingsManager.SettingsFilePath);
    }

    // Load tests

    [Fact]
    public void Load_NoFile_ShouldReturnDefaultConfig()
    {
        var config = SettingsManager.Load();

        Assert.NotNull(config);
    }

    [Fact]
    public void Load_InvalidJson_ShouldReturnDefaultConfig()
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(SettingsManager.SettingsFilePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(SettingsManager.SettingsFilePath, "not valid json{{{");

        var config = SettingsManager.Load();

        Assert.NotNull(config);
    }

    // SetValue / GetValue round-trip tests

    [Fact]
    public void SetValue_GetValue_ShouldRoundTrip()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");

        var value = SettingsManager.GetValue("openai.model");

        Assert.Equal("gpt-4", value);
    }

    [Fact]
    public void SetValue_ShouldConvertToCamelCase()
    {
        SettingsManager.SetValue("openai.api_key", "sk-test-123");

        // Read raw JSON to verify camelCase
        var json = File.ReadAllText(SettingsManager.SettingsFilePath);
        Assert.Contains("apiKey", json);
        Assert.DoesNotContain("api_key", json);
    }

    [Fact]
    public void SetValue_BoolValue_ShouldStoreAsBoolean()
    {
        SettingsManager.SetValue("lmsupply.enabled", "true");

        var json = File.ReadAllText(SettingsManager.SettingsFilePath);
        // Should contain true without quotes (as boolean)
        Assert.Contains("true", json);
    }

    [Fact]
    public void SetValue_IntValue_ShouldStoreAsNumber()
    {
        SettingsManager.SetValue("lmsupply.max_context", "4096");

        var json = File.ReadAllText(SettingsManager.SettingsFilePath);
        Assert.Contains("4096", json);
    }

    [Fact]
    public void SetValue_StringValue_ShouldStoreAsString()
    {
        SettingsManager.SetValue("openai.endpoint", "https://api.openai.com");

        var value = SettingsManager.GetValue("openai.endpoint");
        Assert.Equal("https://api.openai.com", value);
    }

    [Fact]
    public void SetValue_NestedKey_ShouldCreateHierarchy()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");
        SettingsManager.SetValue("anthropic.model", "claude-3");

        var openai = SettingsManager.GetValue("openai.model");
        var anthropic = SettingsManager.GetValue("anthropic.model");

        Assert.Equal("gpt-4", openai);
        Assert.Equal("claude-3", anthropic);
    }

    [Fact]
    public void SetValue_OverwriteExisting_ShouldUpdate()
    {
        SettingsManager.SetValue("openai.model", "gpt-3.5");
        SettingsManager.SetValue("openai.model", "gpt-4");

        var value = SettingsManager.GetValue("openai.model");

        Assert.Equal("gpt-4", value);
    }

    // GetValue tests

    [Fact]
    public void GetValue_NoFile_ShouldReturnNull()
    {
        var value = SettingsManager.GetValue("openai.model");

        Assert.Null(value);
    }

    [Fact]
    public void GetValue_NonExistingKey_ShouldReturnNull()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");

        var value = SettingsManager.GetValue("anthropic.model");

        Assert.Null(value);
    }

    [Fact]
    public void GetValue_CaseInsensitiveFallback_ShouldMatch()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");

        // The key is stored as "openai.model" → camelCase "model" under "openai"
        // Case-insensitive lookup should find it
        var value = SettingsManager.GetValue("openai.Model");

        Assert.Equal("gpt-4", value);
    }

    // UnsetValue tests

    [Fact]
    public void UnsetValue_ExistingKey_ShouldRemoveAndReturnTrue()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");

        var removed = SettingsManager.UnsetValue("openai.model");

        Assert.True(removed);
        Assert.Null(SettingsManager.GetValue("openai.model"));
    }

    [Fact]
    public void UnsetValue_NonExistingKey_ShouldReturnFalse()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");

        var removed = SettingsManager.UnsetValue("anthropic.model");

        Assert.False(removed);
    }

    [Fact]
    public void UnsetValue_NoFile_ShouldReturnFalse()
    {
        var removed = SettingsManager.UnsetValue("openai.model");

        Assert.False(removed);
    }

    [Fact]
    public void UnsetValue_ShouldPreserveOtherKeys()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");
        SettingsManager.SetValue("openai.endpoint", "https://api.openai.com");

        SettingsManager.UnsetValue("openai.model");

        Assert.Null(SettingsManager.GetValue("openai.model"));
        Assert.Equal("https://api.openai.com", SettingsManager.GetValue("openai.endpoint"));
    }

    // ListAll tests

    [Fact]
    public void ListAll_NoFile_ShouldReturnEmpty()
    {
        var result = SettingsManager.ListAll();

        Assert.Empty(result);
    }

    [Fact]
    public void ListAll_ShouldReturnFlattenedKeys()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");
        SettingsManager.SetValue("anthropic.model", "claude-3");

        var result = SettingsManager.ListAll();

        Assert.True(result.Count >= 2);
        Assert.Contains(result, kvp => kvp.Key.Contains("model"));
    }

    [Fact]
    public void ListAll_ShouldMaskApiKeyValues()
    {
        SettingsManager.SetValue("openai.api_key", "sk-1234567890abcdef");

        var result = SettingsManager.ListAll();

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
        SettingsManager.SetValue("openai.api_key", "short");

        var result = SettingsManager.ListAll();

        var apiKeyEntry = result.FirstOrDefault(kvp =>
            kvp.Key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));

        Assert.NotEqual(default, apiKeyEntry);
        Assert.Equal("***", apiKeyEntry.Value);
    }

    [Fact]
    public void ListAll_NonSensitiveValues_ShouldNotBeMasked()
    {
        SettingsManager.SetValue("openai.model", "gpt-4");

        var result = SettingsManager.ListAll();

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
        SettingsManager.Save(config);

        var loaded = SettingsManager.Load();

        Assert.NotNull(loaded);
    }
}
