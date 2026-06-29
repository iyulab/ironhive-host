using System.Globalization;
using System.Net;
using System.Text;
using IronHive.Host.Core.Tools;
using WebLookup;

namespace IronHive.Host.Tests.Tools;

/// <summary>
/// Unit tests for WebSearchTool web search and site exploration.
/// Uses a fake ISearchProvider and mock HttpMessageHandler
/// to avoid real network calls.
/// </summary>
public class WebSearchToolTests : IDisposable
{
    private readonly FakeSearchProvider _fakeProvider;
    private readonly MockHttpHandler _mockHandler;
    private readonly WebSearchClient _searchClient;
    private readonly SiteExplorer _siteExplorer;
    private readonly WebSearchTool _tool;

    public WebSearchToolTests()
    {
        _fakeProvider = new FakeSearchProvider();
        _searchClient = new WebSearchClient(_fakeProvider);
        _mockHandler = new MockHttpHandler();
        var httpClient = new HttpClient(_mockHandler);
        _siteExplorer = new SiteExplorer(httpClient);
        _tool = new WebSearchTool(_searchClient, _siteExplorer, defaultMaxResults: 5, maxSitemapEntries: 3);
    }

    public void Dispose()
    {
        _mockHandler.Dispose();
        _searchClient.Dispose();
        _siteExplorer.Dispose();
        GC.SuppressFinalize(this);
    }

    // ================================================================
    // WebSearch tests
    // ================================================================

    [Fact]
    public async Task WebSearch_WithValidQuery_ReturnsFormattedResults()
    {
        // Arrange
        _fakeProvider.Results =
        [
            new SearchResult { Url = "https://example.com/1", Title = "Result One", Description = "First result" },
            new SearchResult { Url = "https://example.com/2", Title = "Result Two", Description = "Second result" },
        ];

        // Act
        var result = await _tool.WebSearch("test query");

        // Assert
        Assert.Contains("Found 2 results", result);
        Assert.Contains("test query", result);
        Assert.Contains("[Result One]", result);
        Assert.Contains("URL: https://example.com/1", result);
        Assert.Contains("First result", result);
        Assert.Contains("[Result Two]", result);
        Assert.Contains("URL: https://example.com/2", result);
    }

    [Fact]
    public async Task WebSearch_WithEmptyQuery_ReturnsError()
    {
        var result = await _tool.WebSearch("");
        Assert.Equal("Error: Search query is required.", result);
    }

    [Fact]
    public async Task WebSearch_WithWhitespaceQuery_ReturnsError()
    {
        var result = await _tool.WebSearch("   ");
        Assert.Equal("Error: Search query is required.", result);
    }

    [Fact]
    public async Task WebSearch_WithNoResults_ReturnsNoResultsMessage()
    {
        // Arrange
        _fakeProvider.Results = [];

        // Act
        var result = await _tool.WebSearch("obscure query");

        // Assert
        Assert.Equal("No results found for: obscure query", result);
    }

