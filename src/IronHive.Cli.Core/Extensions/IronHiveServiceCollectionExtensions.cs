using System.ClientModel;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Providers;
using IronHive.Cli.Core.Session;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace IronHive.Cli.Core.Extensions;

/// <summary>
/// Extension methods for configuring IronHive services in DI.
/// </summary>
public static class IronHiveServiceCollectionExtensions
{
    /// <summary>
    /// Adds IronHive core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure IronHive options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronHive(
        this IServiceCollection services,
        Action<IronHiveOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new IronHiveOptions();
        configure(options);

        // Validate required options
        if (options.ChatClientFactory == null && options.ChatClient == null)
        {
            throw new InvalidOperationException(
                "Either ChatClient or ChatClientFactory must be configured. " +
                "Use options.UseChatClient() or options.UseChatClientFactory().");
        }

        // Register IChatClient or IChatClientFactory
        // Note: IChatClient is obtained via IChatClientFactory.CreateAsync() at runtime
        if (options.ChatClient != null)
        {
            services.AddSingleton(options.ChatClient);
        }
        else if (options.ChatClientFactory != null)
        {
            services.AddSingleton<IChatClientFactory>(options.ChatClientFactory);
            // Note: IChatClient is NOT registered here - use IChatClientFactory.CreateAsync() directly
        }

        // Register ISessionManager
        services.AddSingleton<ISessionManager>(sp =>
        {
            var baseDir = options.SessionStoragePath ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ironhive");
            return new SessionManager(baseDir);
        });

        // Register AgentOptions
        services.AddSingleton(sp => new AgentOptions
        {
            SystemPrompt = options.SystemPrompt,
            ModelId = options.DefaultModel,
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            Tools = options.Tools
        });

        // Register IAgentLoop only if IChatClient is directly configured
        // When using IChatClientFactory, create IAgentLoop manually via:
        //   var client = await factory.CreateAsync();
        //   var agentLoop = new AgentLoop(client, agentOptions);
        if (options.ChatClient != null)
        {
            services.AddTransient<IAgentLoop>(sp =>
            {
                var chatClient = sp.GetRequiredService<IChatClient>();
                var agentOptions = sp.GetRequiredService<AgentOptions>();
                return new AgentLoop(chatClient, agentOptions);
            });
        }

        return services;
    }

    /// <summary>
    /// Adds IronHive with a pre-configured IChatClient.
    /// Minimal setup for simple use cases.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="chatClient">The chat client to use.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronHive(
        this IServiceCollection services,
        IChatClient chatClient,
        string? systemPrompt = null)
    {
        return services.AddIronHive(options =>
        {
            options.UseChatClient(chatClient);
            if (systemPrompt != null)
            {
                options.SystemPrompt = systemPrompt;
            }
        });
    }

    /// <summary>
    /// Adds IronHive with OpenAI as the provider.
    /// Uses the official OpenAI API (https://api.openai.com).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model to use (default: gpt-4o-mini).</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronHiveWithOpenAI(
        this IServiceCollection services,
        string apiKey,
        string model = "gpt-4o-mini",
        string? systemPrompt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey));
        var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

        return services.AddIronHive(options =>
        {
            options.UseChatClient(chatClient);
            options.DefaultModel = model;
            options.SystemPrompt = systemPrompt ?? "You are a helpful assistant.";
        });
    }

    /// <summary>
    /// Adds IronHive with an OpenAI-compatible API endpoint.
    /// Works with GpuStack, vLLM, LiteLLM, Ollama (OpenAI mode), and other compatible services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoint">API endpoint URL (e.g., "http://localhost:11434/v1").</param>
    /// <param name="apiKey">API key (use "ollama" or any value for local services).</param>
    /// <param name="model">Model name.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronHiveWithOpenAICompatible(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string model,
        string? systemPrompt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var openAiOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint)
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey ?? "default"), openAiOptions);
        var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

        return services.AddIronHive(options =>
        {
            options.UseChatClient(chatClient);
            options.DefaultModel = model;
            options.SystemPrompt = systemPrompt ?? "You are a helpful assistant.";
        });
    }

    /// <summary>
    /// Adds IronHive with Ollama as the provider.
    /// Convenience method for local Ollama setup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="model">Model name (default: llama3.2).</param>
    /// <param name="endpoint">Ollama endpoint (default: http://localhost:11434/v1).</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIronHiveWithOllama(
        this IServiceCollection services,
        string model = "llama3.2",
        string endpoint = "http://localhost:11434/v1",
        string? systemPrompt = null)
    {
        return services.AddIronHiveWithOpenAICompatible(endpoint, "ollama", model, systemPrompt);
    }
}

/// <summary>
/// Configuration options for IronHive integration.
/// </summary>
public class IronHiveOptions
{
    internal IChatClient? ChatClient { get; private set; }
    internal IChatClientFactory? ChatClientFactory { get; private set; }

    /// <summary>
    /// System prompt to use for all conversations.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Default model to use when no model is specified.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Maximum tokens for responses.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Temperature for response generation (0.0 - 1.0).
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Path for session storage. Defaults to ~/.ironhive.
    /// </summary>
    public string? SessionStoragePath { get; set; }

    /// <summary>
    /// Tools available to the agent.
    /// </summary>
    public IList<AITool>? Tools { get; set; }

    /// <summary>
    /// Uses a pre-configured IChatClient.
    /// </summary>
    /// <param name="chatClient">The chat client to use.</param>
    public void UseChatClient(IChatClient chatClient)
    {
        ChatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        ChatClientFactory = null;
    }

    /// <summary>
    /// Uses a chat client factory for dynamic model/provider selection.
    /// </summary>
    /// <param name="factory">The chat client factory to use.</param>
    public void UseChatClientFactory(IChatClientFactory factory)
    {
        ChatClientFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        ChatClient = null;
    }
}
