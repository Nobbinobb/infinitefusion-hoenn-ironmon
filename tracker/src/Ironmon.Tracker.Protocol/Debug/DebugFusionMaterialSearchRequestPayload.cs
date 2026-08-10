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
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of pairs to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.FusionMaterialPageSize;
}
