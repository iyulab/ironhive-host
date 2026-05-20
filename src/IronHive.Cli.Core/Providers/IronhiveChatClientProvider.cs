using System.Collections.Concurrent;
using IronHive.Abstractions.Catalog;
using IronHive.Abstractions.Messages;
using IronHive.Agent.Providers;
using IronHive.Core.Compatibility;
using Microsoft.Extensions.AI;
using TokenMeter;

namespace IronHive.Cli.Core.Providers;

/// <summary>
/// ironhive IMessageGenerator를 IChatClient로 제공하는 프로바이더입니다.
/// </summary>
public sealed class IronhiveChatClientProvider : IChatClientProvider, IDisposable
{
    private readonly IMessageGenerator _generator;
    private readonly string _providerName;
    private readonly string _defaultModel;
    private readonly IModelCatalog? _catalog;
    private readonly ConcurrentDictionary<string, IChatClient> _clientCache = new();
    private bool _disposed;

    public IronhiveChatClientProvider(
        IMessageGenerator generator,
        string providerName,
        string defaultModel,
        IModelCatalog? catalog = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _defaultModel = defaultModel ?? throw new ArgumentNullException(nameof(defaultModel));
        _catalog = catalog;
    }

    /// <inheritdoc />
    public string ProviderName => _providerName;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<IChatClient> GetChatClientAsync(string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        var model = modelOverride ?? _defaultModel;

        var client = _clientCache.GetOrAdd(model, m =>
            new ChatClientAdapter(_generator, m));

        return Task.FromResult(client);
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // ironhive 프로바이더는 항상 사용 가능하다고 가정
        // 실제 연결 테스트는 첫 요청 시 수행됨
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        // 1. Try dynamic model list from ironhive IModelCatalog
        if (_catalog is not null)
        {
            try
            {
                var specs = await _catalog.ListModelsAsync(cancellationToken);
                var models = specs.Select(s =>
                {
                    var info = new AvailableModelInfo
                    {
                        ModelId = s.ModelId,
                        Provider = _providerName,
                        DisplayName = s.DisplayName ?? s.ModelId,
                        Source = ModelSource.Api,
                        IsDefault = string.Equals(s.ModelId, _defaultModel, StringComparison.OrdinalIgnoreCase),
                        ContextWindow = s is ChatModelSpec chat ? chat.ContextWindow : null
                    };
                    return info;
                }).ToList();

                // Enrich with pricing data if available
                EnrichWithPricingData(models);
                return models;
            }
            catch
            {
                // Fall through to static data
            }
        }

        // 2. Fallback: static pricing data from TokenMeter
        var pricingData = GetPricingDataForProvider(_providerName);
        if (pricingData is null)
        {
            return new[]
            {
                new AvailableModelInfo
                {
                    ModelId = _defaultModel,
                    Provider = _providerName,
                    DisplayName = _defaultModel,
                    Source = ModelSource.Static,
                    IsDefault = true
                }
            };
        }

        var staticModels = pricingData.Values
            .Select(p => new AvailableModelInfo
            {
                ModelId = p.ModelId,
                Provider = _providerName,
                DisplayName = p.DisplayName ?? p.ModelId,
                ContextWindow = p.ContextWindow,
                InputPricePerMillion = p.InputPricePerMillion,
                OutputPricePerMillion = p.OutputPricePerMillion,
                Source = ModelSource.Static,
                IsDefault = string.Equals(p.ModelId, _defaultModel, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        return staticModels;
    }

    private static void EnrichWithPricingData(List<AvailableModelInfo> models)
    {
        // Try to find pricing data for the provider
        if (models.Count == 0)
        {
            return;
        }

        var providerName = models[0].Provider;
        var pricingData = GetPricingDataForProvider(providerName);
        if (pricingData is null)
        {
            return;
        }

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            if (pricingData.TryGetValue(model.ModelId, out var pricing))
            {
                models[i] = model with
                {
                    ContextWindow = model.ContextWindow ?? pricing.ContextWindow,
                    InputPricePerMillion = pricing.InputPricePerMillion,
                    OutputPricePerMillion = pricing.OutputPricePerMillion
                };
            }
        }
    }

    private static IReadOnlyDictionary<string, ModelInfo>? GetPricingDataForProvider(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "openai" => ModelCatalog.OpenAI,
            "anthropic" or "claude" => ModelCatalog.Anthropic,
            "google" or "gemini" or "googleai" => ModelCatalog.Google,
            "xai" or "grok" => ModelCatalog.XAI,
            "azure" or "azure-openai" or "azureopenai" => ModelCatalog.Azure,
            _ => null
        };
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
        _generator.Dispose();
        _disposed = true;
    }
}
