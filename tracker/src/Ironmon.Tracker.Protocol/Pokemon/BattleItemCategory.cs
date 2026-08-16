namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Identifies the tracker section containing one battle-usable item.
/// </summary>
public enum BattleItemCategory
{
    /// <summary>
    /// Restores the active Pokemon's HP.
    /// </summary>
    Healing = 0,

    /// <summary>
    /// Restores one or more moves' PP.
    /// </summary>
    PpRestore = 1,

    /// <summary>
    /// Removes a persistent or volatile status condition.
    /// </summary>
    Status = 2,

    /// <summary>
    /// Changes combat stats or critical-hit rate.
    /// </summary>
    CombatStat = 3,

    /// <summary>
    /// Covers every remaining item usable during combat.
    /// </summary>
    Other = 4
}
