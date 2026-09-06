namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Requests a revealed starter from the currently open selection screen.
/// </summary>
public sealed class StarterSelectionRequestPayload
{
    /// <summary>
    /// Gets or initializes the game-issued selection identifier.
    /// </summary>
    public required string SelectionId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based starter position.
    /// </summary>
    public int Index { get; init; }
}
