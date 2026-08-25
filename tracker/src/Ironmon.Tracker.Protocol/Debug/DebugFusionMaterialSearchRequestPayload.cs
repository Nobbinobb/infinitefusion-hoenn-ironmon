namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests one page of fusion-material pairs from an authorized active run.
/// </summary>
public sealed class DebugFusionMaterialSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run material-page request for protocol serialization.
    /// </summary>
    public DebugFusionMaterialSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the fusion species identifier.
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
    /// Gets or initializes the maximum number of pairs to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.FusionMaterialPageSize;

    /// <summary>
    /// Gets or initializes the tracker-generated material assignments for this page when available.
    /// </summary>
    public IReadOnlyList<FusionMaterialAssignmentPayload>? MaterialAssignments { get; init; }

    /// <summary>
    /// Gets or initializes the total number of tracker-generated material assignments when available.
    /// </summary>
    public int? MaterialAssignmentTotal { get; init; }
}
