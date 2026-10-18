namespace Ironmon.Tracker.Connection.Knowledge;

/// <summary>
/// Identifies an enemy stat with a tracker-owned manual annotation.
/// </summary>
public enum EnemyStat
{
    /// <summary>
    /// Identifies maximum HP.
    /// </summary>
    Hp = 0,

    /// <summary>
    /// Identifies Attack.
    /// </summary>
    Attack = 1,

    /// <summary>
    /// Identifies Defense.
    /// </summary>
    Defense = 2,

    /// <summary>
    /// Identifies Special Attack.
    /// </summary>
    SpecialAttack = 3,

    /// <summary>
    /// Identifies Special Defense.
    /// </summary>
    SpecialDefense = 4,

    /// <summary>
    /// Identifies Speed.
    /// </summary>
    Speed = 5
}
