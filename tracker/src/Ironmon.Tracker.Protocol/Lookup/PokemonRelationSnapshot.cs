namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes a navigable Pokemon related to a post-run lookup result.
/// </summary>
public sealed class PokemonRelationSnapshot
{
    /// <summary>
    /// Initializes an empty Pokemon relation for protocol serialization.
    /// </summary>
    public PokemonRelationSnapshot()
    {
    }

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
    /// Gets or initializes the relationship or evolution requirement shown beneath the name.
    /// </summary>
    public required string Label { get; init; }
}
