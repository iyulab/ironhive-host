using FluentAssertions;
using IronHive.Host.Config;

namespace IronHive.Host.Tests.Config;

internal sealed class TempConfigDirs : IDisposable
{
    public string ProjectRoot { get; }
    public string GlobalConfigPath { get; }
    private readonly string _root;
    public TempConfigDirs()
    {
        _root = Path.Combine(Path.GetTempPath(), "ihcfg-" + Guid.NewGuid().ToString("N"));
        ProjectRoot = Path.Combine(_root, "proj");
        Directory.CreateDirectory(Path.Combine(ProjectRoot, ".ironhive"));
        GlobalConfigPath = Path.Combine(_root, "global", "config.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(GlobalConfigPath)!);
    }
    public void WriteGlobal(string yaml) => File.WriteAllText(GlobalConfigPath, yaml);
    public void WriteProject(string yaml) => File.WriteAllText(Path.Combine(ProjectRoot, ".ironhive", "config.yaml"), yaml);
    public string LegacySettingsPath => Path.Combine(_root, "global", "settings.json");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

internal sealed class ScopedEnv : IDisposable
{
    private readonly (string Key, string? Prev)[] _prev;
    public ScopedEnv(params (string Key, string Value)[] vars)
    {
        _prev = vars.Select(v => (v.Key, Environment.GetEnvironmentVariable(v.Key))).ToArray();
        foreach (var (k, val) in vars)
        {
            Environment.SetEnvironmentVariable(k, val);
        }
    }
    public void Dispose()
    {
        foreach (var (k, prev) in _prev)
        {
            Environment.SetEnvironmentVariable(k, prev);
        }
    }
}

public class ConfigurationManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigurationManager _manager;

    public ConfigurationManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ironhive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _manager = new ConfigurationManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_ProjectConfigOverridesGlobal_UnsetFieldsFallThrough()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("gpuStack:\n  endpoint: http://global:8080\n  model: global-model\n  apiKey: gk\n");
        tmp.WriteProject("gpuStack:\n  model: project-model\n"); // only model set

        var manager = new ConfigurationManager(projectRoot: tmp.ProjectRoot, globalConfigPath: tmp.GlobalConfigPath);
        var config = manager.Load();

        config.GpuStack.Model.Should().Be("project-model");        // project wins
        config.GpuStack.Endpoint.Should().Be("http://global:8080"); // falls through from global
        config.GpuStack.ApiKey.Should().Be("gk");
    }

    [Fact]
    public void Load_NoConfigFiles_ReturnsDefaults()
    {
        var config = _manager.Load();

        Assert.NotNull(config);
        Assert.NotNull(config.GpuStack);
        Assert.NotNull(config.OpenAI);
        Assert.NotNull(config.Anthropic);
        Assert.NotNull(config.GoogleAI);
        Assert.NotNull(config.Xai);
        Assert.NotNull(config.AzureOpenAI);
        Assert.NotNull(config.LMSupply);
        Assert.NotNull(config.Ollama);
        Assert.NotNull(config.LMStudio);
        Assert.NotNull(config.Permissions);
        Assert.NotNull(config.Compaction);
        Assert.NotNull(config.WebSearch);
        Assert.NotNull(config.DeepResearch);
        Assert.NotNull(config.ChatBehavior);
    }

    [Fact]
    public void Load_WithProjectConfig_LoadsValues()
    {
        var projectConfigDir = Path.Combine(_tempDir, ".ironhive");
        Directory.CreateDirectory(projectConfigDir);
        File.WriteAllText(
            Path.Combine(projectConfigDir, "config.yaml"),
            """
            chatBehavior:
              maximumIterationsPerRequest: 5
              maximumConsecutiveErrorsPerRequest: 7
            compaction:
              thresholdPercentage: 0.85
            """);

        var config = _manager.Load(forceReload: true);

        Assert.Equal(5, config.ChatBehavior.MaximumIterationsPerRequest);
        Assert.Equal(7, config.ChatBehavior.MaximumConsecutiveErrorsPerRequest);
        Assert.Equal(0.85f, config.Compaction.ThresholdPercentage);
    }

