namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Describes one independently requested section of live Pokemon diagnostics.
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
    /// Gets or initializes the populated inspector section.
    /// </summary>
    public PokemonLookupSection Section { get; init; }

    /// <summary>
    /// Gets or initializes live identity and current-state information.
    /// </summary>
    public required DebugPokemonIdentitySnapshot Identity { get; init; }

    /// <summary>
    /// Gets or initializes live ability-slot diagnostics when requested.
    /// </summary>
    public DebugPokemonAbilitiesSnapshot? Abilities { get; init; }

    /// <summary>
    /// Gets or initializes live base-stat diagnostics when requested.
    /// </summary>
    public DebugPokemonStatsSnapshot? Stats { get; init; }
}

/// <summary>
/// Describes identity and current state shared by every live inspector section.
/// </summary>
public sealed class DebugPokemonIdentitySnapshot
{
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
}

/// <summary>
/// Describes live ability-slot information for the Abilities page.
/// </summary>
public sealed class DebugPokemonAbilitiesSnapshot
{
    /// <summary>
    /// Gets or initializes the active ability generator metadata.
    /// </summary>
    public DebugAbilityGeneratorSnapshot Generator { get; init; } = new() { PoolFingerprint = string.Empty };

    /// <summary>
    /// Gets or initializes every ability row exposed by the live inspector.
    /// </summary>
    public IReadOnlyList<DebugAbilitySlotSnapshot> Slots { get; init; } = [];
}

/// <summary>
/// Describes live original and generated base stats for the Stats page.
/// </summary>
public sealed class DebugPokemonStatsSnapshot
{
    /// <summary>
    /// Gets or initializes all six final original base stats.
    /// </summary>
    public BaseStatsSnapshot Original { get; init; } = new();

    /// <summary>
    /// Gets or initializes the final original base-stat total.
    /// </summary>
    public int OriginalTotal { get; init; }

    /// <summary>
    /// Gets or initializes all six final generated base stats.
    /// </summary>
    public BaseStatsSnapshot Generated { get; init; } = new();

    /// <summary>
    /// Gets or initializes the final generated base-stat total.
    /// </summary>
    public int GeneratedTotal { get; init; }

    /// <summary>
    /// Gets or initializes the active base-stat generator metadata.
    /// </summary>
    public DebugBaseStatGeneratorSnapshot Generator { get; init; } = new();
}
