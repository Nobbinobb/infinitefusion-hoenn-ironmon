using Ironmon.Tracker.Protocol.Debug;

namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one independently requested section of deterministic Pokemon information.
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
    /// Gets or initializes the represented Pokemon identity.
    /// </summary>
    public required PokemonLookupIdentitySnapshot Identity { get; init; }

    /// <summary>
    /// Gets or initializes the populated information section.
    /// </summary>
    public PokemonLookupSection Section { get; init; }

    /// <summary>
    /// Gets or initializes Overview information when requested.
    /// </summary>
    public PokemonLookupOverviewSnapshot? Overview { get; init; }

    /// <summary>
    /// Gets or initializes Abilities information when requested.
    /// </summary>
    public PokemonLookupAbilitiesSnapshot? Abilities { get; init; }

    /// <summary>
    /// Gets or initializes Stats information when requested.
    /// </summary>
    public PokemonLookupStatsSnapshot? Stats { get; init; }

    /// <summary>
    /// Gets or initializes Moves information when requested.
    /// </summary>
    public PokemonLookupMovesSnapshot? Moves { get; init; }

    /// <summary>
    /// Gets or initializes Evolutions information when requested.
    /// </summary>
    public PokemonLookupEvolutionsSnapshot? Evolutions { get; init; }
}

/// <summary>
/// Describes identity shared by every lookup section.
/// </summary>
public sealed class PokemonLookupIdentitySnapshot
{
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
    /// Gets or initializes whether the species is a fusion.
    /// </summary>
    public bool Fusion { get; init; }

    /// <summary>
    /// Gets or initializes the represented Pokemon's run-specific obtainability proof state.
    /// </summary>
    public PokemonObtainabilitySnapshot Obtainability { get; init; } = new();
}

/// <summary>
/// Describes authored occurrences and fusion relationships on the Overview page.
/// </summary>
public sealed class PokemonLookupOverviewSnapshot
{
    /// <summary>
    /// Gets or initializes authored wild slots which generate this species.
    /// </summary>
    public WildOccurrenceSearchResponsePayload WildOccurrences { get; init; } = new();

    /// <summary>
    /// Gets or initializes authored trainer slots which generate this species.
    /// </summary>
    public TrainerOccurrenceSearchResponsePayload TrainerOccurrences { get; init; } = new();

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
    public FusionMaterialSearchResponsePayload FusionMaterials { get; init; } = new();
}

/// <summary>
/// Describes generated abilities and their diagnostics.
/// </summary>
public sealed class PokemonLookupAbilitiesSnapshot
{
    /// <summary>
    /// Gets or initializes every generated ability slot.
    /// </summary>
    public IReadOnlyList<AbilitySnapshot> Values { get; init; } = [];

    /// <summary>
    /// Gets or initializes reconstructed original, generated, component, and final fusion slot diagnostics.
    /// </summary>
    public IReadOnlyList<DebugAbilitySlotSnapshot> Slots { get; init; } = [];

    /// <summary>
    /// Gets or initializes ability-generator diagnostics.
    /// </summary>
    public GeneratorDiagnosticsSnapshot Generator { get; init; } = new();
}

/// <summary>
/// Describes original and generated base stats and their diagnostics.
/// </summary>
public sealed class PokemonLookupStatsSnapshot
{
    /// <summary>
    /// Gets or initializes all six original base stats.
    /// </summary>
    public BaseStatsSnapshot Original { get; init; } = new();

    /// <summary>
    /// Gets or initializes the original base-stat total.
    /// </summary>
    public int OriginalTotal { get; init; }

    /// <summary>
    /// Gets or initializes all six generated base stats.
    /// </summary>
    public BaseStatsSnapshot Generated { get; init; } = new();

    /// <summary>
    /// Gets or initializes the generated base-stat total.
    /// </summary>
    public int GeneratedTotal { get; init; }

    /// <summary>
    /// Gets or initializes whether this run used generated base stats.
    /// </summary>
    public bool Randomized { get; init; }

    /// <summary>
    /// Gets or initializes base-stat-generator diagnostics.
    /// </summary>
    public GeneratorDiagnosticsSnapshot Generator { get; init; } = new();
}

/// <summary>
/// Describes generated move access and its diagnostics.
/// </summary>
public sealed class PokemonLookupMovesSnapshot
{
    /// <summary>
    /// Gets or initializes the complete generated level-up learnset.
    /// </summary>
    public IReadOnlyList<ObservedMoveSnapshot> Learnset { get; init; } = [];

    /// <summary>
    /// Gets or initializes every generated move-access channel.
    /// </summary>
    public MoveAccessSnapshot Access { get; init; } = new();

    /// <summary>
    /// Gets or initializes move-access-generator diagnostics.
    /// </summary>
    public GeneratorDiagnosticsSnapshot Generator { get; init; } = new();
}

/// <summary>
/// Describes authored and generated evolution relationships and diagnostics.
/// </summary>
public sealed class PokemonLookupEvolutionsSnapshot
{
    /// <summary>
    /// Gets or initializes the current Pokemon's stable graph row.
    /// </summary>
    public int CurrentStageLevel { get; init; }

    /// <summary>
    /// Gets or initializes the current Pokemon's generated base-stat total.
    /// </summary>
    public int CurrentBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes possible native evolution requirements without destination species.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> NativeTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current direct native pre-evolution relationships.
    /// </summary>
    public IReadOnlyList<PokemonRelationSnapshot> NativePredecessors { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated Pokemon which evolve directly into this Pokemon.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> GeneratedPredecessors { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations for a normal Pokemon.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> GeneratedTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations activated by the fusion head.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> HeadTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations activated by the fusion body.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> BodyTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes evolution-generator diagnostics.
    /// </summary>
    public GeneratorDiagnosticsSnapshot Generator { get; init; } = new();
}
