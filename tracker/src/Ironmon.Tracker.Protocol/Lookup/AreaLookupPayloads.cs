namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Identifies the content category requested from an area group.
/// </summary>
public enum AreaContentCategory
{
    /// <summary>
    /// Trainer battles in the area.
    /// </summary>
    Trainer = 0,

    /// <summary>
    /// Authored wild encounter slots in the area.
    /// </summary>
    Encounter = 1,

    /// <summary>
    /// Visible and hidden ground pickups in the area.
    /// </summary>
    Item = 2
}

/// <summary>
/// Requests compact area summaries for the active or one completed run.
/// </summary>
public sealed class AreaLookupSummaryRequestPayload
{
    /// <summary>
    /// Initializes an empty area-summary request for protocol serialization.
    /// </summary>
    public AreaLookupSummaryRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the completed-run recipe, or null for the active run.
    /// </summary>
    public CompletedRunRecipePayload? Recipe { get; init; }

    /// <summary>
    /// Gets or initializes the selected content category.
    /// </summary>
    public AreaContentCategory Category { get; init; }
}

/// <summary>
/// Returns compact progress and total counts for every public area.
/// </summary>
public sealed class AreaLookupSummaryResponsePayload
{
    /// <summary>
    /// Initializes an empty area-summary response for protocol serialization.
    /// </summary>
    public AreaLookupSummaryResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the catalog schema version.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets or initializes the run's progress revision.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Gets or initializes the public area summaries.
    /// </summary>
    public IReadOnlyList<AreaSummaryPayload> Areas { get; init; } = [];
}

/// <summary>
/// Describes compact totals and completion counts for one area.
/// </summary>
public sealed class AreaSummaryPayload
{
    /// <summary>
    /// Initializes an empty area summary for protocol serialization.
    /// </summary>
    public AreaSummaryPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable logical area identifier.
    /// </summary>
    public required string AreaId { get; init; }

    /// <summary>
    /// Gets or initializes the display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the physical map identifiers grouped into the area.
    /// </summary>
    public IReadOnlyList<int> MapIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the number of trainer entries.
    /// </summary>
    public int TrainerTotal { get; init; }

    /// <summary>
    /// Gets or initializes the number of defeated trainer entries.
    /// </summary>
    public int TrainerDefeated { get; init; }

    /// <summary>
    /// Gets or initializes the number of authored encounter slots.
    /// </summary>
    public int EncounterTotal { get; init; }

    /// <summary>
    /// Gets or initializes the number of encountered authored slots.
    /// </summary>
    public int Encountered { get; init; }

    /// <summary>
    /// Gets or initializes the number of ground pickups.
    /// </summary>
    public int ItemTotal { get; init; }

    /// <summary>
    /// Gets or initializes the number of collected pickups.
    /// </summary>
    public int ItemsCollected { get; init; }
}

/// <summary>
/// Requests one category's lazily loaded entries for an area.
/// </summary>
public sealed class AreaLookupDetailRequestPayload
{
    /// <summary>
    /// Initializes an empty area-detail request for protocol serialization.
    /// </summary>
    public AreaLookupDetailRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable logical area identifier.
    /// </summary>
    public required string AreaId { get; init; }

    /// <summary>
    /// Gets or initializes the requested content category.
    /// </summary>
    public AreaContentCategory Category { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run recipe, or null for the active run.
    /// </summary>
    public CompletedRunRecipePayload? Recipe { get; init; }

    /// <summary>
    /// Gets or initializes the tracker-owned discovery keys for the requested area and category.
    /// </summary>
    public IReadOnlyList<string> DiscoveryKeys { get; init; } = [];
}

/// <summary>
/// Returns one lazily loaded category for an area.
/// </summary>
public sealed class AreaLookupDetailResponsePayload
{
    /// <summary>
    /// Initializes an empty area-detail response for protocol serialization.
    /// </summary>
    public AreaLookupDetailResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable logical area identifier.
    /// </summary>
    public required string AreaId { get; init; }

    /// <summary>
    /// Gets or initializes the display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the returned category.
    /// </summary>
    public AreaContentCategory Category { get; init; }

    /// <summary>
    /// Gets or initializes the progress revision represented by the response.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Gets or initializes trainer entries when the trainer category was requested.
    /// </summary>
    public IReadOnlyList<AreaTrainerEntryPayload> Trainers { get; init; } = [];

    /// <summary>
    /// Gets or initializes encounter entries when the encounter category was requested.
    /// </summary>
    public IReadOnlyList<AreaEncounterEntryPayload> Encounters { get; init; } = [];

