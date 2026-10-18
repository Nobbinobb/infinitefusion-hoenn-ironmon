namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests one progressive page of generated evolution predecessors from an authorized active run.
/// </summary>
public sealed class DebugEvolutionPredecessorSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run predecessor request for protocol serialization.
    /// </summary>
    public DebugEvolutionPredecessorSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the target species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the live source whose represented species must override <see cref="SpeciesId"/>.
    /// </summary>
    public DebugPokemonTarget? Target { get; init; }

    /// <summary>
    /// Gets or initializes the enemy battler position when the live source is an enemy.
    /// </summary>
    public int? EnemyPosition { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of predecessors to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.EvolutionPredecessorPageSize;

    /// <summary>
    /// Gets or initializes the exact tracker-computed fusion predecessor assignments, or null when the active background assignment job is unavailable.
    /// </summary>
    public IReadOnlyList<EvolutionPredecessorAssignmentPayload>? PredecessorAssignments { get; init; }
}
