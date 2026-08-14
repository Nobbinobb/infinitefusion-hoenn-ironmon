namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Publishes thread-safe connection snapshots to tracker interface consumers.
/// </summary>
public sealed class TrackerConnectionState
{
    private readonly Lock _sync = new();
    private TrackerConnectionSnapshot _snapshot = new(TrackerConnectionStatus.Stopped, null, null, null);

    /// <summary>
    /// Initializes the shared tracker connection state.
    /// </summary>
    public TrackerConnectionState()
    {
    }

    /// <summary>
    /// Occurs after the connection snapshot changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the latest immutable connection snapshot.
    /// </summary>
    public TrackerConnectionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    /// <summary>
    /// Publishes a connection snapshot.
    /// </summary>
    /// <param name="status">The connection lifecycle state.</param>
    /// <param name="game">The connected game's handshake.</param>
    /// <param name="currentState">The recovered game state.</param>
    /// <param name="lastError">The most recent connection error.</param>
    internal void Publish(TrackerConnectionStatus status, GameHandshakePayload? game = null, GameCurrentStatePayload? currentState = null, string? lastError = null)
    {
        lock (_sync)
            _snapshot = new TrackerConnectionSnapshot(status, game, currentState, lastError);

        Changed?.Invoke(this, EventArgs.Empty);
    }

}
