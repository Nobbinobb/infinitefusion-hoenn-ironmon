namespace Ironmon.Tracker.Protocol.Live;

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
    /// Gets or initializes the persistent species identifier used for learnset ownership.
    /// This differs from <see cref="SpeciesId"/> while Transform is active.
    /// </summary>
    public string? OriginalSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the Pokemon nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes whether the game identifies the Pokemon as a fusion.
    /// Missing values from older game snapshots leave the marker hidden.
    /// </summary>
    public bool Fusion { get; init; }

    /// <summary>
    /// Gets or initializes whether the active battler is currently transformed.
    /// </summary>
    public bool Transformed { get; init; }

    /// <summary>
    /// Gets or initializes the stable gender identifier.
    /// </summary>
    public required string Gender { get; init; }

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
    /// Gets or initializes the privacy-filtered defensive overview.
    /// </summary>
    public DefenseOverviewSnapshot? DefensiveOverview { get; init; }

    /// <summary>
    /// Gets or initializes the localized ability name.
    /// </summary>
    public required string Ability { get; init; }

    /// <summary>
    /// Gets or initializes the player's legally known ability details.
    /// </summary>
    public AbilitySnapshot? AbilityDetails { get; init; }

    /// <summary>
    /// Gets or initializes the persistent ability used for species knowledge.
    /// This differs from <see cref="AbilityDetails"/> after a temporary battle ability change.
    /// </summary>
    public AbilitySnapshot? StoredAbilityDetails { get; init; }

    /// <summary>
    /// Gets or initializes the target ability copied when Transform activated.
    /// This remains the knowledge authority if the active ability later changes.
    /// </summary>
    public AbilitySnapshot? CopiedAbilityDetails { get; init; }

    /// <summary>
    /// Gets or initializes the localized held-item name when one is held.
    /// </summary>
    public string? HeldItem { get; init; }

    /// <summary>
    /// Gets the legally visible held item's localized description, when supplied by the game.
    /// </summary>
    public string? HeldItemDescription { get; init; }

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
    /// Gets or initializes the current in-battle stat stages.
    /// </summary>
    public BattleStatStagesSnapshot StatStages { get; init; } = new();

    /// <summary>
    /// Gets or initializes the current species base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the localized nature name when tracker rules allow it.
    /// </summary>
    public string? Nature { get; init; }

    /// <summary>
    /// Gets or initializes nature adjustments for the calculated stats.
    /// </summary>
    public NatureAdjustmentsSnapshot NatureAdjustments { get; init; } = new();

    /// <summary>
    /// Gets or initializes the four current moves in game order.
    /// </summary>
    public IReadOnlyList<PlayerMoveSnapshot> Moves { get; init; } = [];

    /// <summary>
    /// Gets or initializes the persistent party moves used for PP-item targeting.
    /// This differs from <see cref="Moves"/> while Transform is active.
    /// </summary>
    public IReadOnlyList<PlayerMoveSnapshot> StoredMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes current moves that legally reveal level-up learnset entries.
    /// </summary>
    public IReadOnlyList<ObservedMoveSnapshot> LevelUpMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes progress through the complete level-up learnset.
    /// </summary>
    public LearnsetProgressSnapshot LearnsetProgress { get; init; } = new();

    /// <summary>
    /// Gets or initializes possible evolution requirements in stable display order.
    /// </summary>
    public IReadOnlyList<EvolutionSnapshot> Evolutions { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current healing inventory summary.
    /// </summary>
    public required HealingInventorySnapshot Healing { get; init; }
}
