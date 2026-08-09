namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Defines stable identifiers used by the debug target selector.
/// </summary>
internal static class DebugTargetIds
{
    /// <summary>
    /// Gets the player target identifier.
    /// </summary>
    internal const string Player = "player";

    /// <summary>
    /// Gets the enemy target identifier.
    /// </summary>
    internal const string Enemy = "enemy";

    /// <summary>
    /// Gets the separator between a target kind and its position.
    /// </summary>
    internal const char Separator = ':';

    /// <summary>
    /// Gets the number of segments in an enemy selector value.
    /// </summary>
    internal const int EnemySegmentCount = 2;

    /// <summary>
    /// Gets the game-position stride between adjacent enemy battlers.
    /// </summary>
    internal const int EnemyPositionStride = 2;

    /// <summary>
    /// Gets the offset from zero-based game positions to one-based display positions.
    /// </summary>
    internal const int DisplayPositionOffset = 1;
}