    /// <summary>
    /// Gets or initializes item entries when the item category was requested.
    /// </summary>
    public IReadOnlyList<AreaItemEntryPayload> Items { get; init; } = [];
}

/// <summary>
/// Describes one trainer battle and its disclosure state.
/// </summary>
public sealed class AreaTrainerEntryPayload
{
    /// <summary>
    /// Initializes an empty trainer entry for protocol serialization.
    /// </summary>
    public AreaTrainerEntryPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable entry identifier.
    /// </summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Gets or initializes the physical map identifier.
    /// </summary>
    public int MapId { get; init; }

    /// <summary>
    /// Gets or initializes the trainer class display name.
    /// </summary>
    public required string TrainerType { get; init; }

    /// <summary>
    /// Gets or initializes the trainer name.
    /// </summary>
    public required string TrainerName { get; init; }

    /// <summary>
    /// Gets or initializes the number of Pokemon in the party.
    /// </summary>
    public int PartySize { get; init; }

    /// <summary>
    /// Gets or initializes whether the trainer was defeated.
    /// </summary>
    public bool Defeated { get; init; }

    /// <summary>
    /// Gets or initializes whether generated party identities are disclosed.
    /// </summary>
    public bool DetailsRevealed { get; init; }

    /// <summary>
    /// Gets or initializes the disclosed generated party.
    /// </summary>
    public IReadOnlyList<AreaTrainerPokemonPayload> Party { get; init; } = [];
}

/// <summary>
/// Describes one disclosed generated trainer Pokemon.
/// </summary>
public sealed class AreaTrainerPokemonPayload
{
    /// <summary>
    /// Initializes an empty trainer Pokemon for protocol serialization.
    /// </summary>
    public AreaTrainerPokemonPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the one-based party slot.
    /// </summary>
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the generated species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the generated species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the Pokemon level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the game-relative local sprite path.
    /// </summary>
    public string? SpritePath { get; init; }
}

/// <summary>
/// Describes one authored encounter slot and its disclosure state.
/// </summary>
public sealed class AreaEncounterEntryPayload
{
    /// <summary>
    /// Initializes an empty encounter entry for protocol serialization.
    /// </summary>
    public AreaEncounterEntryPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable entry identifier.
    /// </summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Gets or initializes the physical map identifier.
    /// </summary>
    public int MapId { get; init; }

    /// <summary>
    /// Gets or initializes the encounter data version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the encounter method or condition.
    /// </summary>
    public required string EncounterType { get; init; }

    /// <summary>
    /// Gets or initializes the one-based authored slot.
    /// </summary>
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the slot's probability within its encounter table.
    /// </summary>
    public double ProbabilityPercent { get; init; }

    /// <summary>
    /// Gets or initializes the minimum encounter level.
    /// </summary>
    public int MinimumLevel { get; init; }

    /// <summary>
    /// Gets or initializes the maximum encounter level.
    /// </summary>
    public int MaximumLevel { get; init; }

    /// <summary>
    /// Gets or initializes whether this authored slot entered battle.
    /// </summary>
    public bool Encountered { get; init; }

    /// <summary>
    /// Gets or initializes whether generated species identity is disclosed.
    /// </summary>
    public bool DetailsRevealed { get; init; }

    /// <summary>
    /// Gets or initializes the generated species identifier when disclosed.
    /// </summary>
    public string? SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the generated species name when disclosed.
    /// </summary>
    public string? SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the game-relative sprite path when disclosed.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes whether a fused result belongs independently to this authored slot.
    /// </summary>
    public bool IndependentFusion { get; init; }
}

/// <summary>
/// Describes one visible or hidden ground pickup and its disclosure state.
/// </summary>
public sealed class AreaItemEntryPayload
{
    /// <summary>
    /// Initializes an empty item entry for protocol serialization.
    /// </summary>
    public AreaItemEntryPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable entry identifier.
    /// </summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Gets or initializes the physical map identifier.
    /// </summary>
    public int MapId { get; init; }

    /// <summary>
    /// Gets or initializes the event's map X coordinate.
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Gets or initializes the event's map Y coordinate.
    /// </summary>
    public int Y { get; init; }

    /// <summary>
    /// Gets or initializes the pickup classification.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets or initializes whether the base game hid this pickup.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// Gets or initializes whether the pickup was collected.
    /// </summary>
    public bool Collected { get; init; }

    /// <summary>
    /// Gets or initializes whether item identity is disclosed.
    /// </summary>
    public bool DetailsRevealed { get; init; }

    /// <summary>
    /// Gets or initializes possible or received item identities when disclosed.
    /// </summary>
    public IReadOnlyList<AreaItemIdentityPayload> Items { get; init; } = [];
}

/// <summary>
/// Describes one disclosed item identity.
/// </summary>
public sealed class AreaItemIdentityPayload
{
    /// <summary>
    /// Initializes an empty item identity for protocol serialization.
    /// </summary>
    public AreaItemIdentityPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable item identifier.
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// Gets or initializes the localized item name.
    /// </summary>
    public required string ItemName { get; init; }
}
