namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes the complete legal tracker view of the active player Pokemon.
/// </summary>
public sealed class PlayerPokemonSnapshot
{
    /// <summary>
    /// Initializes an empty player Pokemon snapshot for protocol serialization.
    /// </summary>
    public PlayerPokemonSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable individual Pokemon identifier.
    /// </summary>
    public required string PokemonId { get; init; }

    /// <summary>
    /// Gets or initializes the stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the Pokemon nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the sprite path relative to the game root when available.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes the current level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the current HP.
    /// </summary>
    public int CurrentHp { get; init; }

    /// <summary>
    /// Gets or initializes the maximum HP.
    /// </summary>
    public int MaximumHp { get; init; }

    /// <summary>
    /// Gets or initializes the stable status identifier.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets or initializes whether the Pokemon is currently confused in battle.
    /// </summary>
    public bool Confused { get; init; }

    /// <summary>
    /// Gets or initializes the current type identifiers.
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>
    /// Gets or initializes the localized ability name.
    /// </summary>
    public required string Ability { get; init; }

    /// <summary>
    /// Gets or initializes the localized held-item name when one is held.
    /// </summary>
    public string? HeldItem { get; init; }

    /// <summary>
    /// Gets or initializes the calculated Attack stat.
    /// </summary>
    public int Attack { get; init; }

    /// <summary>
    /// Gets or initializes the calculated Defense stat.
    /// </summary>
    public int Defense { get; init; }

    /// <summary>
    /// Gets or initializes the calculated Special Attack stat.
    /// </summary>
    public int SpecialAttack { get; init; }

    /// <summary>
    /// Gets or initializes the calculated Special Defense stat.
    /// </summary>
    public int SpecialDefense { get; init; }

    /// <summary>
    /// Gets or initializes the calculated Speed stat.
    /// </summary>
    public int Speed { get; init; }

    /// <summary>
    /// Gets or initializes the current species base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the localized nature name when tracker rules allow it.
    /// </summary>
    public string? Nature { get; init; }

    /// <summary>
    /// Gets or initializes the four current moves in game order.
    /// </summary>
    public IReadOnlyList<PlayerMoveSnapshot> Moves { get; init; } = [];

    /// <summary>
    /// Gets or initializes current moves that legally reveal level-up learnset entries.
    /// </summary>
    public IReadOnlyList<ObservedMoveSnapshot> LevelUpMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current healing inventory summary.
    /// </summary>
    public required HealingInventorySnapshot Healing { get; init; }
}
