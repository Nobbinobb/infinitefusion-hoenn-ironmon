namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries move-access iteration data observed during one completed run.
/// </summary>
public sealed class MoveAccessMetricsPayload
{
    /// <summary>
    /// Gets or initializes the metrics schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes encountered species and fusion summaries.
    /// </summary>
    public IReadOnlyList<MoveAccessEncounterMetricPayload> Encounters { get; init; } = [];

    /// <summary>
    /// Gets or initializes obtained machine observations.
    /// </summary>
    public IReadOnlyList<MachineAcquisitionMetricPayload> MachineAcquisitions { get; init; } = [];

    /// <summary>
    /// Gets or initializes tutor interaction observations.
    /// </summary>
    public IReadOnlyList<TutorVisitMetricPayload> TutorVisits { get; init; } = [];

    /// <summary>
    /// Gets or initializes move acquisition observations.
    /// </summary>
    public IReadOnlyList<MoveAcquisitionMetricPayload> MoveAcquisitions { get; init; } = [];

    /// <summary>
    /// Gets or initializes aggregated move-use observations.
    /// </summary>
    public IReadOnlyList<MoveUseMetricPayload> MoveUses { get; init; } = [];
}

/// <summary>
/// Describes generated move access for one species, level, and encountered side.
/// </summary>
public sealed class MoveAccessEncounterMetricPayload
{
    /// <summary>
    /// Gets or initializes the species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the form number.
    /// </summary>
    public int Form { get; init; }

    /// <summary>
    /// Gets or initializes the encountered level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes whether the Pokemon was encountered on the player or enemy side.
    /// </summary>
    public required string Side { get; init; }

    /// <summary>
    /// Gets or initializes how often this species, level, and side combination was encountered.
    /// </summary>
    public int EncounterCount { get; init; }

    /// <summary>
    /// Gets or initializes whether the entry is a fusion.
    /// </summary>
    public bool IsFusion { get; init; }

    /// <summary>
    /// Gets or initializes metrics for each access channel.
    /// </summary>
    public IReadOnlyList<MoveAccessChannelMetricPayload> Channels { get; init; } = [];

    /// <summary>
    /// Gets or initializes the number of distinct moves across all channels.
    /// </summary>
    public int UniqueMoveCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of moves present in more than one channel.
    /// </summary>
    public int CrossChannelOverlapCount { get; init; }

    /// <summary>
    /// Gets or initializes the earliest level containing a positive-power move.
    /// </summary>
    public int? EarliestDamagingMoveLevel { get; init; }

    /// <summary>
    /// Gets or initializes the number of entries available by level 1.
    /// </summary>
    public int LevelOneMoveCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of damaging moves in the effective level-1 starting four.
    /// </summary>
    public int LevelOneDamagingMoveCount { get; init; }

    /// <summary>
    /// Gets or initializes whether the level-1 minimum and damaging-move guarantee was satisfied.
    /// </summary>
    public bool LevelOneGuaranteeSatisfied { get; init; }

    /// <summary>
    /// Gets or initializes the theoretical initial four moves at the encountered level.
    /// </summary>
    public IReadOnlyList<MoveAccessInitialMoveMetricPayload> InitialMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes the abstract ordinary-tutor compatibility count.
    /// </summary>
    public int OrdinaryTutorAbstractCount { get; init; }

    /// <summary>
    /// Gets or initializes the ordinary-tutor compatibility count backed by an actual tutor.
    /// </summary>
    public int OrdinaryTutorSupportedCount { get; init; }
}

/// <summary>
/// Describes one generated move-access channel.
/// </summary>
public sealed class MoveAccessChannelMetricPayload
{
    /// <summary>
    /// Gets or initializes the channel identifier.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Gets or initializes the generated entry count.
    /// </summary>
    public int GeneratedEntryCount { get; init; }

    /// <summary>
    /// Gets or initializes the distinct move count within the channel.
    /// </summary>
    public int UniqueMoveCount { get; init; }

    /// <summary>
    /// Gets or initializes the total entries contributed by both fusion components.
    /// </summary>
    public int? ComponentEntryCount { get; init; }

    /// <summary>
    /// Gets or initializes the entries removed by fusion duplicate handling.
    /// </summary>
    public int? DuplicateRemovals { get; init; }

