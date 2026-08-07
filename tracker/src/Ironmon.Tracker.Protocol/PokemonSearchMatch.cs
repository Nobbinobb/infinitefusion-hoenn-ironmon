namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes one stable Pokemon search match.
/// </summary>
public sealed class PokemonSearchMatch
{
    /// <summary>
    /// Initializes an empty Pokemon search match for protocol serialization.
    /// </summary>
    public PokemonSearchMatch()
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
}
