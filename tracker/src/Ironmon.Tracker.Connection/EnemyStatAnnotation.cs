namespace Ironmon.Tracker.Connection;

/// <summary>
/// Identifies the manual comparison mark assigned to an enemy stat.
/// </summary>
public enum EnemyStatAnnotation
{
    /// <summary>
    /// Indicates no annotation.
    /// </summary>
    Empty = 0,

    /// <summary>
    /// Indicates a positive annotation.
    /// </summary>
    Plus = 1,

    /// <summary>
    /// Indicates a negative annotation.
    /// </summary>
    Minus = 2
}
