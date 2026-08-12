namespace Ironmon.Tracker.Connection.RunState;

/// <summary>
/// Represents the live battle and player data currently held by the tracker.
/// </summary>
/// <remarks>
/// Initializes a tracker run-state snapshot.
/// </remarks>
/// <param name="battle">The active battle when one exists.</param>
/// <param name="player">The initialized player Pokemon when one exists.</param>
/// <param name="enemies">The active legally visible opposing Pokemon.</param>
/// <param name="moveMenuPokemonId">The player Pokémon whose move menu most recently opened.</param>
/// <param name="starterSelection">The active starter-selection view when one exists.</param>
public sealed class TrackerRunStateSnapshot(BattleSnapshot? battle, PlayerPokemonSnapshot? player, IReadOnlyList<EnemyPokemonSnapshot>? enemies = null, string? moveMenuPokemonId = null, StarterSelectionSnapshot? starterSelection = null)
{
    /// <summary>
    /// Gets the active battle when one exists.
    /// </summary>
    public BattleSnapshot? Battle { get; } = battle;

    /// <summary>
    /// Gets the initialized player Pokemon when one exists.
    /// </summary>
    public PlayerPokemonSnapshot? Player { get; } = player;

    /// <summary>
    /// Gets the active legally visible opposing Pokemon.
    /// </summary>
    public IReadOnlyList<EnemyPokemonSnapshot> Enemies { get; } = enemies ?? [];

    /// <summary>
    /// Gets the player Pokémon whose move menu most recently opened.
    /// </summary>
    public string? MoveMenuPokemonId { get; } = moveMenuPokemonId;

    /// <summary>
    /// Gets the active starter-selection view when one exists.
    /// </summary>
    public StarterSelectionSnapshot? StarterSelection { get; } = starterSelection;
}
