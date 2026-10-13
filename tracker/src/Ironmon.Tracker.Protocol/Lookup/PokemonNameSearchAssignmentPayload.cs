namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Supplies one bounded tracker-matched page for game validation and authoritative discovery-status annotation.
/// </summary>
/// <remarks>
/// Instances are initialized through their properties and sent with the original search request so the game can validate its context and matched identifiers.
/// </remarks>
public sealed class PokemonNameSearchAssignmentPayload
{
    /// <summary>
    /// Gets or initializes the game-issued catalog context token used to produce this page.
    /// </summary>
    public required string ContextToken { get; init; }

    /// <summary>
    /// Gets or initializes the complete number of matches before paging.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Gets or initializes the stable species identifiers on the bounded page in the game's search order.
    /// </summary>
    public IReadOnlyList<string> SpeciesIds { get; init; } = [];
}
