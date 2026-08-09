namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Identifies the independently requested section of a Pokemon lookup.
/// </summary>
public enum PokemonLookupSection
{
    /// <summary>
    /// Requests identity, occurrences, and fusion relationships.
    /// </summary>
    Overview = 0,

    /// <summary>
    /// Requests abilities and ability-generator diagnostics.
    /// </summary>
    Abilities = 1,

    /// <summary>
    /// Requests base stats and base-stat-generator diagnostics.
    /// </summary>
    Stats = 2,

    /// <summary>
    /// Requests move access and move-generator diagnostics.
    /// </summary>
    Moves = 3,

    /// <summary>
    /// Requests evolution relationships and evolution-generator diagnostics.
    /// </summary>
    Evolutions = 4
}
