namespace Ironmon.Tracker.Protocol;

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
    /// Gets or initializes all six generated base stats.
    /// </summary>
    public BaseStatsSnapshot BaseStats { get; init; } = new();

    /// <summary>
    /// Gets or initializes the generated base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes every generated ability slot.
    /// </summary>
    public IReadOnlyList<AbilitySnapshot> Abilities { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete generated level-up learnset.
    /// </summary>
    public IReadOnlyList<ObservedMoveSnapshot> Learnset { get; init; } = [];

    /// <summary>
    /// Gets or initializes possible evolution requirements without destination species.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> Evolutions { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current direct pre-evolution relationships.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> PreviousEvolutions { get; init; } = [];

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
