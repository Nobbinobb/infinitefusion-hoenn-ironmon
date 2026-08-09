namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Reproduces the game-owned Ironmon Pokemon inspector data for debug mode.
/// </summary>
public sealed class DebugPokemonInspectorSnapshot
{
    /// <summary>
    /// Initializes an empty debug Pokemon snapshot for protocol serialization.
    /// </summary>
    public DebugPokemonInspectorSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the individual Pokemon identifier.
    /// </summary>
    public required string PokemonId { get; init; }

    /// <summary>
    /// Gets or initializes the Pokemon nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// Gets or initializes the stable species identifier.
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
    /// Gets or initializes the current level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the localized gender label.
    /// </summary>
    public required string Gender { get; init; }

    /// <summary>
    /// Gets or initializes the held-item identifier.
    /// </summary>
    public string? HeldItemId { get; init; }

    /// <summary>
    /// Gets or initializes the localized held-item name.
    /// </summary>
    public string? HeldItemName { get; init; }

    /// <summary>
    /// Gets or initializes whether the inspected species is a fusion.
    /// </summary>
    public bool Fusion { get; init; }

    /// <summary>
    /// Gets or initializes the numeric form identifier.
    /// </summary>
    public int Form { get; init; }

    /// <summary>
    /// Gets or initializes the localized form name.
    /// </summary>
    public string? FormName { get; init; }

    /// <summary>
    /// Gets or initializes the displayed body component for a fusion.
    /// </summary>
    public DebugSpeciesSnapshot? Body { get; init; }

    /// <summary>
    /// Gets or initializes the displayed head component for a fusion.
    /// </summary>
    public DebugSpeciesSnapshot? Head { get; init; }

    /// <summary>
    /// Gets or initializes the raw active ability index stored by the Pokemon.
    /// </summary>
    public int ActiveAbilityIndex { get; init; }

    /// <summary>
    /// Gets or initializes the active ability slot label.
    /// </summary>
    public required string ActiveAbilitySlot { get; init; }

    /// <summary>
    /// Gets or initializes the active ability identifier.
    /// </summary>
    public required string ActiveAbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the localized active ability name.
    /// </summary>
    public required string ActiveAbilityName { get; init; }

    /// <summary>
    /// Gets or initializes the active ability generator metadata.
    /// </summary>
    public DebugAbilityGeneratorSnapshot Generator { get; init; } = new() { PoolFingerprint = string.Empty };

    /// <summary>
    /// Gets or initializes all six final original base stats.
    /// </summary>
    public BaseStatsSnapshot OriginalBaseStats { get; init; } = new();

    /// <summary>
    /// Gets or initializes the final original base-stat total.
    /// </summary>
    public int OriginalBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes all six final generated base stats.
    /// </summary>
    public BaseStatsSnapshot GeneratedBaseStats { get; init; } = new();

    /// <summary>
    /// Gets or initializes the final generated base-stat total.
    /// </summary>
    public int GeneratedBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the active base-stat generator metadata.
    /// </summary>
    public DebugBaseStatGeneratorSnapshot BaseStatGenerator { get; init; } = new();

    /// <summary>
    /// Gets or initializes generated normal Pokemon which evolve directly into the inspected Pokemon.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> EvolutionPredecessors { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations for an inspected normal Pokemon.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> EvolutionTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations activated by the inspected fusion's head.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> HeadEvolutionTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated destinations activated by the inspected fusion's body.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> BodyEvolutionTargets { get; init; } = [];

    /// <summary>
    /// Gets or initializes every ability row exposed by the in-game inspector.
    /// </summary>
    public IReadOnlyList<DebugAbilitySlotSnapshot> AbilitySlots { get; init; } = [];

    /// <summary>
    /// Gets or initializes every generated move-access channel.
    /// </summary>
    public MoveAccessSnapshot MoveAccess { get; init; } = new();
}
