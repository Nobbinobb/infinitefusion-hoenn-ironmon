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
    /// Authored wild encounter slots and derived fusion possibilities in the area.
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
    /// Gets or initializes the active encounter mode used to count possibilities, or null for archives.
    /// </summary>
    public bool? OverworldEncounters { get; init; }

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
    /// Gets or initializes the number of authored slots and eligible derived fusion possibilities.
    /// </summary>
    public int EncounterTotal { get; init; }

    /// <summary>
    /// Gets or initializes the number of encountered authored slots and exact derived combinations.
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
    /// Gets or initializes whether the tracker can resolve requested fusion pairs using its shared native worker.
    /// </summary>
    public bool UseNativeFusionMapping { get; init; }

    /// <summary>
    /// Gets or initializes the native results for the previously requested, disclosed material pairs.
    /// </summary>
    public IReadOnlyList<AreaFusionResultPayload> FusionResults { get; init; } = [];

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

    /// <summary>
    /// Gets or initializes the encounter environment requested for paging, or null to request the environment index.
    /// </summary>
    public string? EncounterEnvironment { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based encounter-entry offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of encounter entries to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.AreaLookupPageSize;
}

/// <summary>
/// Returns one lazily loaded category for an area.
/// </summary>
public sealed class AreaLookupDetailResponsePayload
{
    /// <summary>
    /// Gets or initializes the actual active encounter mode used for this response, or null for historical recipes.
    /// </summary>
    public bool? OverworldEncounters { get; init; }

    /// <summary>
    /// Gets or initializes the disclosed material pairs needed to finish this page using native mapping.
    /// </summary>
    public IReadOnlyList<FusionMaterialAssignmentPayload> RequiredFusionMaterials { get; init; } = [];

    /// <summary>
    /// Gets or initializes whether encounter fusion preparation is still running and this page must be requested again.
    /// </summary>
    public bool Pending { get; init; }

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
    /// Gets or initializes the zero-based entry offset represented by the response.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum requested page size.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.AreaLookupPageSize;

    /// <summary>
    /// Gets or initializes the total number of entries in the selected category.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets or initializes the encounter environment represented by this page, or null for the environment index.
    /// </summary>
    public string? EncounterEnvironment { get; init; }

    /// <summary>
    /// Gets or initializes the encounter environments available in the selected area.
    /// </summary>
    public IReadOnlyList<AreaEncounterEnvironmentPayload> EncounterEnvironments { get; init; } = [];

    /// <summary>
    /// Gets or initializes trainer entries when the trainer category was requested.
    /// </summary>
    public IReadOnlyList<AreaTrainerEntryPayload> Trainers { get; init; } = [];

    /// <summary>
    /// Gets or initializes encounter entries when the encounter category was requested.
    /// </summary>
    public IReadOnlyList<AreaEncounterEntryPayload> Encounters { get; init; } = [];

    /// <summary>
    /// Gets or initializes possible fusions derived from two normal encounter slots.
    /// </summary>
    public IReadOnlyList<AreaEncounterFusionEntryPayload> EncounterFusions { get; init; } = [];

    /// <summary>
    /// Gets or initializes item entries when the item category was requested.
    /// </summary>
    public IReadOnlyList<AreaItemEntryPayload> Items { get; init; } = [];
}

/// <summary>
/// Carries one ordered material pair's deterministic native fusion result.
/// </summary>
public sealed class AreaFusionResultPayload
{
    /// <summary>
    /// Initializes an empty result for protocol serialization.
    /// </summary>
    public AreaFusionResultPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the normal Body material identifier.
    /// </summary>
    public int BodyId { get; init; }

    /// <summary>
    /// Gets or initializes the normal Head material identifier.
    /// </summary>
    public int HeadId { get; init; }

    /// <summary>
    /// Gets or initializes the generated custom fusion's numeric identifier.
    /// </summary>
    public int SpeciesNumber { get; init; }
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
    /// Gets or initializes the broad encounter environment.
    /// </summary>
    public string Environment { get; init; } = "special";

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
/// Describes one independently paged encounter environment in an area.
/// </summary>
public sealed class AreaEncounterEnvironmentPayload
{
    /// <summary>
    /// Initializes an empty encounter environment for protocol serialization.
    /// </summary>
    public AreaEncounterEnvironmentPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable environment key.
    /// </summary>
    public required string Key { get; init; }
}

/// <summary>
/// Describes one possible fusion derived from two normal encounter slots.
/// </summary>
public sealed class AreaEncounterFusionEntryPayload
{
    /// <summary>
    /// Initializes an empty derived fusion entry for protocol serialization.
    /// </summary>
    public AreaEncounterFusionEntryPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable possibility and discovery identifier.
    /// </summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Gets or initializes the mechanic that produced the fusion.
    /// </summary>
    public required string Origin { get; init; }

    /// <summary>
    /// Gets or initializes the primary source environment.
    /// </summary>
    public required string Environment { get; init; }

    /// <summary>
    /// Gets or initializes whether the source encounter tables differ.
    /// </summary>
    public bool CrossEnvironment { get; init; }

    /// <summary>
    /// Gets or initializes whether the generated fusion identity may be displayed.
    /// </summary>
    public bool DetailsRevealed { get; init; }

    /// <summary>
    /// Gets or initializes whether this exact derived fusion has entered battle.
    /// </summary>
    public bool Encountered { get; init; }

    /// <summary>
    /// Gets or initializes the first source encounter method or condition.
    /// </summary>
    public required string FirstEncounterType { get; init; }

    /// <summary>
    /// Gets or initializes the first one-based authored slot.
    /// </summary>
    public int FirstSlot { get; init; }

    /// <summary>
    /// Gets or initializes the second source encounter method or condition.
    /// </summary>
    public required string SecondEncounterType { get; init; }

    /// <summary>
    /// Gets or initializes the second one-based authored slot.
    /// </summary>
    public int SecondSlot { get; init; }

    /// <summary>
    /// Gets or initializes the fusion roll percentage after two eligible normal sources are available.
    /// </summary>
    public double FusionChancePercent { get; init; }

    /// <summary>
    /// Gets or initializes the minimum resulting level.
    /// </summary>
    public int MinimumLevel { get; init; }

    /// <summary>
    /// Gets or initializes the maximum resulting level.
    /// </summary>
    public int MaximumLevel { get; init; }

    /// <summary>
    /// Gets or initializes the generated fusion identifier when its details are revealed.
    /// </summary>
    public string? SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the generated fusion name when its details are revealed.
    /// </summary>
    public string? SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the game-relative local sprite path.
    /// </summary>
    public string? SpritePath { get; init; }
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
