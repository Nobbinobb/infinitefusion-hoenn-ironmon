using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Represents an immutable view of the current local game connection.
/// </summary>
public sealed class TrackerConnectionSnapshot
{
    /// <summary>
    /// Initializes a tracker connection snapshot.
    /// </summary>
    /// <param name="status">The connection lifecycle state.</param>
    /// <param name="game">The connected game's handshake.</param>
    /// <param name="currentState">The most recently recovered game state.</param>
    /// <param name="lastError">The most recent connection error.</param>
    public TrackerConnectionSnapshot(
        TrackerConnectionStatus status,
        GameHandshakePayload? game,
        GameCurrentStatePayload? currentState,
        string? lastError)
    {
        Status = status;
        Game = game;
        CurrentState = currentState;
        LastError = lastError;
    }

    /// <summary>
    /// Gets the connection lifecycle state.
    /// </summary>
    public TrackerConnectionStatus Status { get; }

    /// <summary>
    /// Gets the connected game's handshake when available.
    /// </summary>
    public GameHandshakePayload? Game { get; }

    /// <summary>
    /// Gets the most recently recovered game state when available.
    /// </summary>
    public GameCurrentStatePayload? CurrentState { get; }

    /// <summary>
    /// Gets the most recent connection error when one exists.
    /// </summary>
    public string? LastError { get; }
}
