using System.Globalization;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Abstractions;
using IronHive.Abstractions.Messages;
using IronHive.Agent.Context;
using IronHive.Agent.ErrorRecovery;
using IronHive.Agent.Extensions;
using IronHive.Agent.Invocation;
using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Agent.Mode;
using IronHive.Agent.Providers;
using IronHive.Agent.Tracking;
using IronHive.Cli.Infrastructure.Delegation;
using IronHive.DeepResearch.Models.Research;
using IronHive.Host.Config;
using IronHive.Host.Oops;
using IronHive.Host.Providers;
using IronHive.Host.Server;
using IronHive.Host.Session;
using IronHive.Host.Tools;
using IronHive.Host.Update;
using IronHive.Providers.Anthropic;
using IronHive.Providers.GoogleAI;
using IronHive.Providers.OpenAI;
using IronHive.Providers.OpenAI.Compatible;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using WebLookup;
using CliConfig = IronHive.Host.Config;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// Extension methods for configuring IronHive services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Sets the per-call tool time limit from <see cref="ChatBehaviorConfig.ToolCallTimeoutSeconds"/>. Called when a
    /// chat client is created — after <c>run --tool-timeout</c> has written the config — so the pipeline built earlier
    /// reads the turn's setting.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured value is zero or negative.</exception>
    internal static void ApplyToolCallTimeout(ToolInvocationOptions options, ChatBehaviorConfig behavior)
    {
        options.MaxInvocationDuration = behavior.ToolCallTimeoutSeconds switch
        {
            null => null,
            > 0 and var seconds => TimeSpan.FromSeconds(seconds),
            var invalid => throw new InvalidOperationException(
                $"chatBehavior.toolCallTimeoutSeconds must be a positive number of seconds (was {invalid}); remove it for no limit."),
        };
    }

    /// <summary>
    /// Adds IronHive CLI services to the service collection.
    /// </summary>
    /// <summary>
    /// The LMSupply config the chat provider is registered on: the user's own when LMSupply is the
    /// enabled fallback, otherwise a copy with <c>Enabled</c> forced on so <c>/model</c> can still
    /// select it — and in both cases carrying the configured model ids, never the class defaults.
    /// </summary>
    internal static CliConfig.LMSupplyConfig SelectableLMSupplyConfig(CliConfig.LMSupplyConfig configured) =>
        configured.Enabled
            ? configured
            : new CliConfig.LMSupplyConfig
            {
                Enabled = true,
                GeneratorModel = configured.GeneratorModel,
                MaxContextLength = configured.MaxContextLength
            };

    /// <summary>
    /// The tool-call permission policy. MCP tools keep the names their servers give them, so the <c>mcp_tools</c> rules
    /// find them through the plugin manager's <see cref="IMcpPluginManager.IsMcpTool"/>.
    /// </summary>
    internal static ToolCallPolicy CreateToolCallPolicy(IronHive.Agent.Permissions.PermissionConfig permissions, IMcpPluginManager? mcp) =>
        mcp is null ? new ToolCallPolicy(permissions) : new ToolCallPolicy(permissions, mcp.IsMcpTool);

    public static IServiceCollection AddIronHiveServices(this IServiceCollection services)
    {
        // Load configuration (config.yaml, 4-scope merge; migrate legacy settings.json once)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalConfigPath = Path.Combine(userProfile, ".ironhive", "config.yaml");
        var projectRoot = Directory.GetCurrentDirectory();
        var legacySettingsPath = Path.Combine(userProfile, ".ironhive", "settings.json");
        // Diagnostics go to stderr: stdout is the command's output (a `run --json` document, server-mode JSON Lines), and
        // a config warning written there would corrupt it.
        using var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));
        var configLogger = bootstrapLoggerFactory.CreateLogger<ConfigurationManager>();
        ConfigMigrator.MigrateIfNeeded(globalConfigPath, projectRoot, legacySettingsPath, configLogger);
        var config = new ConfigurationManager(projectRoot, globalConfigPath, configLogger).Load();
        services.AddSingleton(config);
        // The commands that read and write config.yaml later (`get`, `set`, `config`, `doctor`) get a manager that logs
        // through the application's logging; the bootstrap factory above is disposed when this method returns.
        services.AddSingleton(sp => new ConfigurationManager(projectRoot, globalConfigPath, sp.GetService<ILogger<ConfigurationManager>>()));

        // Register HttpClient factory with named clients
        services.AddHttpClient();
        services.AddHttpClient("GpuStack", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("Webhook", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Register providers with fallback chain
        RegisterProviders(services, config);

        // Note: IChatClient is obtained via IChatClientFactory.CreateAsync() at runtime
        // This avoids synchronous blocking during DI resolution

        // Register WebLookup services (web search + site exploration)
        RegisterWebLookup(services, config);

        // Register DeepResearch tool (autonomous research agent)
        RegisterDeepResearch(services, config);

        // Register MCP plugin manager for external tool integration
        // An MCP server that needs a person before a call can finish (URL-mode elicitation) asks the client on the wire while
        // `run --server` has one attached, otherwise the terminal.
        services.AddSingleton<Services.ConsoleElicitationHandler>();
        services.AddSingleton<IMcpElicitationHandler, Services.HostElicitationHandler>();
        services.AddSingleton<IMcpPluginManager>(sp =>
        {
            var logger = sp.GetService<ILogger<McpPluginManager>>();
            return new McpPluginManager(sp.GetRequiredService<IMcpElicitationHandler>(), logger);
        });

        // Register IndexThinking services
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();

        // Note: IAgentLoop is obtained via IAgentLoopFactory.CreateAsync() at runtime
        // This avoids synchronous blocking during DI resolution

        // Agent Skills from config.yaml `skills:` — the loader and its instruction contributor. The loop factory
        // adds the load_skill tool. Nothing is registered while no root is configured.
        if (config.Skills.Roots.Count > 0)
        {
            services.AddAgentSkills(config.Skills.ToSkillsConfig(Directory.GetCurrentDirectory()));
        }

        // Register the loop factory for runtime model/provider selection. The host-side interface
        // also hands back the registered tools (per-turn tool selection resolves names against them).
        services.AddSingleton<IAgentLoopFactory>(sp => sp.GetRequiredService<IHostAgentLoopFactory>());
        services.AddSingleton<IHostAgentLoopFactory>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IChatClientFactory>();
            var turnManager = sp.GetRequiredService<IThinkingTurnManager>();
            var oopsService = sp.GetService<IOopsService>();
            var webSearchTool = sp.GetService<WebSearchTool>();
            var deepResearchTool = sp.GetService<DeepResearchTool>();
            var mcpPluginManager = sp.GetService<IMcpPluginManager>();
            var logger = sp.GetService<ILogger<AgentLoopFactory>>();

            // A transient provider failure is retried once, and a usage limit an embedder registered is
            // enforced -- the same turn safeguards AgentLoop applies (ThinkingAgentLoop had neither).
            return new AgentLoopFactory(clientFactory, turnManager, oopsService, webSearchTool, deepResearchTool, mcpPluginManager, logger, config.Compaction,
                sp.GetService<IErrorRecoveryService>(), sp.GetService<IUsageLimiter>(), config.Advisor,
                sp.GetServices<IronHive.Agent.Context.ISystemInstructionContributor>(),
                skills: sp.GetService<IronHive.Agent.Skills.SkillsLoader>(),
                fileToolOptions: sp.GetService<IronHive.Agent.Tools.FileToolOptions>(),
                permissions: config.Permissions,
                budget: config.Budget,
                agentsMd: config.AgentsMd,
                toolRetrieval: config.ToolRetrieval,
                delegation: config.Delegation,
                plainClients: sp.GetRequiredService<PlainChatClients>().Factory,
                chatBehavior: config.ChatBehavior,
                historySummary: config.HistorySummary,
                shell: config.Shell);
        });

        // Error recovery for the loops the factory builds. TryAdd keeps an embedder's own registration.
        services.TryAddSingleton<IErrorRecoveryService>(_ => new ErrorRecoveryService());

        // Register usage tracker for session-level token tracking
        services.AddSingleton<IUsageTracker, UsageTracker>();

        // Register mode manager for Plan/Work/HITL mode system
        services.AddSingleton<IModeManager, ModeManager>();
        services.AddSingleton<IModeToolFilter>(sp =>
        {
            var ironHiveConfig = sp.GetRequiredService<IronHiveConfig>();
            return new ModeToolFilter(ironHiveConfig.Permissions);
        });
        services.AddSingleton<IToolCallPolicy>(sp =>
            CreateToolCallPolicy(sp.GetRequiredService<IronHiveConfig>().Permissions, sp.GetService<IMcpPluginManager>()));
        // The terminal answers approvals, except while `run --server` has a client attached to the bridge.
        services.AddSingleton<HitlBridge>(sp =>
        {
            var approval = sp.GetRequiredService<IronHiveConfig>().Approval;
            return new HitlBridge(
                TimeSpan.FromSeconds(approval.TimeoutSeconds),
                deferAfter: approval.DeferAfterSeconds is { } defer ? TimeSpan.FromSeconds(defer) : null);
        });
        services.AddSingleton<Services.ConsoleApprovalService>();
        services.AddSingleton<IHumanApprovalService, Services.HostApprovalService>();
        services.AddSingleton<IReplanningService, ReplanningService>();

        // Register update service for self-update functionality
        services.AddSingleton<IUpdateService>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient("GitHub");
            return new GitHubUpdateService(httpClient);  // Uses default: iyulab/ironhive-cli-releases
        });

        // Register oops service for file versioning (non-Git environments)
        services.AddSingleton<IOopsService>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient("Oops");
            return new OopsService(httpClient);
        });

        // Register session manager for transcript persistence
        services.AddSingleton<ISessionManager, SessionManager>();

        return services;
    }

    /// <summary>
    /// Strips a trailing OpenAI-style API path segment so a provider config that appends its own
    /// path does not produce a doubled one. <c>GpuStackConfig</c> appends its path (<c>/v1/</c>)
    /// unconditionally (unlike <c>OpenAICompatibleConfig</c>, whose append is idempotent), so an
    /// operator who configured the full URL would otherwise get <c>/v1/v1/</c>; the older <c>/v1-openai</c>
    /// is stripped too, so a URL written for it reaches the same server.
    /// </summary>
    internal static string StripApiPath(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        foreach (var segment in (string[])["/v1-openai", "/v1"])
        {
            if (trimmed.EndsWith(segment, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^segment.Length];
            }
        }
        return trimmed;
    }

    /// <summary>
    /// Normalizes an endpoint URL by ensuring a trailing slash
    /// and optionally appending a required path suffix if not already present.
    /// Handles all combinations: with/without trailing slash, with/without path suffix.
    /// </summary>
    internal static string NormalizeEndpoint(string endpoint, string? requiredSuffix = null)
    {
        var trimmed = endpoint.TrimEnd('/');

        if (requiredSuffix is not null)
        {
            var suffix = requiredSuffix.Trim('/');
            if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Check if the endpoint ends with a prefix segment of the required suffix
                // e.g., "/v1" is a prefix of "v1-openai" → replace instead of append
                var lastSegment = trimmed[(trimmed.LastIndexOf('/') + 1)..];
                if (lastSegment.Length > 0
                    && suffix.StartsWith(lastSegment, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed[..^lastSegment.Length] + suffix;
                }
                else
                {
                    trimmed = trimmed + "/" + suffix;
                }
            }
        }

        return trimmed + "/";
    }

    private static void RegisterWebLookup(IServiceCollection services, IronHiveConfig config)
    {
        if (!config.WebSearch.Enabled)
        {
            return;
        }

        services.AddWebLookup(builder =>
        {
            // DuckDuckGo is always available (no API key required)
            if (!string.IsNullOrEmpty(config.WebSearch.DuckDuckGoRegion))
            {
                builder.AddDuckDuckGo(config.WebSearch.DuckDuckGoRegion);
            }
            else
            {
                builder.AddDuckDuckGo();
            }

            // Tavily (optional, requires API key)
            if (!string.IsNullOrEmpty(config.WebSearch.TavilyApiKey))
            {
                builder.AddTavily(config.WebSearch.TavilyApiKey);
            }

            // SearchApi (optional, requires API key)
            if (!string.IsNullOrEmpty(config.WebSearch.SearchApiKey))
            {
                builder.AddSearchApi(config.WebSearch.SearchApiKey, config.WebSearch.SearchApiEngine);
            }
        });

        // Register WebSearchTool
        services.AddSingleton(sp =>
        {
            var searchClient = sp.GetRequiredService<WebSearchClient>();
            var siteExplorer = sp.GetRequiredService<SiteExplorer>();
            return new WebSearchTool(
                searchClient,
                siteExplorer,
                config.WebSearch.DefaultMaxResults,
                config.WebSearch.MaxSitemapEntries);
        });
    }

    private static void RegisterDeepResearch(IServiceCollection services, IronHiveConfig config)
    {
        if (!config.DeepResearch.Enabled)
        {
            return;
        }

        // Resolve Tavily API key: DeepResearch config > WebSearch config
        var tavilyApiKey = config.DeepResearch.TavilyApiKey ?? config.WebSearch.TavilyApiKey;

        services.AddSingleton(sp =>
        {
            var clientFactory = sp.GetRequiredService<IChatClientFactory>();
            var tool = new DeepResearchTool(clientFactory, config.DeepResearch, tavilyApiKey);

            // Subscribe to progress events for real-time CLI rendering
            tool.OnProgress += RenderResearchProgress;

            return tool;
        });
    }

    private static void RenderResearchProgress(ResearchProgress progress)
    {
        var message = progress.Type switch
        {
            ProgressType.Started =>
                $"[grey]  Research started (max {progress.MaxIterations} iterations)[/]",
            ProgressType.PlanGenerated when progress.Plan is not null =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[grey]  Plan: {0} queries, {1} angles[/]",
                    progress.Plan.GeneratedQueries.Count,
                    progress.Plan.ResearchAngles.Count),
            ProgressType.SearchCompleted when progress.Search is not null =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[grey]  Search: [white]\"{0}\"[/] \u2192 {1} results ({2})[/]",
                    Markup.Escape(TruncateText(progress.Search.Query, 50)),
                    progress.Search.ResultCount,
                    Markup.Escape(progress.Search.Provider)),
            ProgressType.AnalysisCompleted when progress.Analysis is not null =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[grey]  Analysis: {0} findings (score: {1:F2})[/]",
                    progress.Analysis.FindingsCount,
                    progress.Analysis.Score.OverallScore),
            ProgressType.IterationCompleted =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[grey]  Iteration {0}/{1} complete[/]",
                    progress.CurrentIteration,
                    progress.MaxIterations),
            ProgressType.ReportGenerationStarted =>
                "[grey]  Generating report...[/]",
            ProgressType.Completed when progress.Result is not null =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[green]  Research completed[/] [grey]({0} iterations, {1} sources, {2:F1}s)[/]",
                    progress.Result.Metadata.IterationCount,
                    progress.Result.CitedSources.Count,
                    progress.Result.Metadata.Duration.TotalSeconds),
            ProgressType.Failed when progress.Error is not null =>
                $"[red]  Research error: {Markup.Escape(progress.Error.Message)}[/]",
            _ => null
        };

        if (message is not null)
        {
            AnsiConsole.MarkupLine(message);
        }
    }

    private static string TruncateText(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }

    /// <summary>
    /// The provider config the CLI's Anthropic registration uses. No <c>BaseUrl</c>: the SDK's default is the API root
    /// and it appends the versioned path itself — the value this used to set (<c>.../v1/</c>) doubled it, so every
    /// Anthropic call from the CLI was a 404.
    /// </summary>
    internal static IronHive.Providers.Anthropic.AnthropicConfig CreateAnthropicConfig(CliConfig.AnthropicConfig configured) => new()
    {
        ApiKey = configured.ApiKey!,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "anthropic"),
    };

    /// <summary>The provider config the CLI's OpenAI (first-party, Responses) registration uses.</summary>
    internal static IronHive.Providers.OpenAI.OpenAIConfig CreateOpenAIConfig(CliConfig.OpenAIConfig configured) => new()
    {
        BaseUrl = NormalizeEndpoint(configured.Endpoint ?? "https://api.openai.com/v1"),
        ApiKey = configured.ApiKey!,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "openai"),
    };

    /// <summary>The provider config the CLI's Google AI registration uses.</summary>
    internal static IronHive.Providers.GoogleAI.GoogleAIConfig CreateGoogleAIConfig(CliConfig.GoogleAIConfig configured) => new()
    {
        HttpOptions = new Google.GenAI.Types.HttpOptions
        {
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
        },
        ApiKey = configured.ApiKey!,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "googleai"),
    };

    /// <summary>The provider config the CLI's xAI (Chat Completions) registration uses.</summary>
    internal static OpenAICompatibleConfig CreateXaiConfig(CliConfig.XaiConfig configured) => new()
    {
        BaseUrl = configured.Endpoint.TrimEnd('/'),
        ApiKey = configured.ApiKey!,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "xai"),
    };

    /// <summary>
    /// A provider block's <c>streamIdleTimeoutSeconds</c> as the provider config's <c>StreamIdleTimeout</c>: unset is no
    /// limit (the library default); zero or negative is refused here, naming the key, rather than by the provider
    /// constructor with a parameter name the config file never shows.
    /// </summary>
    internal static TimeSpan StreamIdleTimeout(int? seconds, string section) => seconds switch
    {
        null => Timeout.InfiniteTimeSpan,
        > 0 => TimeSpan.FromSeconds(seconds.Value),
        _ => throw new InvalidOperationException(
            $"{section}.streamIdleTimeoutSeconds must be a positive number of seconds (remove it for no limit); got {seconds}."),
    };

    /// <summary>The provider config the CLI's GPUStack registration uses.</summary>
    internal static IronHive.Providers.OpenAI.Compatible.GpuStack.GpuStackConfig CreateGpuStackConfig(CliConfig.GpuStackConfig configured) => new()
    {
        BaseUrl = StripApiPath(configured.Endpoint!),
        ApiKey = configured.ApiKey!,
        CarryImageToolResultsAsUserMessage = configured.CarryToolImages ?? false,
        InstructionsPlacement = configured.InstructionsPlacement ?? InstructionsPlacement.SystemAppend,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "gpuStack"),
    };

    /// <summary>
    /// The provider config the CLI's Ollama registration uses. Ollama serves the OpenAI Chat Completions surface under
    /// <c>/v1</c> and needs no key; the endpoint may be given with or without the <c>/v1</c> path.
    /// </summary>
    internal static OpenAICompatibleConfig CreateOllamaConfig(CliConfig.OllamaConfig configured) => new()
    {
        BaseUrl = configured.Endpoint.TrimEnd('/'),
        CarryImageToolResultsAsUserMessage = configured.CarryToolImages ?? false,
        InstructionsPlacement = configured.InstructionsPlacement ?? InstructionsPlacement.SystemAppend,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "ollama"),
    };

    /// <summary>The provider config the CLI's LM Studio (or any local OpenAI-compatible server) registration uses.</summary>
    internal static OpenAICompatibleConfig CreateLMStudioConfig(CliConfig.LMStudioConfig configured) => new()
    {
        BaseUrl = configured.Endpoint.TrimEnd('/'),
        ApiKey = "lm-studio",
        CarryImageToolResultsAsUserMessage = configured.CarryToolImages ?? false,
        InstructionsPlacement = configured.InstructionsPlacement ?? InstructionsPlacement.SystemAppend,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "lmstudio"),
    };

    /// <summary>
    /// The provider config the CLI's generic OpenAI-compatible registration uses: Chat Completions at the configured
    /// endpoint taken as given (no <c>/v1</c> appended — a compatible endpoint may live under another version path), the
    /// key only when one is set (no <c>Authorization</c> header otherwise).
    /// </summary>
    internal static OpenAICompatibleConfig CreateOpenAICompatibleConfig(CliConfig.OpenAICompatibleHostConfig configured) => new()
    {
        BaseUrl = configured.Endpoint!.TrimEnd('/'),
        Path = string.Empty,
        ApiKey = string.IsNullOrWhiteSpace(configured.ApiKey) ? null : configured.ApiKey,
        CarryImageToolResultsAsUserMessage = configured.CarryToolImages ?? false,
        InstructionsPlacement = configured.InstructionsPlacement ?? InstructionsPlacement.SystemAppend,
        StreamIdleTimeout = StreamIdleTimeout(configured.StreamIdleTimeoutSeconds, "openaiCompatible"),
    };

    /// <summary>
    /// How each <see cref="ProviderBlocks"/> entry becomes a chat client provider, keyed by block name. Every block has one
    /// (a test holds this); a block is built only when it is configured.
    /// </summary>
    internal static Dictionary<string, Func<IChatClientProvider>> ProviderFactories(IronHiveConfig config)
    {
        var factories = BlockProviderFactories(config);
        foreach (var entry in ProviderBlocks.DeclaredEntries(config).Where(e => e.IsConfigured))
        {
            var captured = entry;
            // A declared provider is built per model by the factory, so a model reached over another wire gets its own adapter.
            factories[captured.Id!.Trim()] = () => new DeclaredChatClientProvider(captured.ToDeclaration(), captured.Model!);
        }

        return factories;
    }

    private static Dictionary<string, Func<IChatClientProvider>> BlockProviderFactories(IronHiveConfig config) => new(StringComparer.OrdinalIgnoreCase)
    {
        // GPUStack's API path (/v1/) is GpuStackConfig's to append; the model finder uses
        // the OpenAI-shaped view it derives.
        ["gpustack"] = () =>
        {
            var gpuStack = CreateGpuStackConfig(config.GpuStack);
            return new IronhiveChatClientProvider(new OpenAICompatibleMessageGenerator(gpuStack.ToOpenAICompatible()), "gpustack",
                config.GpuStack.Model!, new OpenAIModelFinder(gpuStack.ToOpenAI()));
        },
        // Any Chat Completions server, named for the wire API; the key optional.
        ["openaiCompatible"] = () =>
        {
            var compatible = CreateOpenAICompatibleConfig(config.OpenAICompatible);
            return new IronhiveChatClientProvider(new OpenAICompatibleMessageGenerator(compatible), "openaiCompatible",
                config.OpenAICompatible.Model!, new OpenAIModelFinder(compatible.ToOpenAI()));
        },
        // First-party OpenAI: the Responses API.
        ["openai"] = () =>
        {
            var openAI = CreateOpenAIConfig(config.OpenAI);
            return new IronhiveChatClientProvider(new OpenAIMessageGenerator(openAI), "openai", config.OpenAI.Model!, new OpenAIModelFinder(openAI));
        },
        ["anthropic"] = () =>
        {
            var anthropic = CreateAnthropicConfig(config.Anthropic);
            return new IronhiveChatClientProvider(new AnthropicMessageGenerator(anthropic), "anthropic", config.Anthropic.Model!,
                new AnthropicModelFinder(anthropic));
        },
        ["google"] = () =>
        {
            var google = CreateGoogleAIConfig(config.GoogleAI);
            return new IronhiveChatClientProvider(new GoogleAIMessageGenerator(google), "google", config.GoogleAI.Model!,
                new GoogleAIModelFinder(google));
        },
        // xAI, Ollama and LM Studio serve Chat Completions (IronHive.Providers.OpenAI.Compatible — provider isolation).
        ["xai"] = () =>
        {
            var xai = CreateXaiConfig(config.Xai);
            return new IronhiveChatClientProvider(new OpenAICompatibleMessageGenerator(xai), "xai", config.Xai.Model!, new OpenAIModelFinder(xai.ToOpenAI()));
        },
        ["ollama"] = () =>
        {
            var ollama = CreateOllamaConfig(config.Ollama);
            return new IronhiveChatClientProvider(new OpenAICompatibleMessageGenerator(ollama), "ollama", config.Ollama.Model!,
                new OpenAIModelFinder(ollama.ToOpenAI()));
        },
        ["lmstudio"] = () =>
        {
            var lmStudio = CreateLMStudioConfig(config.LMStudio);
            return new IronhiveChatClientProvider(new OpenAICompatibleMessageGenerator(lmStudio), "lmstudio", config.LMStudio.Model!,
                new OpenAIModelFinder(lmStudio.ToOpenAI()));
        },
    };

    private static void RegisterProviders(IServiceCollection services, IronHiveConfig config)
    {
        // ironhive 0.8.0 removed the keyed provider registry (HiveServiceBuilder/IHiveService.Providers).
        // IHiveService itself was only ever used here as a lookup for the raw generator/finder instances
        // this method registered a few lines above -- nothing else in this codebase consumes it -- so
        // providers are now constructed directly instead of round-tripping through IHiveServiceBuilder.
        var declaredProblems = ProviderBlocks.Problems(config);
        if (declaredProblems.Count > 0)
        {
            throw new InvalidOperationException("config.yaml providers: " + string.Join("; ", declaredProblems) + ".");
        }

        var providersDict = new Dictionary<string, IChatClientProvider>(StringComparer.OrdinalIgnoreCase);

        // One registration per configured block, in ProviderBlocks order; the first configured one is the default
        // (--provider overrides it; README «Providers» lists the order).
        IChatClientProvider? defaultProvider = null;
        var factories = ProviderFactories(config);
        foreach (var block in ProviderBlocks.Configured(config))
        {
            var provider = factories.TryGetValue(block.Name, out var create)
                ? create()
                : throw new InvalidOperationException($"Provider block '{block.Name}' has no registration.");
            providersDict[block.Name] = provider;
            foreach (var alias in block.Aliases)
            {
                providersDict[alias] = provider;
            }

            defaultProvider ??= provider;
        }

        // LMSupply chat provider — registered exactly once, always, so /model can select it even
        // when it is not the enabled fallback; when disabled in config it is registered on a copy
        // with Enabled forced on. It always carries the configured model ids: a second registration
        // on a fresh LMSupplyConfig used to shadow this one (last registration wins), so the
        // generatorModel a user set was never read and every local run loaded the class default.
        var lmSupplyConfig = SelectableLMSupplyConfig(config.LMSupply);
        services.AddSingleton<LMSupplyChatClientProvider>(sp =>
            new LMSupplyChatClientProvider(lmSupplyConfig, sp.GetService<ILogger<LMSupplyChatClientProvider>>()));

        // Register primary IChatClientProvider
        services.AddSingleton<IChatClientProvider>(sp =>
        {
            if (defaultProvider is not null)
            {
                return defaultProvider;
            }

            // No remote provider, try LMSupply
            var lmSupply = sp.GetService<LMSupplyChatClientProvider>();
            if (lmSupply?.IsAvailable == true)
            {
                return lmSupply;
            }

            throw new InvalidOperationException(
                "No API provider configured or available.\n" +
                "\n" +
                "Please configure one of the following in .env file:\n" +
                "  - OPENAI_API_KEY and OPENAI_MODEL\n" +
                "  - ANTHROPIC_API_KEY and ANTHROPIC_MODEL\n" +
                "  - GOOGLE_API_KEY and GOOGLE_MODEL\n" +
                "  - GPUSTACK_ENDPOINT, GPUSTACK_API_KEY, and GPUSTACK_MODEL\n" +
                "  - OPENAI_COMPATIBLE_ENDPOINT and OPENAI_COMPATIBLE_MODEL (any Chat Completions server; key optional)\n" +
                "\n" +
                "Or use '/model local' for local inference.\n" +
                "\n" +
                "See .env.example for configuration examples.");
        });

        // Register IChatClientFactory for runtime model/provider selection
        services.AddSingleton<IChatClientFactory>(sp =>
        {
            // Add LMSupply to providers
            var lmSupply = sp.GetRequiredService<LMSupplyChatClientProvider>();
            providersDict["lmsupply"] = lmSupply;
            providersDict["local"] = lmSupply; // Alias

            var primary = sp.GetRequiredService<IChatClientProvider>();

            // Decorator chain (outer → inner):
            //   FunctionInvokingChatClient (M.E.AI tool-call orchestrator, installed by UseToolInvocationPipeline)
            //     → ToolRoundContextChatClient (unbound here; the agent loop binds its own ContextManager, so each
            //       tool round of a turn gets tool-result compaction and observation masking — not only the first call)
            //       → UsageLimitChatClient (unbound here; a loop with a budget binds its own limiter, so the budget is
            //         checked before every model call of a turn, not only before the turn)
            //       → TokenBudgetChatClient (graceful exit when accumulated history nears the context window)
            //         → inner LMSupply / OpenAI / Anthropic / etc.
            // The FunctionInvoker is a ToolInvocationPipeline (HostChatPipeline.CreateToolInvocationPipeline): loop guards
            // (unparseable arguments, a repeated call, a repeated error), then the permission rules and the
            // human approval prompt in front of every call (Allow / Deny / Ask), then ResilientArgumentsMiddleware
            // around the tool, which turns per-tool-call marshaller errors into recovery directives. The pipeline is
            // reachable from the client, so a loop also runs host-supplied results through its result stage.
            // TokenBudgetChatClient handles per-iteration history-size overflow.
            // Iteration / consecutive-error caps come from ChatBehaviorConfig so they can be
            // tuned per model without forking: a malformed tool call must not throw out of the
            // turn, a retry storm must not overflow a small context window, and the right caps
            // differ between a 4K and a 16K+ model.
            var toolInvocationOptions = new ToolInvocationOptions();
            var toolInvocationPipeline = HostChatPipeline.CreateToolInvocationPipeline(
                sp.GetRequiredService<IToolCallPolicy>(),
                sp.GetService<IHumanApprovalService>(),
                sp.GetService<ILoggerFactory>(),
                sp.GetService<IModeManager>(),
                sp.GetRequiredService<IModeToolFilter>(),
                toolInvocationOptions);
            IChatClient ClientDecorator(IChatClient inner)
            {
                ApplyToolCallTimeout(toolInvocationOptions, config.ChatBehavior);
                // compaction.maxContextTokens is the window the user declared for a model nothing else knows: the budget
                // guard uses it too when the provider reports none.
                return HostChatPipeline.Decorate(inner, config.ChatBehavior, toolInvocationPipeline, OtlpTracing.ChatSourceName,
                    config.Compaction.MaxContextTokens ?? 0);
            }

            return new ChatClientFactory(providersDict, primary, ClientDecorator);
        });

        // Plain provider clients for delegated agents and the history summarizer: no tool pipeline (the agent runtime runs
        // delegated tools through the session's pipeline itself; a summary request has none), no usage limiter (the
        // delegation tools charge the session budget per run) and no token budget guard (a summary is requested exactly
        // when the history is near the window).
        services.AddSingleton(sp =>
        {
            var lmSupply = sp.GetRequiredService<LMSupplyChatClientProvider>();
            providersDict["lmsupply"] = lmSupply;
            providersDict["local"] = lmSupply;
            return new PlainChatClients(new ChatClientFactory(
                providersDict, sp.GetRequiredService<IChatClientProvider>(), static inner => inner));
        });
    }
}

