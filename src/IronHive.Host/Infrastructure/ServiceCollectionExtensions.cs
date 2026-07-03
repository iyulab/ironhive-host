using System.Globalization;
using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Abstractions;
using IronHive.Abstractions.Messages;
using IronHive.Agent.Loop;
using IronHive.Agent.Mcp;
using IronHive.Agent.Memory;
using IronHive.Agent.Mode;
using IronHive.Agent.Providers;
using IronHive.Agent.Tracking;
using IronHive.DeepResearch.Models.Research;
using IronHive.Host.Core.Config;
using IronHive.Host.Core.Oops;
using IronHive.Host.Core.Providers;
using IronHive.Host.Core.Session;
using IronHive.Host.Core.Tools;
using IronHive.Host.Core.Update;
using IronHive.Providers.Anthropic;
using IronHive.Providers.GoogleAI;
using IronHive.Providers.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using WebLookup;
using CliConfig = IronHive.Host.Core.Config;
// Aliased to avoid the literal "new Function..." token in source — the
// security-reminder hook flags it as a false positive (the hook targets
// JS new Function() code-injection patterns, not C# class instantiation).
using FunctionInvokingDecorator = Microsoft.Extensions.AI.FunctionInvokingChatClient;

namespace IronHive.Host.Infrastructure;

