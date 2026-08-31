namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one authored slot or derived wild fusion which generates a lookup species.
/// </summary>
public sealed class WildPokemonOccurrenceSnapshot
{
    /// <summary>
    /// Gets or initializes the stable entry identifier shared with area lookup.
    /// </summary>
    public string? EntryId { get; init; }

    /// <summary>
    /// Gets or initializes the standard or overworld fusion origin, absent for authored slots.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Gets or initializes the second material's encounter table.
    /// </summary>
    public string? SecondaryEncounterType { get; init; }

    /// <summary>
    /// Gets or initializes the second material's encounter-data version.
    /// </summary>
    public int? SecondaryEncounterVersion { get; init; }

    /// <summary>
    /// Gets or initializes the fusion roll, not the probability of selecting this specific material pair.
    /// </summary>
    public decimal? FusionChancePercent { get; init; }

    /// <summary>
    /// Gets or initializes whether the fusion crosses environment, time, or weather tables.
    /// </summary>
    public bool CrossEnvironment { get; init; }

    /// <summary>
    /// Initializes an empty wild occurrence for protocol serialization.
    /// </summary>
    public WildPokemonOccurrenceSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the map identifier.
    /// </summary>
    public int MapId { get; init; }

    /// <summary>
    /// Gets or initializes the localized route or map name.
    /// </summary>
    public required string RouteName { get; init; }

    /// <summary>
    /// Gets or initializes the encounter-data mode.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Gets or initializes the encounter-data version.
    /// </summary>
    public int EncounterVersion { get; init; }

    /// <summary>
    /// Gets or initializes the encounter method or table name.
    /// </summary>
    public required string EncounterType { get; init; }

    /// <summary>
    /// Gets or initializes the one-based authored table slot.
    /// </summary>
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the second authored slot used by a wild fusion event.
    /// </summary>
    public int? SecondarySlot { get; init; }

    /// <summary>
    /// Gets or initializes the minimum effective Ironmon encounter level.
    /// </summary>
    public int MinimumLevel { get; init; }

    /// <summary>
    /// Gets or initializes the maximum effective Ironmon encounter level.
    /// </summary>
    public int MaximumLevel { get; init; }

    /// <summary>
    /// Gets or initializes the stable source-species identifier.
    /// </summary>
    public required string SourceSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized source-species name.
    /// </summary>
    public required string SourceSpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the second source-species identifier for a fusion event.
    /// </summary>
    public string? SecondarySourceSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the second source-species name for a fusion event.
    /// </summary>
    public string? SecondarySourceSpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the slot's percentage of its encounter table.
    /// </summary>
    public decimal? ChancePercent { get; init; }

    /// <summary>
    /// Gets or initializes whether the chance is conditional on a fusion event triggering.
    /// </summary>
    public bool ChanceIsConditional { get; init; }
}
