namespace Ironmon.Tracker.Connection.Access;

/// <summary>
/// Identifies the tracker-owned diagnostic-access lifecycle state.
/// </summary>
public enum DiagnosticAccessState
{
    /// <summary>
    /// No persisted diagnostic access exists.
    /// </summary>
    None = 0,

    /// <summary>
    /// A signed token currently grants diagnostic capabilities.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The persisted token has expired.
    /// </summary>
    Expired = 2,

    /// <summary>
    /// The persisted token cannot be trusted or read.
    /// </summary>
    Invalid = 3,

    /// <summary>
    /// A Debug tracker build grants every supported capability locally.
    /// </summary>
    DeveloperOverride = 4
}
