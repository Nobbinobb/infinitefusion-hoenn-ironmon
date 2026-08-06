namespace Ironmon.Tracker.Core;

/// <summary>
/// Identifies the observable action that taught the tracker about a move.
/// </summary>
public enum MoveDiscoveryOrigin
{
    /// <summary>
    /// Indicates that an opposing Pokemon used the move.
    /// </summary>
    EnemyUse = 0,

    /// <summary>
    /// Indicates that the move was present when the player's Pokemon was initialized.
    /// </summary>
    PlayerInitial = 1,

    /// <summary>
    /// Indicates that the player's Pokemon learned the move after leveling up.
    /// </summary>
    PlayerLevelUp = 2
}
