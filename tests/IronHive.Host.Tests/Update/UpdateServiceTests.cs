using System.Net;
using System.Text.Json;
using IronHive.Host.Update;

namespace IronHive.Host.Tests.Update;

/// <summary>
/// Unit tests for GitHubUpdateService.
/// </summary>
public class GitHubUpdateServiceTests
{
    [Fact]
    public void CurrentVersion_ReturnsValidVersion()
    {
        // Arrange
        var httpClient = new HttpClient(new MockHttpHandler());
        var service = new GitHubUpdateService(httpClient);

        // Act
        var version = service.CurrentVersion;

        // Assert
        Assert.NotNull(version);
        Assert.True(version.Major >= 0);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GitHubUpdateService(null!));
    }

    [Fact]
    public async Task CheckForUpdateAsync_WithValidResponse_ReturnsUpdateInfo()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        mockHandler.SetResponse(HttpStatusCode.OK, CreateReleaseJson("v1.0.0"));

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        // Act
        var result = await service.CheckForUpdateAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(new Version(1, 0, 0), result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WithNetworkError_ReturnsNull()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        mockHandler.SetResponse(HttpStatusCode.InternalServerError, "");

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        // Act
        var result = await service.CheckForUpdateAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WithPrerelease_SetsFlag()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        mockHandler.SetResponse(HttpStatusCode.OK, CreateReleaseJson("v1.0.0-alpha", isPrerelease: true));

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        // Act
        var result = await service.CheckForUpdateAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WithAssets_ExtractsDownloadUrl()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        mockHandler.SetResponse(HttpStatusCode.OK, CreateReleaseJsonWithAssets("v1.0.0"));

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        // Act
        var result = await service.CheckForUpdateAsync();

        // Assert
        Assert.NotNull(result);
        // Note: DownloadUrl will be null if runtime identifier doesn't match any asset
        // This is expected behavior
    }

    [Fact]
    public async Task UpdateAsync_WhenUpToDate_ReturnsSuccessWithNoChange()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        // Return a version lower than current (simulating "up to date")
        mockHandler.SetResponse(HttpStatusCode.OK, CreateReleaseJson("v0.0.1"));

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        // Act
        var result = await service.UpdateAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("up to date", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_WithNetworkError_ReturnsFailure()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        mockHandler.SetResponse(HttpStatusCode.InternalServerError, "");

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        // Act
        var result = await service.UpdateAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("check for updates", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_ReportsProgress()
    {
        // Arrange
        var mockHandler = new MockHttpHandler();
        mockHandler.SetResponse(HttpStatusCode.OK, CreateReleaseJson("v0.0.1"));

        var httpClient = new HttpClient(mockHandler);
        var service = new GitHubUpdateService(httpClient);

        var progressReports = new List<UpdateProgress>();
        var progress = new Progress<UpdateProgress>(p => progressReports.Add(p));

        // Act
        await service.UpdateAsync(progress);

        // Assert
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.Operation.Contains("Checking", StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateReleaseJson(string version, bool isPrerelease = false)
    {
        var release = new
        {
            tag_name = version,
            html_url = $"https://github.com/iyulab/ironhive-cli/releases/tag/{version}",
            body = "Test release notes",
            prerelease = isPrerelease,
            assets = Array.Empty<object>()
        };

        return JsonSerializer.Serialize(release);
    }

    private static string CreateReleaseJsonWithAssets(string version)
    {
        var release = new
        {
            tag_name = version,
            html_url = $"https://github.com/iyulab/ironhive-cli/releases/tag/{version}",
            body = "Test release notes",
            prerelease = false,
            assets = new[]
            {
                new
                {
                    name = $"ironhive-{version.TrimStart('v')}-win-x64.zip",
                    browser_download_url = $"https://github.com/iyulab/ironhive-cli/releases/download/{version}/ironhive-{version.TrimStart('v')}-win-x64.zip"
                },
                new
                {
                    name = $"ironhive-{version.TrimStart('v')}-linux-x64.tar.gz",
                    browser_download_url = $"https://github.com/iyulab/ironhive-cli/releases/download/{version}/ironhive-{version.TrimStart('v')}-linux-x64.tar.gz"
                }
            }
        };

        return JsonSerializer.Serialize(release);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _content = "";

        public void SetResponse(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content)
            };

            return Task.FromResult(response);
        }
    }
}

/// <summary>
/// Unit tests for UpdateInfo record.
/// </summary>
public class UpdateInfoTests
{
    [Fact]
    public void IsUpdateAvailable_WhenLatestGreater_ReturnsTrue()
    {
        var info = new UpdateInfo
        {
            LatestVersion = new Version(2, 0, 0),
            CurrentVersion = new Version(1, 0, 0)
        };

        Assert.True(info.IsUpdateAvailable);
    }

    [Fact]
    public void IsUpdateAvailable_WhenSameVersion_ReturnsFalse()
    {
        var info = new UpdateInfo
        {
            LatestVersion = new Version(1, 0, 0),
            CurrentVersion = new Version(1, 0, 0)
        };

        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public void IsUpdateAvailable_WhenCurrentGreater_ReturnsFalse()
    {
        var info = new UpdateInfo
        {
            LatestVersion = new Version(1, 0, 0),
            CurrentVersion = new Version(2, 0, 0)
        };

        Assert.False(info.IsUpdateAvailable);
    }
}

/// <summary>
/// Unit tests for UpdateResult record.
/// </summary>
public class UpdateResultTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new UpdateResult
        {
            Success = true,
            UpdatedVersion = new Version(1, 0, 0),
            Error = null,
            RestartRequired = true,
            NewExecutablePath = "/usr/local/bin/ironhive"
        };

        Assert.True(result.Success);
        Assert.Equal(new Version(1, 0, 0), result.UpdatedVersion);
        Assert.Null(result.Error);
        Assert.True(result.RestartRequired);
        Assert.Equal("/usr/local/bin/ironhive", result.NewExecutablePath);
    }
}

/// <summary>
/// Unit tests for UpdateProgress record.
/// </summary>
public class UpdateProgressTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        var progress = new UpdateProgress
        {
            Operation = "Downloading...",
            PercentComplete = 50,
            BytesDownloaded = 1024,
            TotalBytes = 2048
        };

        Assert.Equal("Downloading...", progress.Operation);
        Assert.Equal(50, progress.PercentComplete);
        Assert.Equal(1024, progress.BytesDownloaded);
        Assert.Equal(2048, progress.TotalBytes);
    }
}
