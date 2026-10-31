namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Identifies the connection-scoped startup loading lifecycle.
/// </summary>
public enum TrackerStartupPreparationStatus
{
    /// <summary>
    /// No loading attempt has been observed in this connection.
    /// </summary>
    Idle = 0,

    /// <summary>
    /// The game is preparing the loaded save.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The loaded save is ready for gameplay.
    /// </summary>
    Complete = 2,

    /// <summary>
    /// Loading failed before gameplay resumed.
    /// </summary>
    Failed = 3
}

/// <summary>
/// Represents one immutable view of game startup preparation.
/// </summary>
/// <remarks>
/// Initializes the identity, lifecycle and stage without a run-scoped progress counter.
/// </remarks>
/// <param name="PreparationId">The loading identity, or null while idle.</param>
/// <param name="Status">The current startup lifecycle.</param>
/// <param name="Phase">The game-owned stage identifier.</param>
/// <param name="Error">The optional terminal error.</param>
public sealed record TrackerStartupPreparationSnapshot(string? PreparationId, TrackerStartupPreparationStatus Status, string Phase, string? Error)
{
    /// <summary>
    /// Gets the shared empty connection-scoped state.
    /// </summary>
    public static TrackerStartupPreparationSnapshot Idle { get; } = new(null, TrackerStartupPreparationStatus.Idle, string.Empty, null);
}

/// <summary>
/// Publishes startup stage changes while rejecting retired loading identities and late terminal events.
/// </summary>
public sealed class TrackerStartupPreparationState
{
    private const int _maximumIdentityLength = 128;
    private const int _maximumPhaseLength = 64;
    private const int _maximumErrorLength = 2000;
    private readonly Lock _sync = new();
    private readonly HashSet<string> _seenIdentities = new(StringComparer.Ordinal);
    private TrackerStartupPreparationSnapshot _snapshot = TrackerStartupPreparationSnapshot.Idle;

    /// <summary>
    /// Occurs after an accepted stage change or connection reset.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the latest immutable snapshot, including the shared idle state.
    /// </summary>
    public TrackerStartupPreparationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    /// <summary>
    /// Applies a valid startup stage without borrowing saved run identity or game event sequence numbers.
    /// </summary>
    /// <param name="payload">The stage notification from the currently connected game.</param>
    /// <returns>Whether this notification changed the published snapshot.</returns>
    public bool Observe(StartupPreparationProgressPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(payload.PreparationId) || payload.PreparationId.Length > _maximumIdentityLength
            || payload.Phase is null || payload.Phase.Length > _maximumPhaseLength
            || payload.Error?.Length > _maximumErrorLength)
        {
            return false;
        }

        TrackerStartupPreparationStatus status = payload.Status switch
        {
            StartupPreparationProgressPayload.RunningStatus => TrackerStartupPreparationStatus.Running,
            StartupPreparationProgressPayload.CompleteStatus => TrackerStartupPreparationStatus.Complete,
            StartupPreparationProgressPayload.FailedStatus => TrackerStartupPreparationStatus.Failed,
            _ => TrackerStartupPreparationStatus.Idle
        };
        if (status == TrackerStartupPreparationStatus.Idle)
            return false;

        lock (_sync)
        {
            bool current = string.Equals(payload.PreparationId, _snapshot.PreparationId, StringComparison.Ordinal);
            if (current && _snapshot.Status != TrackerStartupPreparationStatus.Running)
                return false;

            if (!current && (status != TrackerStartupPreparationStatus.Running || !_seenIdentities.Add(payload.PreparationId)))
                return false;

            TrackerStartupPreparationSnapshot snapshot = new(payload.PreparationId, status, payload.Phase, status == TrackerStartupPreparationStatus.Failed ? payload.Error : null);
            if (_snapshot == snapshot)
                return false;

            _snapshot = snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Returns to the tracker after a failed loading attempt while keeping its identity retired.
    /// </summary>
    /// <returns>Whether a visible failed attempt was dismissed; running and idle attempts remain unchanged.</returns>
    public bool DismissFailure()
    {
        lock (_sync)
        {
            if (_snapshot.Status != TrackerStartupPreparationStatus.Failed)
                return false;

            _snapshot = TrackerStartupPreparationSnapshot.Idle;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Clears stage and retired identities when the game connection or session ends.
    /// </summary>
    public void Reset()
    {
        bool changed;
        lock (_sync)
        {
            changed = _snapshot != TrackerStartupPreparationSnapshot.Idle;
            _snapshot = TrackerStartupPreparationSnapshot.Idle;
            _seenIdentities.Clear();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
