namespace Ironmon.Tracker.Connection;

/// <summary>
/// Identifies the source and direction of one tracker diagnostic entry.
/// </summary>
public enum TrackerDiagnosticDirection
{
    /// <summary>
    /// Describes tracker connection lifecycle state.
    /// </summary>
    Lifecycle = 0,

    /// <summary>
    /// Describes a message received from the game.
    /// </summary>
    Incoming = 1,

    /// <summary>
    /// Describes a message sent to the game.
    /// </summary>
    Outgoing = 2
}
