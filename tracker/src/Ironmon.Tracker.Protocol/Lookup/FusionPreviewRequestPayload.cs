namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests both deterministic orientations for two normal fusion materials.
/// </summary>
public sealed class FusionPreviewRequestPayload
{
    /// <summary>
    /// Initializes an empty fusion preview request for protocol serialization.
    /// </summary>
    public FusionPreviewRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the first normal species identifier.
    /// </summary>
    public required string FirstSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the second normal species identifier.
    /// </summary>
    public required string SecondSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}
