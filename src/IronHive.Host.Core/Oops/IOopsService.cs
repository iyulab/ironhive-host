namespace IronHive.Host.Core.Oops;

/// <summary>
/// Service for integrating with oops file versioning tool.
/// Provides automatic version control for files outside Git repositories.
/// </summary>
public interface IOopsService
{
    /// <summary>
    /// Gets whether oops is installed and available.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Gets the installed oops version, or null if not installed.
    /// </summary>
    string? Version { get; }

    /// <summary>
    /// Ensures oops is installed, downloading if necessary.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if oops is available after this call.</returns>
    Task<bool> EnsureInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file is being tracked by oops.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>True if the file is tracked.</returns>
    bool IsTracked(string filePath);

    /// <summary>
    /// Starts versioning a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>Result message.</returns>
    Task<OopsResult> StartAsync(string filePath);

    /// <summary>
    /// Saves a snapshot of the file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="message">Optional snapshot message.</param>
    /// <returns>Result message.</returns>
    Task<OopsResult> SaveAsync(string filePath, string? message = null);

    /// <summary>
    /// Undoes changes to the file (restores last saved state).
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>Result message.</returns>
    Task<OopsResult> UndoAsync(string filePath);

    /// <summary>
    /// Goes back to a specific snapshot.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="snapshotNumber">Snapshot number to restore.</param>
    /// <returns>Result message.</returns>
    Task<OopsResult> BackAsync(string filePath, int snapshotNumber);

    /// <summary>
    /// Gets the snapshot history for a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>History output.</returns>
    Task<OopsResult> HistoryAsync(string filePath);

    /// <summary>
    /// Gets the changes (diff) for a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="snapshotA">Optional first snapshot number.</param>
    /// <param name="snapshotB">Optional second snapshot number.</param>
    /// <returns>Diff output.</returns>
    Task<OopsResult> ChangesAsync(string filePath, int? snapshotA = null, int? snapshotB = null);

    /// <summary>
    /// Gets the current status of a tracked file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>Status output.</returns>
    Task<OopsResult> StatusAsync(string filePath);

    /// <summary>
    /// Stops versioning a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>Result message.</returns>
    Task<OopsResult> StopAsync(string filePath);

    /// <summary>
    /// Cleans up orphaned stores (files that no longer exist).
    /// </summary>
    /// <param name="dryRun">If true, only preview what would be cleaned.</param>
    /// <returns>Cleanup result.</returns>
    Task<OopsResult> CleanupAsync(bool dryRun = false);

    /// <summary>
    /// Checks if oops should be automatically used for a file.
    /// Returns true if: not in Git repo AND file has been edited multiple times.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>True if oops should be auto-activated.</returns>
    bool ShouldAutoActivate(string filePath);

    /// <summary>
    /// Records a file edit for auto-activation tracking.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    void RecordEdit(string filePath);
}

/// <summary>
/// Result of an oops operation.
/// </summary>
public record OopsResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Output message from oops.
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Exit code from the oops process.
    /// </summary>
    public int ExitCode { get; init; }
}