    /// <summary>
    /// Gets or initializes growth over the larger individual component channel.
    /// </summary>
    public int? GrowthOverLargerComponent { get; init; }
}

/// <summary>
/// Describes one move in the theoretical initial four at an encountered level.
/// </summary>
public sealed class MoveAccessInitialMoveMetricPayload
{
    /// <summary>
    /// Gets or initializes the move identifier.
    /// </summary>
    public required string MoveId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed move name.
    /// </summary>
    public required string MoveName { get; init; }

    /// <summary>
    /// Gets or initializes the physical, special, or status category.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets or initializes the move's base power.
    /// </summary>
    public int Power { get; init; }
}

/// <summary>
/// Describes a machine obtained during the run and party compatibility at that moment.
/// </summary>
public sealed class MachineAcquisitionMetricPayload
{
    /// <summary>
    /// Gets or initializes the item identifier.
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed item name.
    /// </summary>
    public required string ItemName { get; init; }

    /// <summary>
    /// Gets or initializes the move identifier.
    /// </summary>
    public required string MoveId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed move name.
    /// </summary>
    public required string MoveName { get; init; }

    /// <summary>
    /// Gets or initializes the number of copies obtained.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Gets or initializes whether the machine was found or script-awarded.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets or initializes the party size when the machine was obtained.
    /// </summary>
    public int PartyCount { get; init; }

    /// <summary>
    /// Gets or initializes compatible party members when the machine was obtained.
    /// </summary>
    public IReadOnlyList<MoveAccessPartyMemberMetricPayload> CompatibleParty { get; init; } = [];
}

/// <summary>
/// Describes a tutor interaction and compatible party members at that moment.
/// </summary>
public sealed class TutorVisitMetricPayload
{
    /// <summary>
    /// Gets or initializes the tutor channel identifier.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets or initializes the tutor slot identifier.
    /// </summary>
    public string? TutorId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed tutor name or location.
    /// </summary>
    public string? TutorName { get; init; }

    /// <summary>
    /// Gets or initializes the offered move identifier.
    /// </summary>
    public required string MoveId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed offered move name.
    /// </summary>
    public required string MoveName { get; init; }

    /// <summary>
    /// Gets or initializes compatible party members at the visit.
    /// </summary>
    public IReadOnlyList<MoveAccessPartyMemberMetricPayload> CompatibleParty { get; init; } = [];

    /// <summary>
    /// Gets or initializes the personal identifier of the Pokemon taught, if any.
    /// </summary>
    public string? TaughtPokemonId { get; init; }

    /// <summary>
    /// Gets or initializes the species name of the Pokemon taught, if any.
    /// </summary>
    public string? TaughtSpeciesName { get; init; }
}

/// <summary>
/// Identifies one compatible party member in an interaction metric.
/// </summary>
public sealed class MoveAccessPartyMemberMetricPayload
{
    /// <summary>
    /// Gets or initializes the Pokemon personal identifier.
    /// </summary>
    public required string PokemonId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed species name.
    /// </summary>
    public required string SpeciesName { get; init; }
}

/// <summary>
/// Describes one observed permanent or temporary move acquisition.
/// </summary>
public sealed class MoveAcquisitionMetricPayload
{
    /// <summary>
    /// Gets or initializes the Pokemon personal identifier.
    /// </summary>
    public required string PokemonId { get; init; }

    /// <summary>
    /// Gets or initializes the species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the level at acquisition.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the move identifier.
    /// </summary>
    public required string MoveId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed move name.
    /// </summary>
    public required string MoveName { get; init; }

    /// <summary>
    /// Gets or initializes the acquisition channel identifier.
    /// </summary>
    public required string Source { get; init; }
}

/// <summary>
/// Describes aggregated uses of one move by one species and side.
/// </summary>
public sealed class MoveUseMetricPayload
{
    /// <summary>
    /// Gets or initializes the player or enemy side identifier.
    /// </summary>
    public required string Side { get; init; }

    /// <summary>
    /// Gets or initializes the species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the move identifier.
    /// </summary>
    public required string MoveId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed move name.
    /// </summary>
    public required string MoveName { get; init; }

    /// <summary>
    /// Gets or initializes whether the use came from a copy-style special use.
    /// </summary>
    public bool Copied { get; init; }

    /// <summary>
    /// Gets or initializes the observed use count.
    /// </summary>
    public int Count { get; init; }
}
