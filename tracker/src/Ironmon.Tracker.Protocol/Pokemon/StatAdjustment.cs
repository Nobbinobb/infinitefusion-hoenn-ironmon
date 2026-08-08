namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Identifies a nature's adjustment to one calculated stat.
/// </summary>
public enum StatAdjustment
{
    /// <summary>
    /// The stat is unaffected by nature.
    /// </summary>
    Neutral = 0,

    /// <summary>
    /// The stat is increased by nature.
    /// </summary>
    Increased = 1,

    /// <summary>
    /// The stat is decreased by nature.
    /// </summary>
    Decreased = 2
}
