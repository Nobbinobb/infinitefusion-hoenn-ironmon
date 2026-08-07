namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Identifies where an inspected generated ability slot is presented.
/// </summary>
public enum DebugAbilitySlotGroup
{
    /// <summary>
    /// The currently active Pokemon ability.
    /// </summary>
    Current = 0,

    /// <summary>
    /// A generated slot for a normal species.
    /// </summary>
    Generated = 1,

    /// <summary>
    /// A final generated fusion slot.
    /// </summary>
    FinalFusion = 2,

    /// <summary>
    /// A generated slot belonging to the displayed body component.
    /// </summary>
    BodyGenerated = 3,

    /// <summary>
    /// A generated slot belonging to the displayed head component.
    /// </summary>
    HeadGenerated = 4
}
