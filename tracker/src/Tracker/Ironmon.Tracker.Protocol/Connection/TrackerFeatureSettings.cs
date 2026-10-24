namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Stores independent feature switches without discarding their saved configuration.
/// </summary>
public sealed record TrackerFeatureSettings
{
    /// <summary>
    /// Gets whether earned achievements show game notifications.
    /// </summary>
    public bool AchievementPopups { get; init; } = true;

    /// <summary>
    /// Gets whether remaining Repel steps appear in the overworld.
    /// </summary>
    public bool RepelCounter { get; init; } = true;

    /// <summary>
    /// Gets whether the active speed appears in the overworld.
    /// </summary>
    public bool SpeedIndicator { get; init; } = true;

    /// <summary>
    /// Gets whether the battle item selector and its shortcuts are active.
    /// </summary>
    public bool BattleQuickItems { get; init; } = true;

    /// <summary>
    /// Gets whether the wild-battle Run shortcut is active.
    /// </summary>
    public bool QuickRun { get; init; } = true;

    /// <summary>
    /// Gets whether saved favorites qualify for the Favorite Clause.
    /// </summary>
    public bool FavoriteClause { get; init; } = true;

    /// <summary>
    /// Gets whether the saved nonzero starter BST ceiling is enforced.
    /// </summary>
    public bool EnforceBstLimit { get; init; } = true;
}
