namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Reports whether the game accepted a tracker-selected battle item.
/// </summary>
public sealed class BattleItemUseResponsePayload
{
    /// <summary>
    /// Initializes an empty response payload for protocol serialization.
    /// </summary>
    public BattleItemUseResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes whether the item was accepted as the current turn's action.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// Gets or initializes the user-facing result message.
    /// </summary>
    public required string Message { get; init; }
}
