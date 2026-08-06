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
        Publish(state.Battle, state.Player, state.Enemies);
    }

    /// <summary>
    /// Starts tracking a battle while preserving the last initialized player Pokemon.
    /// </summary>
    /// <param name="battle">The newly active battle.</param>
    internal void StartBattle(BattleSnapshot battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        Publish(battle, Snapshot.Player, []);
    }

    /// <summary>
    /// Ends the active battle while preserving the last initialized player Pokemon.
    /// </summary>
    internal void EndBattle() => Publish(null, Snapshot.Player, []);

    /// <summary>
    /// Replaces the complete initialized player Pokemon snapshot.
    /// </summary>
    /// <param name="player">The latest player Pokemon snapshot.</param>
    internal void UpdatePlayer(PlayerPokemonSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Publish(Snapshot.Battle, player, Snapshot.Enemies);
    }

    /// <summary>
    /// Replaces one active opposing position with its latest legal snapshot.
    /// </summary>
    /// <param name="enemy">The latest opposing Pokemon snapshot.</param>
    internal void UpdateEnemy(EnemyPokemonSnapshot enemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        List<EnemyPokemonSnapshot> enemies = [.. Snapshot.Enemies.Where(candidate => candidate.Position != enemy.Position), enemy];
        Publish(Snapshot.Battle, Snapshot.Player, enemies.OrderBy(candidate => candidate.Position).ToArray());
    }

    /// <summary>
    /// Publishes a run-state snapshot.
    /// </summary>
    /// <param name="battle">The active battle.</param>
    /// <param name="player">The initialized player Pokemon.</param>
    /// <param name="enemies">The active opposing Pokemon.</param>
    private void Publish(BattleSnapshot? battle, PlayerPokemonSnapshot? player, IReadOnlyList<EnemyPokemonSnapshot> enemies)
    {
        lock (_sync)
            _snapshot = new TrackerRunStateSnapshot(battle, player, enemies);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
