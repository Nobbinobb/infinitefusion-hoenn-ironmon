namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Identifies the randomizer eligibility rule applied to an inspected ability.
/// </summary>
public enum DebugAbilityEligibility
{
    /// <summary>
    /// No ability is present.
    /// </summary>
    None = 0,

    /// <summary>
    /// The ability is universally eligible.
    /// </summary>
    Universal = 1,

    /// <summary>
    /// The ability requires an exact species.
    /// </summary>
    ExactSpecies = 2,

    /// <summary>
    /// The ability requires a compatible fusion component.
    /// </summary>
    ComponentCompatible = 3
}
