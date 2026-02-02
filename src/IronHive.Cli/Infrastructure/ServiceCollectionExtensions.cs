using IndexThinking.Agents;
using IndexThinking.Extensions;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Agent.Mode;
using IronHive.Cli.Core.Config;
using IronHive.Cli.Core.Memory;
using IronHive.Cli.Core.Oops;
using IronHive.Cli.Core.Providers;
using IronHive.Cli.Core.Session;
using IronHive.Cli.Core.Update;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Cli.Infrastructure;

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
        // Load configuration from .env file
        var config = EnvConfigLoader.Load();
        services.AddSingleton(config);

        // Register providers with fallback chain
        RegisterProviders(services, config);

        // Register IChatClient from provider, wrapped with FunctionInvokingChatClient
        // This enables automatic tool/function execution in the chat loop
        services.AddSingleton<IChatClient>(sp =>
        {
            var provider = sp.GetRequiredService<IChatClientProvider>();
            var innerClient = provider.GetChatClient();

            // Wrap with FunctionInvokingChatClient for automatic tool execution
            // The actual tools are provided via ChatOptions.Tools at runtime
            return new FunctionInvokingChatClient(innerClient)
            {
                MaximumIterationsPerRequest = 10,
                MaximumConsecutiveErrorsPerRequest = 3,
                IncludeDetailedErrors = true
            };
        });

        // Register IndexThinking services
        services.AddIndexThinkingAgents();
        services.AddIndexThinkingInMemoryStorage();

        // Register Memory services (MemoryIndexer integration)
        services.AddIronHiveMemory();

        // Register agent loop with IndexThinking support (default instance)
        services.AddTransient<IAgentLoop>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var turnManager = sp.GetRequiredService<IThinkingTurnManager>();

            return new ThinkingAgentLoop(
                chatClient,
                turnManager,
                new IronHive.Cli.Core.Agent.AgentOptions
                {
                    SystemPrompt = """
                        You are a helpful AI assistant with access to tools for file and system operations.

                        ## Tool Usage Guidelines
                        - Use tools only when necessary to complete the user's request.
                        - When a tool returns a success message, trust it and DO NOT verify with additional tool calls.
                        - After completing a task (e.g., writing a file), immediately report the result to the user.
                        - Avoid redundant operations: do not read a file you just wrote, or list a directory just to confirm.
                        - If a tool fails, explain the error and ask for clarification if needed.

                        ## Response Format
                        - Be concise and direct in your responses.
                        - After using tools, summarize what was done without repeating tool output verbatim.
                        """,
                    Temperature = 0.7f,
                    MaxTokens = 4096
                });
        });

        // Register IAgentLoopFactory for runtime model/provider selection
        services.AddSingleton<IAgentLoopFactory>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IChatClientFactory>();
            var turnManager = sp.GetRequiredService<IThinkingTurnManager>();
            var oopsService = sp.GetService<IOopsService>();

            return new AgentLoopFactory(clientFactory, turnManager, oopsService);
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
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IUpdateService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new GitHubUpdateService(httpClient);  // Uses default: iyulab/ironhive-cli-releases
        });

        // Register oops service for file versioning (non-Git environments)
        services.AddSingleton<IOopsService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new OopsService(httpClient);
        });

        // Register session manager for transcript persistence
        services.AddSingleton<ISessionManager, SessionManager>();

        return services;
    }

    private static void RegisterProviders(IServiceCollection services, IronHiveConfig config)
    {
        // Note: LMSupply is auto-enabled when no remote provider is configured,
        // so at least one provider will always be available.

        // GpuStack providers (primary)
        if (config.GpuStack.IsConfigured)
        {
            services.AddSingleton<GpuStackChatClientProvider>(sp =>
                new GpuStackChatClientProvider(config.GpuStack));

            services.AddSingleton<GpuStackEmbeddingProvider>(sp =>
                new GpuStackEmbeddingProvider(config.GpuStack));

            services.AddSingleton<GpuStackRerankProvider>(sp =>
                new GpuStackRerankProvider(config.GpuStack));
        }

        // LMSupply providers (fallback)
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
        // (even if not in fallback chain)
        services.AddSingleton<LMSupplyChatClientProvider>(sp =>
            new LMSupplyChatClientProvider(new LMSupplyConfig { Enabled = true }));

        // Primary provider (no fallback - fail fast if unavailable)
        services.AddSingleton<IChatClientProvider>(sp =>
        {
            var gpuStack = sp.GetService<GpuStackChatClientProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                return gpuStack;
            }

            throw new InvalidOperationException(
                "No API provider configured or available.\n" +
                "\n" +
                "Please configure GPUSTACK_* or OPENAI_* variables in .env file.\n" +
                "Use '/model local' or '--provider lmsupply' for local inference.\n" +
                "\n" +
                "See .env.example for configuration examples.");
        });

        // Register IChatClientFactory for runtime model/provider selection
        services.AddSingleton<IChatClientFactory>(sp =>
        {
            var providersDict = new Dictionary<string, IChatClientProvider>(StringComparer.OrdinalIgnoreCase);

            var gpuStack = sp.GetService<GpuStackChatClientProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providersDict["gpustack"] = gpuStack;
            }

            // LMSupply is always available for explicit selection via /model command
            var lmSupply = sp.GetRequiredService<LMSupplyChatClientProvider>();
            providersDict["lmsupply"] = lmSupply;
            providersDict["local"] = lmSupply;  // Alias for convenience

            // Get default provider - try IChatClientProvider first, fallback to lmsupply
            IChatClientProvider defaultProvider;
            try
            {
                defaultProvider = sp.GetRequiredService<IChatClientProvider>();
            }
            catch (InvalidOperationException)
            {
                // No remote provider configured, use lmsupply as default
                defaultProvider = lmSupply;
            }

            // Decorator for FunctionInvokingChatClient
            IChatClient ClientDecorator(IChatClient inner) =>
                new FunctionInvokingChatClient(inner)
                {
                    MaximumIterationsPerRequest = 10,
                    MaximumConsecutiveErrorsPerRequest = 3,
                    IncludeDetailedErrors = true
                };

            return new ChatClientFactory(providersDict, defaultProvider, ClientDecorator);
        });

        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var providers = new List<IEmbeddingProvider>();

            var gpuStack = sp.GetService<GpuStackEmbeddingProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providers.Add(gpuStack);
            }

            var lmSupply = sp.GetService<LMSupplyEmbeddingProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No embedding provider configured.\n" +
                    "\n" +
                    "Please configure one of the following options:\n" +
                    "  1. Set GPUSTACK_EMBEDDING_MODEL in your .env file\n" +
                    "  2. Set LMSUPPLY_ENABLED=true for local inference\n" +
                    "\n" +
                    "See .env.example for configuration examples.");
            }

            return new FallbackEmbeddingProvider(providers.ToArray());
        });

        services.AddSingleton<IRerankProvider>(sp =>
        {
            var providers = new List<IRerankProvider>();

            var gpuStack = sp.GetService<GpuStackRerankProvider>();
            if (gpuStack?.IsAvailable == true)
            {
                providers.Add(gpuStack);
            }

            var lmSupply = sp.GetService<LMSupplyRerankProvider>();
            if (lmSupply is not null && config.LMSupply.Enabled)
            {
                providers.Add(lmSupply);
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No rerank provider configured.\n" +
                    "\n" +
                    "Please configure one of the following options:\n" +
                    "  1. Set GPUSTACK_RERANK_MODEL in your .env file\n" +
                    "  2. Set LMSUPPLY_ENABLED=true for local inference\n" +
                    "\n" +
                    "See .env.example for configuration examples.");
            }

            return new FallbackRerankProvider(providers.ToArray());
        });
    }
}

