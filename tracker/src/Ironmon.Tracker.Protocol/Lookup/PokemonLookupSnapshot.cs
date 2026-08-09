namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes complete deterministic generated information for one Pokemon.
/// </summary>
public sealed class PokemonLookupSnapshot
{
    /// <summary>
    /// Initializes an empty Pokemon lookup snapshot for protocol serialization.
    /// </summary>
    public PokemonLookupSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the resolved game-relative sprite path.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes the generated type identifiers.
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>
    /// Gets or initializes all six original base stats.
    /// </summary>
    public BaseStatsSnapshot OriginalBaseStats { get; init; } = new();

    /// <summary>
    /// Gets or initializes the original base-stat total.
    /// </summary>
    public int OriginalBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes all six generated base stats.
    /// </summary>
    public BaseStatsSnapshot BaseStats { get; init; } = new();

    /// <summary>
    /// Gets or initializes the generated base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes whether this run used generated base stats.
    /// </summary>
    public bool BaseStatsRandomized { get; init; }

    /// <summary>
    /// Gets or initializes every generated ability slot.
    /// </summary>
    public IReadOnlyList<AbilitySnapshot> Abilities { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete generated level-up learnset.
    /// </summary>
    public IReadOnlyList<ObservedMoveSnapshot> Learnset { get; init; } = [];

    /// <summary>
    /// Gets or initializes every generated move-access channel.
    /// </summary>
    public MoveAccessSnapshot MoveAccess { get; init; } = new();

    /// <summary>
    /// Gets or initializes possible evolution requirements without destination species.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> Evolutions { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current direct pre-evolution relationships.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> PreviousEvolutions { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated normal Pokemon which evolve directly into this Pokemon.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> EvolutionPredecessors { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations for a normal Pokemon.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> EvolutionTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations activated by the fusion head.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> HeadEvolutionTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations activated by the fusion body.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> BodyEvolutionTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes authored wild slots which generate this species.
    /// </summary>
    public IReadOnlyList<WildPokemonOccurrenceSnapshot> WildOccurrences { get; init; } = [];

    /// <summary>
    /// Gets or initializes authored trainer slots which generate this species.
    /// </summary>
    public IReadOnlyList<TrainerPokemonOccurrenceSnapshot> TrainerOccurrences { get; init; } = [];

    /// <summary>
    /// Gets or initializes the body and head species when this Pokemon is a fusion.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> FusionBases { get; init; } = [];

    /// <summary>
    /// Gets or initializes the deterministic Ironmon reverse when this Pokemon is a fusion.
    /// </summary>
    public PokemonRelationSnapshot? ReverseFusion { get; init; }

    /// <summary>
    /// Gets or initializes the normal material pairs that produce this Ironmon fusion.
    /// </summary>
    public IReadOnlyList<FusionMaterialPairSnapshot> FusionMaterials { get; init; } = [];
}
