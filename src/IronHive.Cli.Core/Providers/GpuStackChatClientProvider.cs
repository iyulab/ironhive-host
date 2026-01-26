using System.ClientModel;
using IronHive.Cli.Core.Config;
using Microsoft.Extensions.AI;
using OpenAI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// GpuStack/OpenAI-compatible API chat client provider.
/// </summary>
public sealed class GpuStackChatClientProvider : IChatClientProvider
{
    private readonly GpuStackConfig _config;
    private readonly Dictionary<string, IChatClient> _clientCache = new();
    private bool _disposed;

    public GpuStackChatClientProvider(GpuStackConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public string ProviderName => "gpustack";

    /// <inheritdoc />
    public bool IsAvailable => _config.IsConfigured;

    /// <inheritdoc />
    public IChatClient GetChatClient() => GetChatClient(null);

    /// <inheritdoc />
    public IChatClient GetChatClient(string? modelOverride)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("GpuStack is not configured.");
        }

        var model = modelOverride ?? _config.Model!;

        if (!_clientCache.TryGetValue(model, out var chatClient))
        {
            var endpoint = new Uri(_config.Endpoint!);
            var credential = new ApiKeyCredential(_config.ApiKey!);
            var options = new OpenAIClientOptions { Endpoint = endpoint };

            var openAiClient = new OpenAIClient(credential, options);
            chatClient = openAiClient
                .GetChatClient(model)
                .AsIChatClient();

            _clientCache[model] = chatClient;
        }

        return chatClient;
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return false;
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var response = await httpClient.GetAsync(
                $"{_config.Endpoint}/v1/models",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _clientCache.Clear();
        _disposed = true;

        return ValueTask.CompletedTask;
    }
}
