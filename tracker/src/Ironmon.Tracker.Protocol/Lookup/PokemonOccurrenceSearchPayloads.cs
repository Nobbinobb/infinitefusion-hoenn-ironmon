namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests one page of wild occurrences for a generated Pokemon.
/// </summary>
public sealed class WildOccurrenceSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty wild-occurrence request for protocol serialization.
    /// </summary>
    public WildOccurrenceSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the generated species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of occurrences to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.OccurrencePageSize;

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }

    /// <summary>
    /// Gets or initializes ordered normal-material membership for this fusion, packed least-significant-bit first by Body then Head.
    /// </summary>
    public byte[]? FusionMaterialMembership { get; init; }
}

/// <summary>
/// Returns one page of wild occurrences for a generated Pokemon.
/// </summary>
public sealed class WildOccurrenceSearchResponsePayload
{
    /// <summary>
    /// Gets or initializes whether location preparation is still running and this page must be requested again.
    /// </summary>
    public bool Pending { get; init; }

    /// <summary>
    /// Initializes an empty wild-occurrence response for protocol serialization.
    /// </summary>
    public WildOccurrenceSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the wild occurrences on this page.
    /// </summary>
    public IReadOnlyList<WildPokemonOccurrenceSnapshot> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete number of matching wild occurrences.
    /// </summary>
    public int Total { get; init; }
}

/// <summary>
/// Requests one page of trainer occurrences for a generated Pokemon.
/// </summary>
public sealed class TrainerOccurrenceSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty trainer-occurrence request for protocol serialization.
    /// </summary>
    public TrainerOccurrenceSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the generated species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of occurrences to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.OccurrencePageSize;

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}

/// <summary>
/// Returns one page of trainer occurrences for a generated Pokemon.
/// </summary>
public sealed class TrainerOccurrenceSearchResponsePayload
{
    /// <summary>
    /// Gets or initializes whether the overview deferred this list to its occurrence request.
    /// </summary>
    public bool Pending { get; init; }

    /// <summary>
    /// Initializes an empty trainer-occurrence response for protocol serialization.
    /// </summary>
    public TrainerOccurrenceSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the trainer occurrences on this page.
    /// </summary>
    public IReadOnlyList<TrainerPokemonOccurrenceSnapshot> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete number of matching trainer occurrences.
    /// </summary>
    public int Total { get; init; }
}
