namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Requests one bag item as the active Pokemon's battle action.
/// </summary>
public sealed class BattleItemUseRequestPayload
{
    /// <summary>
    /// Initializes an empty request payload for protocol serialization.
    /// </summary>
    public BattleItemUseRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable game item identifier.
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based move index for PP-restoring items.
    /// </summary>
    public int? MoveIndex { get; init; }

    /// <summary>
    /// Gets or initializes the selected opposing battler position when relevant.
    /// </summary>
    public int? TargetPosition { get; init; }
}
