namespace IronHive.Host.Update;

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
    /// Reads the latest published version; <see cref="UpdateInfo.IsUpdateAvailable"/> says whether it is newer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest version and where it comes from.</returns>
    /// <exception cref="HttpRequestException">The release source could not be reached or answered with an error status.</exception>
    /// <exception cref="InvalidOperationException">The release source answered but published no usable version.</exception>
    Task<UpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates to the latest version, or reports that the installed version is already the latest.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was installed. A failed update throws; it is never returned.</returns>
    /// <exception cref="HttpRequestException">The release source or the download could not be reached.</exception>
    /// <exception cref="InvalidOperationException">No release fits this platform, or the installer failed.</exception>
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
/// What a completed update installed. A failed update throws instead.
/// </summary>
public record UpdateResult
{
    /// <summary>
    /// The version now installed: the new one, or the current one when <see cref="AlreadyUpToDate"/> is set.
    /// </summary>
    public required Version UpdatedVersion { get; init; }

    /// <summary>
    /// The installed version was already the latest, so nothing was installed.
    /// </summary>
    public bool AlreadyUpToDate { get; init; }

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
