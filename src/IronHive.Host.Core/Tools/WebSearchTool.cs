using System.ComponentModel;
using System.Globalization;
using System.Text;
using WebLookup;

namespace IronHive.Host.Core.Tools;

/// <summary>
/// Web search and site exploration tools for the CLI agent.
/// Uses WebLookup library (DuckDuckGo by default, Tavily/SearchApi optional).
/// </summary>
public sealed class WebSearchTool
{
    private readonly WebSearchClient _searchClient;
    private readonly SiteExplorer _siteExplorer;
    private readonly int _defaultMaxResults;
    private readonly int _maxSitemapEntries;

    public WebSearchTool(
        WebSearchClient searchClient,
        SiteExplorer siteExplorer,
        int defaultMaxResults = 10,
        int maxSitemapEntries = 50)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _siteExplorer = siteExplorer ?? throw new ArgumentNullException(nameof(siteExplorer));
        _defaultMaxResults = defaultMaxResults;
        _maxSitemapEntries = maxSitemapEntries;
    }

    /// <summary>
    /// Searches the web for information and returns relevant URLs and descriptions.
    /// </summary>
    [Description("Search the web for information and return relevant URLs with titles and descriptions. Use this to find up-to-date information or research specific topics.")]
    public async Task<string> WebSearch(
        [Description("Search query")] string query,
        [Description("Maximum number of results (default: 10)")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: Search query is required.";
        }

        try
        {
            var max = maxResults ?? _defaultMaxResults;
            var results = await _searchClient.SearchAsync(query, new WebSearchOptions
            {
                MaxResultsPerProvider = max
            }, cancellationToken);

            if (results.Count == 0)
            {
                return $"No results found for: {query}";
            }

            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Found {results.Count} results for \"{query}\":");
            sb.AppendLine();

            foreach (var result in results)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  [{result.Title}]");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  URL: {result.Url}");
                if (!string.IsNullOrWhiteSpace(result.Description))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {result.Description}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error searching the web: {ex.Message}";
        }
    }

    /// <summary>
    /// Explores a website's structure by analyzing its robots.txt and sitemap.
    /// </summary>
    [Description("Explore a website's structure by analyzing its robots.txt and sitemap. Returns URL list and site structure. Use this to systematically discover content on a specific site.")]
    public async Task<string> ExploreSite(
        [Description("Base URL of the site (e.g., https://example.com)")] string baseUrl,
        [Description("Whether to include sitemap URLs (default: true)")] bool includeSitemap = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "Error: Base URL is required.";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"Error: Invalid URL: {baseUrl}";
        }

        try
        {
            var robots = await _siteExplorer.GetRobotsAsync(uri, cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Site exploration: {baseUrl}");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Sitemaps found: {robots.Sitemaps.Count}");

            foreach (var sitemapUrl in robots.Sitemaps)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - {sitemapUrl}");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"Crawl delay: {robots.CrawlDelay?.TotalSeconds ?? 0}s");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Rules: {robots.Rules.Count}");
            sb.AppendLine();

            if (includeSitemap && robots.Sitemaps.Count > 0)
            {
                sb.AppendLine("Sitemap entries:");

                var totalEntries = 0;
                foreach (var sitemapUrl in robots.Sitemaps)
                {
                    if (!Uri.TryCreate(sitemapUrl, UriKind.Absolute, out var sitemapUri))
                    {
                        continue;
                    }

                    await foreach (var entry in _siteExplorer.StreamSitemapAsync(sitemapUri, cancellationToken))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  {entry.Url}");
                        if (entry.LastModified.HasValue)
                        {
                            sb.AppendLine(CultureInfo.InvariantCulture, $"    Last modified: {entry.LastModified.Value:yyyy-MM-dd}");
                        }

                        if (++totalEntries >= _maxSitemapEntries)
                        {
                            break;
                        }
                    }

                    if (totalEntries >= _maxSitemapEntries)
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  ... (truncated at {_maxSitemapEntries} entries)");
                        break;
                    }
                }

                sb.AppendLine(CultureInfo.InvariantCulture, $"Total entries shown: {totalEntries}");
            }

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error exploring site: {ex.Message}";
        }
    }

}