    [Fact]
    public void Load_GpuStackFromEnv_LoadsCorrectly()
    {
        Environment.SetEnvironmentVariable("GPUSTACK_ENDPOINT", "http://test.local:8080");
        Environment.SetEnvironmentVariable("GPUSTACK_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("GPUSTACK_MODEL", "test-model");

        try
        {
            var config = _manager.Load(forceReload: true);

            Assert.Equal("http://test.local:8080", config.GpuStack.Endpoint);
            Assert.Equal("test-key", config.GpuStack.ApiKey);
            Assert.Equal("test-model", config.GpuStack.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GPUSTACK_ENDPOINT", null);
            Environment.SetEnvironmentVariable("GPUSTACK_API_KEY", null);
            Environment.SetEnvironmentVariable("GPUSTACK_MODEL", null);
        }
    }

    [Fact]
    public void Load_EnvironmentVariablesOverrideFileConfig_FullSurface()
    {
        using var tmp = new TempConfigDirs();
        // NOTE: OpenAI's clean lowercase alias is "openai" (Task 2.5 explicit
        // [YamlMember(Alias)]); the naive CamelCaseNamingConvention key "openAI"
        // (only the leading char lowercased) is intentionally NOT accepted anymore.
        tmp.WriteGlobal("openai:\n  apiKey: file-key\n  model: file-model\n");
        using var env = new ScopedEnv(("OPENAI_API_KEY", "env-key"), ("ANTHROPIC_MODEL", "claude-x"));

        var manager = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        var config = manager.Load();

        config.OpenAI.ApiKey.Should().Be("env-key");     // env overrides file
        config.OpenAI.Model.Should().Be("file-model");   // file value retained where no env
        config.Anthropic.Model.Should().Be("claude-x");  // env-only field
    }

    [Fact]
    public void GlobalConfigPath_ReturnsCorrectPath()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ironhive",
            "config.yaml");

        Assert.Equal(expected, _manager.GlobalConfigPath);
    }

    [Fact]
    public void ProjectConfigPath_ReturnsCorrectPath()
    {
        var expected = Path.Combine(_tempDir, ".ironhive", "config.yaml");

        Assert.Equal(expected, _manager.ProjectConfigPath);
    }

    [Fact]
    public void UsageLimitsConfig_DefaultValues_AreCorrect()
    {
        var limits = new IronHive.Agent.Tracking.UsageLimitsConfig();

        Assert.Equal(0, limits.MaxSessionTokens); // Unlimited
        Assert.Equal(0m, limits.MaxSessionCost); // Unlimited
        Assert.Equal(0.8f, limits.WarningThreshold);
        Assert.True(limits.StopOnLimit);
    }

    [Fact]
    public void Load_Cached_DoesNotReloadWithoutForce()
    {
        // First load
        var config1 = _manager.Load();

        // Create a config file after first load
        var projectConfigDir = Path.Combine(_tempDir, ".ironhive");
        Directory.CreateDirectory(projectConfigDir);
        File.WriteAllText(
            Path.Combine(projectConfigDir, "config.yaml"),
            "chatBehavior:\n  maximumIterationsPerRequest: 99");

        // Second load without force - should return cached
        var config2 = _manager.Load();

        Assert.Same(config1, config2);
        Assert.Equal(10, config2.ChatBehavior.MaximumIterationsPerRequest); // Original default

        // Force reload
        var config3 = _manager.Load(forceReload: true);

        Assert.NotSame(config1, config3);
        Assert.Equal(99, config3.ChatBehavior.MaximumIterationsPerRequest);
    }

