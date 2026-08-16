namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one stack of battle-usable items in the player's bag.
/// </summary>
public sealed class BattleItemSnapshot
{
    /// <summary>
    /// Initializes an empty battle-item snapshot for protocol serialization.
    /// </summary>
    public BattleItemSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable game item identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets or initializes the localized item name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the localized item description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets or initializes the number currently carried.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Gets or initializes the display category.
    /// </summary>
    public BattleItemCategory Category { get; init; }

    /// <summary>
    /// Gets or initializes whether using the item requires choosing a move.
    /// </summary>
    public bool RequiresMove { get; init; }
}
