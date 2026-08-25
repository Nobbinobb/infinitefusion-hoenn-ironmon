namespace Ironmon.Tracker.Protocol.Lookup;

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
    public int Level { get; init; } = TrackerProtocol.CompatibilityLookupLevel;

    /// <summary>
    /// Gets or initializes the independently requested information section.
    /// </summary>
    public PokemonLookupSection Section { get; init; } = PokemonLookupSection.Overview;

    /// <summary>
    /// Gets or initializes exact tracker-computed outgoing fusion-evolution assignments when available.
    /// </summary>
    public IReadOnlyList<EvolutionTargetAssignmentPayload>? EvolutionAssignments { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}
