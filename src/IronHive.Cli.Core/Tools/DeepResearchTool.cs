using System.ComponentModel;
using System.Globalization;
using System.Text;
using IronHive.Agent.Providers;
using IronHive.Cli.Core.Config;
using IronHive.DeepResearch;
using IronHive.DeepResearch.Extensions;
using IronHive.DeepResearch.Abstractions;
using IronHive.DeepResearch.Models.Research;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Cli.Core.Tools;

/// <summary>
/// Deep research tool for the CLI agent.
/// Wraps IronHive.DeepResearch to perform autonomous multi-step web research.
/// Uses lazy initialization to avoid async DI resolution issues.
/// </summary>
public sealed class DeepResearchTool : IAsyncDisposable
{
    private readonly IChatClientFactory _clientFactory;
    private readonly DeepResearchConfig _config;
    private readonly string? _tavilyApiKey;
    private IDeepResearcher? _researcher;
    private ServiceProvider? _researchServiceProvider;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public DeepResearchTool(
        IChatClientFactory clientFactory,
        DeepResearchConfig config,
        string? tavilyApiKey = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tavilyApiKey = tavilyApiKey ?? config.TavilyApiKey;
    }

    private async Task<IDeepResearcher> GetOrCreateResearcherAsync(CancellationToken cancellationToken)
    {
        if (_researcher is not null)
        {
            return _researcher;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_researcher is not null)
            {
                return _researcher;
            }

            // Create a chat client for DeepResearch using the CLI's factory
            var chatClient = _config.Provider is not null
                ? await _clientFactory.CreateAsync(_config.Provider, _config.Model, cancellationToken)
                : await _clientFactory.CreateAsync(_config.Model, cancellationToken);

            // Build a self-contained service provider for DeepResearch
            var services = new ServiceCollection();
            services.AddDeepResearch(chatClient, opts =>
            {
                opts.DefaultMaxIterations = _config.MaxIterations;

                if (!string.IsNullOrEmpty(_tavilyApiKey))
                {
                    opts.SearchApiKeys["tavily"] = _tavilyApiKey;
                }
            });

            _researchServiceProvider = services.BuildServiceProvider();
            _researcher = _researchServiceProvider.GetRequiredService<IDeepResearcher>();
            return _researcher;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Performs deep autonomous research on a topic, iteratively searching and analyzing web sources.
    /// </summary>
    [Description("Perform deep autonomous research on a topic. Iteratively searches the web, analyzes sources, and generates a comprehensive research report with citations. Use this for complex research queries that require multiple rounds of investigation.")]
    public async Task<string> DeepResearch(
        [Description("Research query or question to investigate")] string query,
        [Description("Research depth: 'quick' (1-2 min), 'standard' (3-5 min), or 'comprehensive' (10-15 min). Default: standard")] string? depth = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: Research query is required.";
        }

        try
        {
            var researcher = await GetOrCreateResearcherAsync(cancellationToken);

            var request = new ResearchRequest
            {
                Query = query,
                Depth = ParseDepth(depth),
                OutputFormat = OutputFormat.Markdown
            };

            var result = await researcher.ResearchAsync(request, cancellationToken);
            return FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error performing deep research: {ex.Message}";
        }
    }

    private static ResearchDepth ParseDepth(string? depth)
    {
        if (string.IsNullOrWhiteSpace(depth))
        {
            return ResearchDepth.Standard;
        }

        return depth.Trim().ToLowerInvariant() switch
        {
            "quick" => ResearchDepth.Quick,
            "standard" => ResearchDepth.Standard,
            "comprehensive" or "deep" => ResearchDepth.Comprehensive,
            _ => ResearchDepth.Standard
        };
    }

    private static string FormatResult(ResearchResult result)
    {
        var sb = new StringBuilder();

        // Main report
        sb.AppendLine(result.Report);
        sb.AppendLine();

        // Metadata
        sb.AppendLine("---");
        sb.AppendLine("**Research Metadata:**");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Iterations: {result.Metadata.IterationCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Queries executed: {result.Metadata.TotalQueriesExecuted}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Sources analyzed: {result.Metadata.TotalSourcesAnalyzed}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Duration: {result.Metadata.Duration.TotalSeconds:F1}s");

        if (result.Metadata.EstimatedCost > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- Estimated cost: ${result.Metadata.EstimatedCost:F4}");
        }

        // Sources summary
        if (result.CitedSources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Cited Sources ({result.CitedSources.Count}):**");
            foreach (var source in result.CitedSources)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [{source.Title}]({source.Url})");
            }
        }

        if (result.IsPartial)
        {
            sb.AppendLine();
            sb.AppendLine("*Note: This is a partial result. The research was terminated before full completion.*");
        }

        if (result.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Warnings ({result.Errors.Count}):**");
            foreach (var error in result.Errors)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {error}");
            }
        }

        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_researchServiceProvider is not null)
        {
            await _researchServiceProvider.DisposeAsync();
            _researchServiceProvider = null;
        }
        _researcher = null;
        _initLock.Dispose();
    }
}