    [Fact]
    public async Task WebSearch_WithNullDescription_OmitsDescription()
    {
        // Arrange
        _fakeProvider.Results =
        [
            new SearchResult { Url = "https://example.com", Title = "No Desc", Description = null },
        ];

        // Act
        var result = await _tool.WebSearch("query");

        // Assert
        Assert.Contains("[No Desc]", result);
        Assert.Contains("URL: https://example.com", result);
        // Description line should not appear
        Assert.DoesNotContain("null", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebSearch_WithCustomMaxResults_PassesToClient()
    {
        // Arrange
        _fakeProvider.Results =
        [
            new SearchResult { Url = "https://a.com", Title = "A", Description = "a" },
        ];

        // Act
        var result = await _tool.WebSearch("query", maxResults: 3);

        // Assert
        Assert.Equal(3, _fakeProvider.LastRequestedCount);
    }

    [Fact]
    public async Task WebSearch_WithoutMaxResults_UsesDefault()
    {
        // Arrange
        _fakeProvider.Results = [];

        // Act
        await _tool.WebSearch("query");

        // Assert - defaultMaxResults is 5 (set in constructor)
        Assert.Equal(5, _fakeProvider.LastRequestedCount);
    }

    [Fact]
    public async Task WebSearch_WhenProviderThrows_ReturnsErrorMessage()
    {
        // Arrange
        _fakeProvider.ThrowException = new HttpRequestException("Network error");

        // Act
        var result = await _tool.WebSearch("query");

        // Assert — WebSearchClient catches provider exceptions and returns empty,
        // so WebSearchTool should report "No results found"
        Assert.Equal("No results found for: query", result);
    }

    // ================================================================
    // ExploreSite tests
    // ================================================================

    [Fact]
    public async Task ExploreSite_WithEmptyUrl_ReturnsError()
    {
        var result = await _tool.ExploreSite("");
        Assert.Equal("Error: Base URL is required.", result);
    }

    [Fact]
    public async Task ExploreSite_WithInvalidUrl_ReturnsError()
    {
        var result = await _tool.ExploreSite("not-a-url");
        Assert.Equal("Error: Invalid URL: not-a-url", result);
    }

    [Fact]
    public async Task ExploreSite_WithValidUrl_ReturnsSiteInfo()
    {
        // Arrange — robots.txt with sitemap reference
        _mockHandler.Responses["https://example.com/robots.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                User-agent: *
                Disallow: /private/
                Sitemap: https://example.com/sitemap.xml
                """)
        };

        _mockHandler.Responses["https://example.com/sitemap.xml"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url>
                    <loc>https://example.com/page1</loc>
                    <lastmod>2025-01-15</lastmod>
                  </url>
                  <url>
                    <loc>https://example.com/page2</loc>
                  </url>
                </urlset>
                """)
        };

        // Act
        var result = await _tool.ExploreSite("https://example.com");

        // Assert
        Assert.Contains("Site exploration: https://example.com", result);
        Assert.Contains("Sitemaps found: 1", result);
        Assert.Contains("https://example.com/sitemap.xml", result);
        Assert.Contains("Rules: 1", result);
        Assert.Contains("Sitemap entries:", result);
        Assert.Contains("https://example.com/page1", result);
        Assert.Contains("https://example.com/page2", result);
        Assert.Contains("2025-01-15", result);
        Assert.Contains("Total entries shown: 2", result);
    }

    [Fact]
    public async Task ExploreSite_WithIncludeSitemapFalse_SkipsSitemap()
    {
        // Arrange
        _mockHandler.Responses["https://example.com/robots.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                User-agent: *
                Allow: /
                Sitemap: https://example.com/sitemap.xml
                """)
        };

        // Act
        var result = await _tool.ExploreSite("https://example.com", includeSitemap: false);

        // Assert
        Assert.Contains("Site exploration: https://example.com", result);
        Assert.Contains("Sitemaps found: 1", result);
        Assert.DoesNotContain("Sitemap entries:", result);
    }

    [Fact]
    public async Task ExploreSite_TruncatesAtMaxSitemapEntries()
    {
        // Arrange — sitemap with 5 entries, but max is 3
        _mockHandler.Responses["https://example.com/robots.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Sitemap: https://example.com/sitemap.xml")
        };

        var sitemapXml = new StringBuilder();
        sitemapXml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sitemapXml.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        for (var i = 1; i <= 5; i++)
        {
            sitemapXml.AppendLine(CultureInfo.InvariantCulture, $"  <url><loc>https://example.com/page{i}</loc></url>");
        }
        sitemapXml.AppendLine("</urlset>");

        _mockHandler.Responses["https://example.com/sitemap.xml"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sitemapXml.ToString())
        };

        // Act
        var result = await _tool.ExploreSite("https://example.com");

        // Assert — should show only 3 entries (maxSitemapEntries)
        Assert.Contains("https://example.com/page1", result);
        Assert.Contains("https://example.com/page2", result);
        Assert.Contains("https://example.com/page3", result);
        Assert.DoesNotContain("https://example.com/page4", result);
        Assert.Contains("truncated at 3 entries", result);
    }

    [Fact]
    public async Task ExploreSite_WithNoSitemaps_ShowsEmptySitemaps()
    {
        // Arrange
        _mockHandler.Responses["https://example.com/robots.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                User-agent: *
                Disallow: /admin/
                """)
        };

        // Act
        var result = await _tool.ExploreSite("https://example.com");

        // Assert
        Assert.Contains("Sitemaps found: 0", result);
        Assert.DoesNotContain("Sitemap entries:", result);
    }

    [Fact]
    public async Task ExploreSite_WhenRobotsNotFound_ReturnsAllowAll()
    {
        // Arrange — 404 for robots.txt
        _mockHandler.Responses["https://example.com/robots.txt"] =
            new HttpResponseMessage(HttpStatusCode.NotFound);

        // Act
        var result = await _tool.ExploreSite("https://example.com");

        // Assert
        Assert.Contains("Site exploration: https://example.com", result);
        Assert.Contains("Sitemaps found: 0", result);
        Assert.Contains("Rules: 0", result);
    }

