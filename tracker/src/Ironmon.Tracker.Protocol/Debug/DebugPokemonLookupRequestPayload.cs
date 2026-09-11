using Ironmon.Tracker.Protocol.Lookup;

namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests generated information for one Pokemon in the active debug run.
/// </summary>
public sealed class DebugPokemonLookupRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run lookup request for protocol serialization.
    /// </summary>
    public DebugPokemonLookupRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the selected stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the independently requested information section.
    /// </summary>
    public PokemonLookupSection Section { get; init; } = PokemonLookupSection.Overview;

    /// <summary>
    /// Gets or initializes whether occurrence lists are requested separately from the overview.
    /// </summary>
    public bool DeferOccurrences { get; init; }

    /// <summary>
    /// Gets or initializes the numeric reverse partner from this run's completed fusion mapping.
    /// </summary>
    public int? ReverseFusionId { get; init; }

    /// <summary>
    /// Gets or initializes exact tracker-computed outgoing fusion-evolution assignments when available.
    /// </summary>
    public IReadOnlyList<EvolutionTargetAssignmentPayload>? EvolutionAssignments { get; init; }
}
