namespace Ironmon.Tracker.Core;

/// <summary>
/// Identifies how a Pokemon can learn a move.
/// </summary>
public enum MoveLearnSource
{
    /// <summary>
    /// Indicates that the source is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Indicates that the move belongs to the deterministic level-up learnset.
    /// </summary>
    LevelUp = 1,

    /// <summary>
    /// Indicates that the move was taught by a machine.
    /// </summary>
    Machine = 2,

    /// <summary>
    /// Indicates that the move was taught by a move tutor.
    /// </summary>
    Tutor = 3,

    /// <summary>
    /// Indicates that the move was inherited as an Egg move.
    /// </summary>
    Egg = 4,

    /// <summary>
    /// Indicates that the move came from a scripted event.
    /// </summary>
    Event = 5,

    /// <summary>
    /// Indicates that the move came from a player-only fusion choice.
    /// </summary>
    FusionChoice = 6,

    /// <summary>
    /// Indicates that the move was assigned through development tools.
    /// </summary>
    Debug = 7
}