    // ================================================================
    // Edge case tests
    // ================================================================

    [Fact]
    public async Task WebSearch_WithUnicodeQuery_ReturnsResults()
    {
        // Arrange
        _fakeProvider.Results =
        [
            new SearchResult { Url = "https://example.kr/1", Title = "한글 결과", Description = "유니코드 테스트" },
        ];

        // Act
        var result = await _tool.WebSearch("최신 .NET 변경사항");

        // Assert
        Assert.Contains("Found 1 results", result);
        Assert.Contains("최신 .NET 변경사항", result);
        Assert.Contains("[한글 결과]", result);
        Assert.Contains("유니코드 테스트", result);
    }

    [Fact]
    public async Task WebSearch_WithNullQuery_ReturnsError()
    {
        var result = await _tool.WebSearch(null!);
        Assert.Equal("Error: Search query is required.", result);
    }

    [Fact]
    public async Task ExploreSite_WithRelativeUrl_ReturnsError()
    {
        var result = await _tool.ExploreSite("/relative/path");
        Assert.Equal("Error: Invalid URL: /relative/path", result);
    }

    [Fact]
    public async Task ExploreSite_WithMultipleSitemaps_ProcessesAll()
    {
        // Arrange — robots.txt with two sitemaps
        _mockHandler.Responses["https://example.com/robots.txt"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                Sitemap: https://example.com/sitemap1.xml
                Sitemap: https://example.com/sitemap2.xml
                """)
        };

        _mockHandler.Responses["https://example.com/sitemap1.xml"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.com/a</loc></url>
                </urlset>
                """)
        };

        _mockHandler.Responses["https://example.com/sitemap2.xml"] = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.com/b</loc></url>
                </urlset>
                """)
        };

        // Act
        var result = await _tool.ExploreSite("https://example.com");

        // Assert
        Assert.Contains("Sitemaps found: 2", result);
        Assert.Contains("https://example.com/a", result);
        Assert.Contains("https://example.com/b", result);
        Assert.Contains("Total entries shown: 2", result);
    }

    [Fact]
    public async Task WebSearch_WithEmptyDescription_OmitsDescription()
    {
        // Arrange — empty string (not null) description
        _fakeProvider.Results =
        [
            new SearchResult { Url = "https://example.com", Title = "Title", Description = "   " },
        ];

        // Act
        var result = await _tool.WebSearch("query");

        // Assert — whitespace-only description should be omitted
        Assert.Contains("[Title]", result);
        Assert.Contains("URL: https://example.com", result);
    }

    // ================================================================
    // CancellationToken propagation tests
    // ================================================================

    [Fact]
    public async Task WebSearch_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _fakeProvider.Results =
        [
            new SearchResult { Url = "https://example.com", Title = "T", Description = "D" }
        ];
        _fakeProvider.RespectCancellation = true;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _tool.WebSearch("query", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExploreSite_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockHandler.RespectCancellation = true;

        // Act & Assert — HttpClient wraps OperationCanceledException in TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tool.ExploreSite("https://example.com", cancellationToken: cts.Token));
    }

    // ================================================================
    // Constructor / Dispose tests
    // ================================================================

    [Fact]
    public void Constructor_WithNullSearchClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebSearchTool(null!, new SiteExplorer()));
    }

    [Fact]
    public void Constructor_WithNullSiteExplorer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebSearchTool(new WebSearchClient(), null!));
    }

    // ================================================================
    // Test doubles
    // ================================================================

    /// <summary>
    /// Fake search provider that returns configurable results.
    /// </summary>
    private sealed class FakeSearchProvider : ISearchProvider
    {
        public string Name => "FakeProvider";
        public IReadOnlyList<SearchResult> Results { get; set; } = [];
        public Exception? ThrowException { get; set; }
        public bool RespectCancellation { get; set; }
        public int LastRequestedCount { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            if (RespectCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            LastRequestedCount = count;

            if (ThrowException is not null)
            {
                throw ThrowException;
            }

            return Task.FromResult(Results);
        }
    }

    /// <summary>
    /// Mock HTTP handler that returns configurable responses per URL.
    /// </summary>
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        public Dictionary<string, HttpResponseMessage> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool RespectCancellation { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (RespectCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var url = request.RequestUri?.ToString() ?? "";

            if (Responses.TryGetValue(url, out var response))
            {
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
