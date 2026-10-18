namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Identifies a page in the shared Pokemon information card.
/// </summary>
public enum PokemonInformationPage
{
    /// <summary>
    /// Shows identity, typing, occurrences, and fusion relationships.
    /// </summary>
    Overview = 0,

    /// <summary>
    /// Shows generated abilities and any available live slot diagnostics.
    /// </summary>
    Abilities = 1,

    /// <summary>
    /// Shows original and generated base stats and any available generator diagnostics.
    /// </summary>
    Stats = 2,

    /// <summary>
    /// Shows generated move access.
    /// </summary>
    Moves = 3,

    /// <summary>
    /// Shows generated candidates, graph edges, and authored evolution relations.
    /// </summary>
    Evolutions = 4
}
