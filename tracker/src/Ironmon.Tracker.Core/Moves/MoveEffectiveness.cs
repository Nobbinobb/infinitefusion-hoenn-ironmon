namespace Ironmon.Tracker.Core.Moves;

/// <summary>
/// Identifies a move type's combined effectiveness against the target types.
/// </summary>
public enum MoveEffectiveness
{
    /// <summary>
    /// The target is immune.
    /// </summary>
    Immune = 0,

    /// <summary>
    /// The move deals one quarter damage.
    /// </summary>
    Quarter = 1,

    /// <summary>
    /// The move deals half damage.
    /// </summary>
    Half = 2,

    /// <summary>
    /// The move deals normal damage.
    /// </summary>
    Neutral = 4,

    /// <summary>
    /// The move deals double damage.
    /// </summary>
    Double = 8,

    /// <summary>
    /// The move deals quadruple damage.
    /// </summary>
    Quadruple = 16
}
