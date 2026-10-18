namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Reports whether the connected game queued a guarded run reset.
/// </summary>
public sealed class ResetRunResponsePayload
{
    /// <summary>
    /// Initializes an empty reset response for protocol deserialization.
    /// </summary>
    public ResetRunResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes whether the active game accepted the reset request.
    /// </summary>
    public bool Accepted { get; init; }
}
