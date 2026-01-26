using System.Globalization;
using IronHive.Cli.Core.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;

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
        // Register configuration
        services.AddSingleton<IronHiveConfig>();

        // Register IChatClient factory
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // Register agent loop
        services.AddTransient<IAgentLoop>(sp =>
        {
            var config = sp.GetRequiredService<IronHiveConfig>();
            var clientFactory = sp.GetRequiredService<IChatClientFactory>();
            var chatClient = clientFactory.Create(config);

            return new AgentLoop(chatClient, new AgentOptions
            {
                SystemPrompt = config.SystemPrompt,
                Temperature = config.Temperature,
                MaxTokens = config.MaxTokens
            });
        });

        return services;
    }
}

/// <summary>
/// IronHive CLI configuration.
/// </summary>
public class IronHiveConfig
{
    /// <summary>
    /// The model provider (e.g., "openai", "azure", "ollama", "gpustack").
    /// </summary>
    public string Provider { get; set; } = "openai";

    /// <summary>
    /// The model name/ID to use.
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// API key for the provider.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base URL for custom endpoints (Ollama, gpustack, etc.).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// System prompt for the agent.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Temperature for response generation.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Maximum tokens for response generation.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Formats temperature as string with invariant culture.
    /// </summary>
    public string? GetTemperatureString() =>
        Temperature?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats max tokens as string with invariant culture.
    /// </summary>
    public string? GetMaxTokensString() =>
        MaxTokens?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Factory interface for creating IChatClient instances.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Creates an IChatClient based on the configuration.
    /// </summary>
    IChatClient Create(IronHiveConfig config);
}

/// <summary>
/// Default implementation of IChatClientFactory.
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    public IChatClient Create(IronHiveConfig config)
    {
        return config.Provider.ToLowerInvariant() switch
        {
            "openai" => CreateOpenAIClient(config),
            "ollama" or "gpustack" => CreateOllamaCompatibleClient(config),
            _ => throw new NotSupportedException($"Provider '{config.Provider}' is not supported.")
        };
    }

    private static IChatClient CreateOpenAIClient(IronHiveConfig config)
    {
        var apiKey = config.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is required. Set OPENAI_API_KEY environment variable or provide it in config.");
        }

        var chatClient = new ChatClient(config.Model, apiKey);
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateOllamaCompatibleClient(IronHiveConfig config)
    {
        var baseUrl = config.BaseUrl ?? config.Provider switch
        {
            "ollama" => "http://localhost:11434",
            "gpustack" => "http://localhost:8000",
            _ => throw new InvalidOperationException($"Base URL is required for provider '{config.Provider}'.")
        };

        // Use OpenAI-compatible endpoint for Ollama/gpustack
        var openAiClient = new OpenAI.OpenAIClient(
            credential: new System.ClientModel.ApiKeyCredential(config.ApiKey ?? "not-needed"),
            options: new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri($"{baseUrl.TrimEnd('/')}/v1")
            });

        var chatClient = openAiClient.GetChatClient(config.Model);
        return chatClient.AsIChatClient();
    }
}
