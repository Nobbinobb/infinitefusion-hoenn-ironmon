namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries one exact tracker-computed outgoing fusion-evolution assignment for game-owned display formatting.
/// </summary>
public sealed class EvolutionTargetAssignmentPayload
{
    /// <summary>
    /// Initializes an empty target assignment for protocol serialization.
    /// </summary>
    public EvolutionTargetAssignmentPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the numeric custom-fusion target identifier.
    /// </summary>
    public int TargetId { get; init; }

    /// <summary>
    /// Gets or initializes the generated base-stat total of the target fusion.
    /// </summary>
    public int TargetBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the evolving component side.
    /// </summary>
    public EvolutionCandidateSide ComponentSide { get; init; }

    /// <summary>
    /// Gets or initializes the stable conceptual normal-evolution branch identity.
    /// </summary>
    public required string ComponentBranchIdentity { get; init; }
}
