namespace Ironmon.Tracker.Connection;

/// <summary>
/// Identifies the current tracker connection lifecycle state.
/// </summary>
public enum TrackerConnectionStatus
{
    /// <summary>
    /// Indicates that the tracker is listening for the game.
    /// </summary>
    Waiting = 0,

    /// <summary>
    /// Indicates that a game connection is exchanging its handshake.
    /// </summary>
    Handshaking = 1,

    /// <summary>
    /// Indicates that the game completed the tracker handshake.
    /// </summary>
    Connected = 2,

    /// <summary>
    /// Indicates that the most recent connection failed protocol validation.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Indicates that the tracker listener has stopped.
    /// </summary>
    Stopped = 4
}
