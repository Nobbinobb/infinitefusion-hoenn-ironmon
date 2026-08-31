namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Advances active-run preparation without requesting any undiscovered lookup results.
/// </summary>
public sealed class RunLookupPreparationRequestPayload
{
    /// <summary>
    /// Initializes an empty preparation request for protocol serialization.
    /// </summary>
    public RunLookupPreparationRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes whether the user explicitly requested priority calculation.
    /// </summary>
    public bool Foreground { get; init; }

    /// <summary>
    /// Gets or initializes the tracker-computed result of the current calculation job.
    /// </summary>
    public PlayerFusionClosureResultPayload? FusionClosureResult { get; init; }

    /// <summary>
    /// Gets or initializes whether the tracker cannot reproduce the requested calculation job.
    /// </summary>
    public bool FusionClosureWorkerUnavailable { get; init; }
}
