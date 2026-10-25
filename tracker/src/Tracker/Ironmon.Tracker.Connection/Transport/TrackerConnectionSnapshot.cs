namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Represents an immutable view of the current local game connection.
/// </summary>
/// <remarks>
/// Initializes a tracker connection snapshot.
/// </remarks>
/// <param name="status">The connection lifecycle state.</param>
/// <param name="game">The connected game's handshake.</param>
/// <param name="currentState">The most recently recovered game state.</param>
/// <param name="lastError">The most recent connection error.</param>
public sealed class TrackerConnectionSnapshot(TrackerConnectionStatus status, GameHandshakePayload? game, GameCurrentStatePayload? currentState, string? lastError)
{
    /// <summary>
    /// Gets the connection lifecycle state.
    /// </summary>
    public TrackerConnectionStatus Status { get; } = status;

    /// <summary>
    /// Gets the connected game's handshake when available.
    /// </summary>
    public GameHandshakePayload? Game { get; } = game;

    /// <summary>
    /// Gets the most recently recovered game state when available.
    /// </summary>
    public GameCurrentStatePayload? CurrentState { get; } = currentState;

    /// <summary>
    /// Gets the most recent connection error when one exists.
    /// </summary>
    public string? LastError { get; } = lastError;
}
