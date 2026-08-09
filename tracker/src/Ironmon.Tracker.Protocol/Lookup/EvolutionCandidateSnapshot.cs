namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one Pokemon that could validly have been selected as an evolution destination.
/// </summary>
public sealed class EvolutionCandidateSnapshot
{
    /// <summary>
    /// Initializes an empty evolution candidate for protocol serialization.
    /// </summary>
    public EvolutionCandidateSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable candidate species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized candidate name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the resolved game-relative sprite path.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes the candidate's generated base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }
}
