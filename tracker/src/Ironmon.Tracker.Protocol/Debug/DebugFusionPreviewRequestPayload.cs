namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests both active-run fusion orientations in authorized debug mode.
/// </summary>
public sealed class DebugFusionPreviewRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run fusion request for protocol serialization.
    /// </summary>
    public DebugFusionPreviewRequestPayload()
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
}
