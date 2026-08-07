namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Requests complete deterministic information for one stable Pokemon identifier.
/// </summary>
public sealed class PokemonLookupRequestPayload
{
    /// <summary>
    /// Initializes an empty Pokemon lookup request for protocol serialization.
    /// </summary>
    public PokemonLookupRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the selected stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the fixed compatibility level understood by earlier Part 6 game scripts.
    /// </summary>
    public int Level { get; init; } = 100;

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}
