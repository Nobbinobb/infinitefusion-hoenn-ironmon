namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Reports whether the game accepted the starter request for its active scene.
/// </summary>
public sealed class StarterSelectionResponsePayload
{
    /// <summary>
    /// Gets or initializes whether the scene will consume the requested selection.
    /// </summary>
    public bool Accepted { get; init; }
}
