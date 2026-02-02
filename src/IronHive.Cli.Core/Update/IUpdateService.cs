namespace IronHive.Cli.Core.Update;

/// <summary>
/// Service for checking and performing CLI updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Gets the current installed version.
    /// </summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// Gets whether the CLI was installed as a dotnet global tool.
    /// </summary>
    bool IsDotnetToolInstallation { get; }

    /// <summary>
    /// Checks if an update is available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update information if available, null otherwise.</returns>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the update to the latest version.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if update was successful.</returns>
    Task<UpdateResult> UpdateAsync(IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about an available update.
/// </summary>
public record UpdateInfo
{
    /// <summary>
    /// The latest available version.
    /// </summary>
    public required Version LatestVersion { get; init; }

    /// <summary>
    /// The current installed version.
    /// </summary>
    public required Version CurrentVersion { get; init; }

    /// <summary>
    /// Whether the update is a prerelease.
    /// </summary>
    public bool IsPrerelease { get; init; }

    /// <summary>
    /// Release notes or description.
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// URL to the release page.
    /// </summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>
    /// Download URL for the current platform.
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Whether an update is available.
    /// </summary>
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

/// <summary>
/// Result of an update operation.
/// </summary>
public record UpdateResult
{
    /// <summary>
    /// Whether the update was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The version updated to (if successful).
    /// </summary>
    public Version? UpdatedVersion { get; init; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether a restart is required.
    /// </summary>
    public bool RestartRequired { get; init; }

    /// <summary>
    /// Path to the new executable (if restart required).
    /// </summary>
    public string? NewExecutablePath { get; init; }
}

/// <summary>
/// Progress information for update operations.
/// </summary>
public record UpdateProgress
{
    /// <summary>
    /// Current operation description.
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>
    /// Progress percentage (0-100), or null if indeterminate.
    /// </summary>
    public int? PercentComplete { get; init; }

    /// <summary>
    /// Bytes downloaded (for download operations).
    /// </summary>
    public long? BytesDownloaded { get; init; }

    /// <summary>
    /// Total bytes to download (for download operations).
    /// </summary>
    public long? TotalBytes { get; init; }
}
