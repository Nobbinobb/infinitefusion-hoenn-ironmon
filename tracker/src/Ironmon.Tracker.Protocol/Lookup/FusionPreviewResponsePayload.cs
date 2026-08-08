namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Returns deterministic Ironmon fusion outcomes for two normal materials.
/// </summary>
public sealed class FusionPreviewResponsePayload
{
    /// <summary>
    /// Initializes an empty fusion preview response for protocol serialization.
    /// </summary>
    public FusionPreviewResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the distinct ordered outcomes.
    /// </summary>
    public IReadOnlyList<FusionOutcomeSnapshot> Outcomes { get; init; } = [];
}
