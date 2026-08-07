namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Identifies whether an inspected ability occupies a normal or hidden slot.
/// </summary>
public enum DebugAbilitySlotKind
{
    /// <summary>
    /// The ability occupies a normal slot.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// The ability occupies a hidden slot.
    /// </summary>
    Hidden = 1
}
