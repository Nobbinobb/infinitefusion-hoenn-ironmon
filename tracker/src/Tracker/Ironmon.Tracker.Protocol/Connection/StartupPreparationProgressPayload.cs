namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes a startup loading stage independently of saved run identity and sequence numbers.
/// </summary>
public sealed class StartupPreparationProgressPayload
{
    /// <summary>
    /// Identifies an active startup preparation.
    /// </summary>
    public const string RunningStatus = "running";

    /// <summary>
    /// Identifies successfully completed startup preparation.
    /// </summary>
    public const string CompleteStatus = "complete";

    /// <summary>
    /// Identifies startup preparation that failed.
    /// </summary>
    public const string FailedStatus = "failed";

    /// <summary>
    /// Gets or initializes the unique identity of this loading attempt.
    /// </summary>
    public string PreparationId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes running, complete, or failed status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current game-owned stage identifier.
    /// </summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional failure message.
    /// </summary>
    public string? Error { get; init; }
}
