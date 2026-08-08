namespace Ironmon.Tracker.Connection.RunState;

/// <summary>
/// Publishes thread-safe live run snapshots to tracker interface consumers.
/// </summary>
public sealed class TrackerRunState
{
    private readonly Lock _sync = new();
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
        Publish(battle, Snapshot.Player, [], null);
    }

    /// <summary>
    /// Ends the active battle while preserving the last initialized player Pokemon.
    /// </summary>
    internal void EndBattle()
        => Publish(null, Snapshot.Player, [], null);

    /// <summary>
    /// Replaces the complete initialized player Pokemon snapshot.
    /// </summary>
    /// <param name="player">The latest player Pokemon snapshot.</param>
    internal void UpdatePlayer(PlayerPokemonSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        string? moveMenuPokemonId = Snapshot.Player?.PokemonId == player.PokemonId ? Snapshot.MoveMenuPokemonId : null;
        Publish(Snapshot.Battle, player, Snapshot.Enemies, moveMenuPokemonId);
    }

    /// <summary>
    /// Replaces one active opposing position with its latest legal snapshot.
    /// </summary>
    /// <param name="enemy">The latest opposing Pokemon snapshot.</param>
    internal void UpdateEnemy(EnemyPokemonSnapshot enemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        List<EnemyPokemonSnapshot> enemies = [.. Snapshot.Enemies.Where(candidate => candidate.Position != enemy.Position), enemy];
        Publish(Snapshot.Battle, Snapshot.Player, [.. enemies.OrderBy(candidate => candidate.Position)], Snapshot.MoveMenuPokemonId);
    }

    /// <summary>
    /// Records the first move-menu opening for the active player Pokémon.
    /// </summary>
    /// <param name="payload">The move-menu event payload.</param>
    internal void OpenPlayerMoveMenu(PlayerMoveMenuOpenedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Publish(Snapshot.Battle, Snapshot.Player, Snapshot.Enemies, payload.PokemonId);
    }

    /// <summary>
    /// Publishes a run-state snapshot.
    /// </summary>
    /// <param name="battle">The active battle.</param>
    /// <param name="player">The initialized player Pokemon.</param>
    /// <param name="enemies">The active opposing Pokemon.</param>
    /// <param name="moveMenuPokemonId">The player Pokémon whose move menu most recently opened.</param>
    private void Publish(BattleSnapshot? battle, PlayerPokemonSnapshot? player, IReadOnlyList<EnemyPokemonSnapshot> enemies, string? moveMenuPokemonId = null)
    {
        lock (_sync)
            _snapshot = new TrackerRunStateSnapshot(battle, player, enemies, moveMenuPokemonId);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
