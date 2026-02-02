using IronHive.Cli.Core.Config;

namespace IronHive.Cli.Tests.Config;

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
    public void Load_NoConfigFiles_ReturnsDefaults()
    {
        var config = _manager.Load();

        Assert.NotNull(config);
        Assert.NotNull(config.GpuStack);
        Assert.NotNull(config.LMSupply);
        Assert.NotNull(config.Permissions);
        Assert.NotNull(config.Webhook);
        Assert.NotNull(config.Limits);
        Assert.NotNull(config.Context);
        Assert.NotNull(config.Session);
    }

    [Fact]
    public void Load_WithProjectConfig_LoadsValues()
    {
        var projectConfigDir = Path.Combine(_tempDir, ".ironhive");
        Directory.CreateDirectory(projectConfigDir);
        File.WriteAllText(
            Path.Combine(projectConfigDir, "config.yaml"),
            """
            limits:
              maxSessionTokens: 50000
              maxSessionCost: 5.00
            context:
              compactionThreshold: 0.85
            """);

        var config = _manager.Load(forceReload: true);

        Assert.Equal(50000, config.Limits.MaxSessionTokens);
        Assert.Equal(5.00m, config.Limits.MaxSessionCost);
        Assert.Equal(0.85f, config.Context.CompactionThreshold);
    }

    [Fact]
    public void LoadClaudeMd_NoProjectFile_ReturnsGlobalOrNull()
    {
        // When no CLAUDE.md in project, may return global CLAUDE.md if exists
        var claudeMd = _manager.LoadClaudeMd();

        // Either null (no global) or global path
        if (claudeMd != null)
        {
            Assert.DoesNotContain(_tempDir, claudeMd.SourcePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LoadClaudeMd_WithFile_ReturnsContent()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "CLAUDE.md"),
            "# Project Instructions\nBe helpful.");

        var claudeMd = _manager.LoadClaudeMd();

        Assert.NotNull(claudeMd);
        Assert.Contains("# Project Instructions", claudeMd.Content);
        Assert.Contains("Be helpful", claudeMd.Content);
        Assert.Equal(Path.Combine(_tempDir, "CLAUDE.md"), claudeMd.SourcePath);
    }

    [Fact]
    public void GetMergedClaudeMdContent_NoProjectFile_ReturnsGlobalOrNull()
    {
        // When no CLAUDE.md in project, may return global content if exists
        var content = _manager.GetMergedClaudeMdContent();

        // Either null (no global) or some content from global
        // Test passes either way - just verify no exception
        Assert.True(content == null || !string.IsNullOrEmpty(content));
    }

    [Fact]
    public void GetMergedClaudeMdContent_WithFile_ReturnsContent()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "CLAUDE.md"),
            "# Instructions\nDo good work.");

        var content = _manager.GetMergedClaudeMdContent();

        Assert.NotNull(content);
        Assert.Contains("Do good work", content);
    }

    [Fact]
    public void Load_EnvironmentVariables_TakePriority()
    {
        // Set environment variable
        Environment.SetEnvironmentVariable("IRONHIVE_MAX_SESSION_TOKENS", "100000");

        try
        {
            var config = _manager.Load(forceReload: true);

            Assert.Equal(100000, config.Limits.MaxSessionTokens);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONHIVE_MAX_SESSION_TOKENS", null);
        }
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
    public void LimitsConfig_DefaultValues_AreCorrect()
    {
        var limits = new LimitsConfig();

        Assert.Equal(0, limits.MaxSessionTokens); // Unlimited
        Assert.Equal(0m, limits.MaxSessionCost); // Unlimited
        Assert.Equal(0.8f, limits.WarningThreshold);
        Assert.True(limits.StopOnLimit);
    }

    [Fact]
    public void ContextConfig_DefaultValues_AreCorrect()
    {
        var context = new ContextConfig();

        Assert.Equal(0.92f, context.CompactionThreshold);
        Assert.Equal(10, context.TailPreserveCount);
        Assert.True(context.GoalReminderEnabled);
        Assert.Equal(10, context.GoalReminderInterval);
        Assert.True(context.PromptCachingEnabled);
        Assert.Equal(1024, context.MinSystemPromptTokensForCaching);
    }

    [Fact]
    public void SessionConfig_DefaultValues_AreCorrect()
    {
        var session = new SessionConfig();

        Assert.Equal(".ironhive/sessions", session.TranscriptDirectory);
        Assert.Equal(100, session.MaxSessions);
        Assert.True(session.AutoSave);
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
            "limits:\n  maxSessionTokens: 99999");

        // Second load without force - should return cached
        var config2 = _manager.Load();

        Assert.Same(config1, config2);
        Assert.Equal(0, config2.Limits.MaxSessionTokens); // Original default

        // Force reload
        var config3 = _manager.Load(forceReload: true);

        Assert.NotSame(config1, config3);
        Assert.Equal(99999, config3.Limits.MaxSessionTokens);
    }
}
