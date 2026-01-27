using System.ClientModel;
using System.Collections.Concurrent;
using IronHive.Cli.Core.Config;
using Microsoft.Extensions.AI;
using OpenAI;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// GpuStack/OpenAI-compatible API chat client provider.
/// </summary>
public sealed class GpuStackChatClientProvider : IChatClientProvider, IDisposable
{
    private readonly GpuStackConfig _config;
    private readonly ConcurrentDictionary<string, IChatClient> _clientCache = new();
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

        return _clientCache.GetOrAdd(model, m =>
        {
            var endpoint = new Uri(_config.Endpoint!);
            var credential = new ApiKeyCredential(_config.ApiKey!);
            var options = new OpenAIClientOptions { Endpoint = endpoint };

            var openAiClient = new OpenAIClient(credential, options);
            return openAiClient
                .GetChatClient(m)
                .AsIChatClient();
        });
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
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _clientCache.Clear();
        _disposed = true;
    }
}
