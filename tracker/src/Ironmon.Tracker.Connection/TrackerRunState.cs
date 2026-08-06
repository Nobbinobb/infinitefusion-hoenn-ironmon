using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Publishes thread-safe live run snapshots to tracker interface consumers.
/// </summary>
public sealed class TrackerRunState
{
    private readonly object _sync = new();
    private TrackerRunStateSnapshot _snapshot = new(null, null);

    /// <summary>
    /// Initializes the shared tracker run state.
    /// </summary>
    public TrackerRunState()
    {
    }

    /// <summary>
    /// Occurs after live run data changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the latest immutable run snapshot.
    /// </summary>
    public TrackerRunStateSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    /// <summary>
    /// Replaces live data with a recovered complete game state.
    /// </summary>
    /// <param name="state">The recovered complete state.</param>
    internal void Recover(GameCurrentStatePayload state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Publish(state.Battle, state.Player);
    }

    /// <summary>
    /// Starts tracking a battle while preserving the last initialized player Pokemon.
    /// </summary>
    /// <param name="battle">The newly active battle.</param>
    internal void StartBattle(BattleSnapshot battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        Publish(battle, Snapshot.Player);
    }

    /// <summary>
    /// Ends the active battle while preserving the last initialized player Pokemon.
    /// </summary>
    internal void EndBattle() => Publish(null, Snapshot.Player);

    /// <summary>
    /// Replaces the complete initialized player Pokemon snapshot.
    /// </summary>
    /// <param name="player">The latest player Pokemon snapshot.</param>
    internal void UpdatePlayer(PlayerPokemonSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Publish(Snapshot.Battle, player);
    }

    /// <summary>
    /// Publishes a run-state snapshot.
    /// </summary>
    /// <param name="battle">The active battle.</param>
    /// <param name="player">The initialized player Pokemon.</param>
    private void Publish(BattleSnapshot? battle, PlayerPokemonSnapshot? player)
    {
        lock (_sync)
            _snapshot = new TrackerRunStateSnapshot(battle, player);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
