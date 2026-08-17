namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Carries signature-verified inputs for a transactional seeded-run import.
/// </summary>
public sealed class SeededRunImportRequestPayload
{
    /// <summary>
    /// Gets or initializes the share token identity.
    /// </summary>
    public required string TokenId { get; init; }

    /// <summary>
    /// Gets or initializes the imported run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the required Infinite Fusion version.
    /// </summary>
    public required string GameVersion { get; init; }

    /// <summary>
    /// Gets or initializes the required Ironmon version.
    /// </summary>
    public required string IronmonVersion { get; init; }

    /// <summary>
    /// Gets or initializes the required game-data mode.
    /// </summary>
    public required string DataMode { get; init; }

    /// <summary>
    /// Gets or initializes the complete imported configuration.
    /// </summary>
    public required RunConfigurationPayload Configuration { get; init; }

    /// <summary>
    /// Gets or initializes the required installation compatibility fingerprint.
    /// </summary>
    public required string CompatibilityFingerprint { get; init; }
}

/// <summary>
/// Reports one seeded-run import lifecycle transition.
/// </summary>
public sealed class SeededRunImportStatusPayload
{
    /// <summary>
    /// Gets or initializes the token identity associated with the transition.
    /// </summary>
    public required string TokenId { get; init; }

    /// <summary>
    /// Gets or initializes the lifecycle status.
    /// </summary>
    public SeededRunImportStatus Status { get; init; }

    /// <summary>
    /// Gets or initializes the bounded user-facing explanation.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Identifies one seeded-run import lifecycle transition.
/// </summary>
public enum SeededRunImportStatus
{
    /// <summary>
    /// The command passed initial validation.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The accepted import is waiting for a safe map-scene boundary.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// The imported run committed and started.
    /// </summary>
    Started = 2,

    /// <summary>
    /// Initial validation rejected the command without changing run state.
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// A queued import failed and restored the active attempt.
    /// </summary>
    Failed = 4
}
