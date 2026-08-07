namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Identifies how a move deals damage.
/// </summary>
public enum MoveCategory
{
    /// <summary>
    /// The move category is unavailable.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The move uses the physical Attack and Defense stats.
    /// </summary>
    Physical = 1,

    /// <summary>
    /// The move uses the Special Attack and Special Defense stats.
    /// </summary>
    Special = 2,

    /// <summary>
    /// The move does not deal direct damage.
    /// </summary>
    Status = 3
}
