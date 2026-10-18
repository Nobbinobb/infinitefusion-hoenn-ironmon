namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests one page of normal material pairs which produce an Ironmon fusion.
/// </summary>
public sealed class FusionMaterialSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty material-page request for protocol serialization.
    /// </summary>
    public FusionMaterialSearchRequestPayload()
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

    /// <summary>
    /// Gets or initializes the tracker-generated material assignments for this page when available.
    /// </summary>
    public IReadOnlyList<FusionMaterialAssignmentPayload>? MaterialAssignments { get; init; }

    /// <summary>
    /// Gets or initializes the total number of tracker-generated material assignments when available.
    /// </summary>
    public int? MaterialAssignmentTotal { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}

/// <summary>
/// Identifies one ordered normal-material pair assigned to a player fusion.
/// </summary>
public sealed class FusionMaterialAssignmentPayload
{
    /// <summary>
    /// Initializes an empty assignment for protocol serialization.
    /// </summary>
    public FusionMaterialAssignmentPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the numeric Body material identifier.
    /// </summary>
    public int BodyId { get; init; }

    /// <summary>
    /// Gets or initializes the numeric Head material identifier.
    /// </summary>
    public int HeadId { get; init; }
}

/// <summary>
/// Returns one page of normal fusion-material pairs.
/// </summary>
public sealed class FusionMaterialSearchResponsePayload
{
    /// <summary>
    /// Initializes an empty material-page response for protocol serialization.
    /// </summary>
    public FusionMaterialSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the material pairs on this page.
    /// </summary>
    public IReadOnlyList<FusionMaterialPairSnapshot> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete number of material pairs.
    /// </summary>
    public int Total { get; init; }
}
