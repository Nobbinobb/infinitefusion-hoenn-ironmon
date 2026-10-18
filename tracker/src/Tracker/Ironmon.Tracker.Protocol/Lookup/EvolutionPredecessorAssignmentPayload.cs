namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries one exact tracker-computed fusion predecessor assignment for game-owned display formatting.
/// </summary>
public sealed class EvolutionPredecessorAssignmentPayload
{
    /// <summary>
    /// Initializes an empty assignment for protocol serialization.
    /// </summary>
    public EvolutionPredecessorAssignmentPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the numeric custom-fusion source identifier.
    /// </summary>
    public int SourceId { get; init; }

    /// <summary>
    /// Gets or initializes the generated base-stat total of the source fusion.
    /// </summary>
    public int SourceBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the evolving component side.
    /// </summary>
    public EvolutionCandidateSide ComponentSide { get; init; }

    /// <summary>
    /// Gets or initializes the stable conceptual normal-evolution branch identity.
    /// </summary>
    public required string ComponentBranchIdentity { get; init; }
}
