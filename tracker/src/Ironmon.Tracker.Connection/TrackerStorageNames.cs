namespace Ironmon.Tracker.Connection;

/// <summary>
/// Defines stable tracker-owned directory and file names.
/// </summary>
public static class TrackerStorageNames
{
    /// <summary>
    /// Gets the application-data root directory name.
    /// </summary>
    public const string RootDirectory = "IronmonTracker";

    /// <summary>
    /// Gets the per-run storage directory name.
    /// </summary>
    public const string RunsDirectory = "runs";

    /// <summary>
    /// Gets the completed-run recipe filename.
    /// </summary>
    public const string RecipeFile = "recipe.json";

    /// <summary>
    /// Gets the diagnostics export directory name.
    /// </summary>
    public const string DiagnosticsDirectory = "diagnostics";

    /// <summary>
    /// Gets the JSON file extension used by tracker-owned documents.
    /// </summary>
    public const string JsonExtension = ".json";

    /// <summary>
    /// Gets the temporary-file extension used by atomic writes.
    /// </summary>
    public const string TemporaryExtension = ".tmp";

    /// <summary>
    /// Gets the diagnostic export filename prefix.
    /// </summary>
    public const string DiagnosticFilePrefix = "ironmon-diagnostic-";

    /// <summary>
    /// Gets the stable filename containing the most recently captured protocol error.
    /// </summary>
    public const string LatestProtocolErrorFile = "latest-protocol-error.json";

    /// <summary>
    /// Gets the timestamp format used in diagnostic export filenames.
    /// </summary>
    public const string DiagnosticTimestampFormat = "yyyyMMdd-HHmmss";
}