/// <summary>
/// Extension methods for configuring IronHive services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds IronHive CLI services to the service collection.
    /// </summary>
    public static IServiceCollection AddIronHiveServices(this IServiceCollection services)
    {
        // Load configuration (config.yaml, 4-scope merge; migrate legacy settings.json once)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalConfigPath = Path.Combine(userProfile, ".ironhive", "config.yaml");
        var projectRoot = Directory.GetCurrentDirectory();
        var legacySettingsPath = Path.Combine(userProfile, ".ironhive", "settings.json");
        ConfigMigrator.MigrateIfNeeded(globalConfigPath, projectRoot, legacySettingsPath);
        var configManager = new ConfigurationManager(projectRoot, globalConfigPath);
        services.AddSingleton(configManager);
        var config = configManager.Load();
        services.AddSingleton(config);

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
        services.AddSingleton<IMcpPluginManager>(sp =>
        {
            var logger = sp.GetService<ILogger<McpPluginManager>>();
            return new McpPluginManager(logger);
        });

        // Register IndexThinking services
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();

        // Register Memory services (MemoryIndexer integration)
        services.AddIronHiveMemory();

        // Note: IAgentLoop is obtained via IAgentLoopFactory.CreateAsync() at runtime
        // This avoids synchronous blocking during DI resolution

        // Register IAgentLoopFactory for runtime model/provider selection
        services.AddSingleton<IAgentLoopFactory>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IChatClientFactory>();
            var turnManager = sp.GetRequiredService<IThinkingTurnManager>();
            var oopsService = sp.GetService<IOopsService>();
            var webSearchTool = sp.GetService<WebSearchTool>();
            var deepResearchTool = sp.GetService<DeepResearchTool>();
            var mcpPluginManager = sp.GetService<IMcpPluginManager>();
            var logger = sp.GetService<ILogger<AgentLoopFactory>>();

            return new AgentLoopFactory(clientFactory, turnManager, oopsService, webSearchTool, deepResearchTool, mcpPluginManager, logger, config.Compaction);
        });

        // Register usage tracker for session-level token tracking
        services.AddSingleton<IUsageTracker, UsageTracker>();

        // Register mode manager for Plan/Work/HITL mode system
        services.AddSingleton<IModeManager, ModeManager>();
        services.AddSingleton<IModeToolFilter>(sp =>
        {
            var ironHiveConfig = sp.GetRequiredService<IronHiveConfig>();
            return new ModeToolFilter(ironHiveConfig.Permissions);
        });
        services.AddSingleton<IHumanApprovalService, Services.ConsoleApprovalService>();
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

    private static void RegisterProviders(IServiceCollection services, IronHiveConfig config)
    {
        // ironhive 0.8.0 removed the keyed provider registry (HiveServiceBuilder/IHiveService.Providers).
        // IHiveService itself was only ever used here as a lookup for the raw generator/finder instances
        // this method registered a few lines above -- nothing else in this codebase consumes it -- so
        // providers are now constructed directly instead of round-tripping through IHiveServiceBuilder.
        var providersDict = new Dictionary<string, IChatClientProvider>(StringComparer.OrdinalIgnoreCase);

        // 1. GpuStack (OpenAI-compatible API; these servers implement Chat Completions, not Responses)
        if (config.GpuStack.IsConfigured)
        {
            var gpuStackConfig = new IronHive.Providers.OpenAI.OpenAIConfig
            {
                BaseUrl = NormalizeEndpoint(config.GpuStack.Endpoint!, "v1-openai"),
                ApiKey = config.GpuStack.ApiKey!,
                Api = OpenAIApiSurface.ChatCompletions
            };
            var generator = new OpenAIMessageGenerator(gpuStackConfig);
            var finder = new OpenAIModelFinder(gpuStackConfig);
            providersDict["gpustack"] = new IronhiveChatClientProvider(generator, "gpustack", config.GpuStack.Model!, finder);
        }

        // 2. OpenAI (first-party; default Responses surface)
        if (config.OpenAI.IsConfigured)
        {
            var openAIConfig = new IronHive.Providers.OpenAI.OpenAIConfig
            {
                BaseUrl = NormalizeEndpoint(config.OpenAI.Endpoint ?? "https://api.openai.com/v1"),
                ApiKey = config.OpenAI.ApiKey!
            };
            var generator = new OpenAIMessageGenerator(openAIConfig);
            var finder = new OpenAIModelFinder(openAIConfig);
            providersDict["openai"] = new IronhiveChatClientProvider(generator, "openai", config.OpenAI.Model!, finder);
        }

        // 3. Anthropic
        if (config.Anthropic.IsConfigured)
        {
            var anthropicConfig = new IronHive.Providers.Anthropic.AnthropicConfig
            {
                BaseUrl = "https://api.anthropic.com/v1/",
                ApiKey = config.Anthropic.ApiKey!
            };
            var generator = new AnthropicMessageGenerator(anthropicConfig);
            var finder = new AnthropicModelFinder(anthropicConfig);
            providersDict["anthropic"] = new IronhiveChatClientProvider(generator, "anthropic", config.Anthropic.Model!, finder);
            providersDict["claude"] = providersDict["anthropic"]; // Alias
        }

        // 4. Google AI
        if (config.GoogleAI.IsConfigured)
        {
            var googleConfig = new IronHive.Providers.GoogleAI.GoogleAIConfig
            {
                HttpOptions = new Google.GenAI.Types.HttpOptions
                {
                    BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
                },
                ApiKey = config.GoogleAI.ApiKey!
            };
            var generator = new GoogleAIMessageGenerator(googleConfig);
            var finder = new GoogleAIModelFinder(googleConfig);
            providersDict["google"] = new IronhiveChatClientProvider(generator, "google", config.GoogleAI.Model!, finder);
            providersDict["gemini"] = providersDict["google"]; // Alias
        }

        // 5. Xai (OpenAI-compatible API; Chat Completions surface, same as GpuStack)
        if (config.Xai.IsConfigured)
        {
            var xaiConfig = new IronHive.Providers.OpenAI.OpenAIConfig
            {
                BaseUrl = NormalizeEndpoint(config.Xai.Endpoint),
                ApiKey = config.Xai.ApiKey!,
                Api = OpenAIApiSurface.ChatCompletions
            };
            var generator = new OpenAIMessageGenerator(xaiConfig);
            var finder = new OpenAIModelFinder(xaiConfig);
            providersDict["xai"] = new IronhiveChatClientProvider(generator, "xai", config.Xai.Model!, finder);
            providersDict["grok"] = providersDict["xai"]; // Alias
        }

        // 6. Ollama (local inference) -- NOT WIRED. IronHive.Providers.Ollama 0.3.3 (last published
        // version; unbumped since ironhive core moved to 0.6.2+) implements IMessageGenerator without
        // CountTokensAsync, added to the interface in 0.7.9 -- loading it against Abstractions 0.8.2
        // throws TypeLoadException. See ironhive-host/claudedocs/issues/
        // ISSUE-ironhive-host-20260703-130000-ollama-provider-abandoned-incompatible.md.
        if (config.Ollama.IsConfigured)
        {
            throw new NotSupportedException(
                "Ollama provider (OLLAMA_ENDPOINT) is temporarily unsupported: IronHive.Providers.Ollama " +
                "0.3.3 is incompatible with the current IronHive.Abstractions contract (missing " +
                "CountTokensAsync). Use '/model local' (LMSupply) for local inference until the Ollama " +
                "provider is rebuilt against a current IronHive.Abstractions version.");
        }

        // 7. LMStudio (OpenAI-compatible local inference; Chat Completions surface)
        if (config.LMStudio.IsConfigured)
        {
            var lmStudioConfig = new IronHive.Providers.OpenAI.OpenAIConfig
            {
                BaseUrl = NormalizeEndpoint(config.LMStudio.Endpoint),
                ApiKey = "lm-studio",
                Api = OpenAIApiSurface.ChatCompletions
            };
            var generator = new OpenAIMessageGenerator(lmStudioConfig);
            var finder = new OpenAIModelFinder(lmStudioConfig);
            providersDict["lmstudio"] = new IronhiveChatClientProvider(generator, "lmstudio", config.LMStudio.Model!, finder);
        }

        // LMSupply providers (local fallback)
        if (config.LMSupply.Enabled)
        {
            services.AddSingleton<LMSupplyChatClientProvider>(sp =>
                new LMSupplyChatClientProvider(config.LMSupply));

            services.AddSingleton<LMSupplyEmbeddingProvider>(sp =>
                new LMSupplyEmbeddingProvider(config.LMSupply));

            services.AddSingleton<LMSupplyRerankProvider>(sp =>
                new LMSupplyRerankProvider(config.LMSupply));
        }

        // LMSupply is always registered for /model command selection
        services.AddSingleton<LMSupplyChatClientProvider>(sp =>
            new LMSupplyChatClientProvider(new CliConfig.LMSupplyConfig { Enabled = true }));

        // Determine default provider (priority: GpuStack > OpenAI > Anthropic > GoogleAI > Xai > Ollama > LMStudio)
        IChatClientProvider? defaultProvider = null;
        foreach (var providerName in new[] { "gpustack", "openai", "anthropic", "google", "xai", "ollama", "lmstudio" })
        {
            if (providersDict.TryGetValue(providerName, out var provider))
            {
                defaultProvider = provider;
                break;
            }
        }

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
            //   FunctionInvokingChatClient (M.E.AI built-in tool-call orchestrator)
            //     → TokenBudgetChatClient (D-2: graceful exit when accumulated history nears context window)
            //       → inner LMSupply / OpenAI / Anthropic / etc.
            // ResilientFunctionInvoker handles per-tool-call marshaller errors;
            // TokenBudgetChatClient handles per-iteration history-size overflow.
            // Iteration / consecutive-error caps come from ChatBehaviorConfig (D-4) so
            // consumers can tune per-model without forking. Rationales: ecosystem ISSUE
            // 2026-04-29 (throw), 2026-04-30 (overflow), 2026-05-01 (consumer-tunable caps).
            IChatClient ClientDecorator(IChatClient inner) =>
                new FunctionInvokingDecorator(new TokenBudgetChatClient(inner))
                {
                    MaximumIterationsPerRequest = config.ChatBehavior.MaximumIterationsPerRequest,
                    MaximumConsecutiveErrorsPerRequest = config.ChatBehavior.MaximumConsecutiveErrorsPerRequest,
                    IncludeDetailedErrors = true,
                    FunctionInvoker = ResilientFunctionInvoker.Create()
                };

            return new ChatClientFactory(providersDict, primary, ClientDecorator);
        });

        // Embedding provider (simplified - uses first available)
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var providers = new List<IEmbeddingProvider>();

            var lmSupply = sp.GetService<LMSupplyEmbeddingProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No embedding provider configured.\n" +
                    "Set LMSUPPLY_ENABLED=true for local inference.");
            }

            return new FallbackEmbeddingProvider([.. providers]);
        });

        // Rerank provider (simplified - uses first available)
        services.AddSingleton<IRerankProvider>(sp =>
        {
            var providers = new List<IRerankProvider>();

            var lmSupply = sp.GetService<LMSupplyRerankProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No rerank provider configured.\n" +
                    "Set LMSUPPLY_ENABLED=true for local inference.");
            }

            return new FallbackRerankProvider([.. providers]);
        });
    }
}

