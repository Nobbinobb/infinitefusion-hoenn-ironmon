namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes usable HP healing currently carried in the player's bag.
/// </summary>
public sealed class HealingInventorySnapshot
{
    /// <summary>
    /// Initializes an empty healing inventory snapshot for protocol serialization.
    /// </summary>
    public HealingInventorySnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the number of usable healing items.
    /// </summary>
    public int ItemCount { get; init; }

    /// <summary>
    /// Gets or initializes the combined potential HP restoration.
    /// </summary>
    public int PotentialHp { get; init; }

    /// <summary>
    /// Gets or initializes the potential restoration as a percentage of maximum HP.
    /// </summary>
    public double Percentage { get; init; }

    /// <summary>
    /// Gets or initializes the battle-usable items currently carried in the bag.
    /// </summary>
    public IReadOnlyList<BattleItemSnapshot> Items { get; init; } = [];
}