    [Fact]
    public void Load_AcronymSectionKeys_UseCleanLowercaseAliases()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("openai:\n  apiKey: k1\nlmsupply:\n  generatorModel: gm\nlmstudio:\n  model: ls\ngoogleai:\n  apiKey: g\nazureopenai:\n  deploymentName: d\n");
        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        config.OpenAI.ApiKey.Should().Be("k1");
        config.LMSupply.GeneratorModel.Should().Be("gm");
        config.LMStudio.Model.Should().Be("ls");
        config.GoogleAI.ApiKey.Should().Be("g");
        config.AzureOpenAI.DeploymentName.Should().Be("d");
    }

    [Fact]
    public void FindUnknownTopLevelKeys_ReportsMisspelledSection()
    {
        var yaml = "openai:\n  apiKey: k\nopenAI:\n  apiKey: dup\nbogusSection:\n  x: 1\n";
        var unknown = ConfigurationManager.FindUnknownTopLevelKeys(yaml);
        unknown.Should().Contain("openAI");     // wrong-case duplicate is unknown
        unknown.Should().Contain("bogusSection");
        unknown.Should().NotContain("openai");  // valid alias
    }

    [Fact]
    public void Load_NoApiProvider_AutoEnablesLmSupply_OverridingExplicitFalse()
    {
        using var tmp = new TempConfigDirs();
        // no provider configured; user explicitly disabled lmsupply
        tmp.WriteGlobal("lmsupply:\n  enabled: false\n");
        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        config.LMSupply.Enabled.Should().BeTrue(); // auto-enable fires because no API provider is set
    }

    [Fact]
    public void Load_ApiProviderConfigured_DoesNotForceLmSupply()
    {
        using var tmp = new TempConfigDirs();
        tmp.WriteGlobal("openai:\n  apiKey: k\n  model: m\nlmsupply:\n  enabled: false\n");
        var config = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        config.LMSupply.Enabled.Should().BeFalse(); // a real provider is configured -> do NOT auto-enable
    }

    [Fact]
    public void SaveGlobal_ThenLoad_RoundTripsAcrossSections()
    {
        using var tmp = new TempConfigDirs();
        var manager = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        var src = new IronHiveConfig();
        src.OpenAI.ApiKey = "k";
        src.OpenAI.Model = "m";
        src.Compaction.ProtectRecentTokens = 12345;   // real CompactionConfig field (default 40000)
        src.LMSupply.GeneratorModel = "gm";

        manager.SaveGlobal(src);

        var loaded = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        loaded.OpenAI.ApiKey.Should().Be("k");
        loaded.OpenAI.Model.Should().Be("m");
        loaded.Compaction.ProtectRecentTokens.Should().Be(12345);
        loaded.LMSupply.GeneratorModel.Should().Be("gm");
    }

    [Fact]
    public void SetValue_ThenTypedLoad_SeesTheChange_NoSilentIgnore()
    {
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("openai.apiKey", "sk-123");
        mgr.GetValue("openai.apiKey").Should().Be("sk-123");                 // raw get sees it
        var loaded = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        loaded.OpenAI.ApiKey.Should().Be("sk-123");                          // typed Load ALSO sees it
    }

    [Fact]
    public void UnsetValue_RemovesKey()
    {
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("openai.model", "gpt-x");
        mgr.UnsetValue("openai.model").Should().BeTrue();
        mgr.GetValue("openai.model").Should().BeNull();
    }

    [Fact]
    public void ListAll_FlattensDottedKeys()
    {
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("openai.apiKey", "k");
        mgr.SetValue("anthropic.model", "claude");
        var all = mgr.ListAll();
        all.Should().ContainKey("openai.apiKey");
        all["anthropic.model"].Should().Be("claude");
    }

    [Fact]
    public void GetValue_MissingFile_ReturnsNull()
    {
        using var tmp = new TempConfigDirs();
        new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).GetValue("openai.apiKey").Should().BeNull();
    }

    [Fact]
    public void GetValue_NonStringLeaf_ReturnsStringNotThrow_AndTypedLoadReadsIt()
    {
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("deepResearch.maxIterations", "7");
        mgr.GetValue("deepResearch.maxIterations").Should().Be("7"); // no throw, no quotes
        var loaded = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath).Load();
        loaded.DeepResearch.MaxIterations.Should().Be(7);            // int round-trips through the bridge to typed Load
    }

    [Fact]
    public void GetValue_StringLeaf_HasNoSurroundingQuotes()
    {
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("openai.apiKey", "sk-xyz");
        mgr.GetValue("openai.apiKey").Should().Be("sk-xyz");         // exactly, no quotes
    }

    [Fact]
    public void FullConfig_YamlRoundTrip_PreservesEverySection()
    {
        var src = new IronHiveConfig();
        src.GpuStack.Endpoint = "e"; src.OpenAI.ApiKey = "o"; src.Anthropic.Model = "a";
        src.GoogleAI.ApiKey = "g"; src.Xai.ApiKey = "x"; src.AzureOpenAI.Endpoint = "az";
        src.LMSupply.GeneratorModel = "gm"; src.Ollama.Model = "ol"; src.LMStudio.Model = "ls";
        src.WebSearch.TavilyApiKey = "t"; src.DeepResearch.MaxIterations = 9;
        src.ChatBehavior.MaximumIterationsPerRequest = 7;
        var yaml = YamlConfigSerializer.Serialize(src);
        var back = YamlConfigSerializer.Deserialize<IronHiveConfig>(yaml)!;
        back.GpuStack.Endpoint.Should().Be("e"); back.OpenAI.ApiKey.Should().Be("o");
        back.Anthropic.Model.Should().Be("a"); back.GoogleAI.ApiKey.Should().Be("g");
        back.Xai.ApiKey.Should().Be("x"); back.AzureOpenAI.Endpoint.Should().Be("az");
        back.LMSupply.GeneratorModel.Should().Be("gm"); back.Ollama.Model.Should().Be("ol");
        back.LMStudio.Model.Should().Be("ls"); back.WebSearch.TavilyApiKey.Should().Be("t");
        back.DeepResearch.MaxIterations.Should().Be(9);
        back.ChatBehavior.MaximumIterationsPerRequest.Should().Be(7);
    }

    [Fact]
    public void ListAll_MasksApiKeyValues()
    {
        // Ported from SettingsManagerTests (unique edge case: FlattenJsonObject's
        // masking of *apiKey/*api_key leaves before SettingsManager is deleted).
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("openai.apiKey", "sk-1234567890abcdef");

        var result = mgr.ListAll();

        var apiKeyEntry = result.First(kvp => kvp.Key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
        apiKeyEntry.Value.Should().NotContain("sk-1234567890abcdef");
        apiKeyEntry.Value.Should().Contain("...");
    }

    [Fact]
    public void ListAll_ShortApiKey_MasksCompletely()
    {
        // Ported from SettingsManagerTests (unique edge case: short-secret masking falls
        // back to a flat "***" instead of the head/tail-preserving mask).
        using var tmp = new TempConfigDirs();
        var mgr = new ConfigurationManager(tmp.ProjectRoot, tmp.GlobalConfigPath);
        mgr.SetValue("openai.apiKey", "short");

        var result = mgr.ListAll();

        var apiKeyEntry = result.First(kvp => kvp.Key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
        apiKeyEntry.Value.Should().Be("***");
    }
}
