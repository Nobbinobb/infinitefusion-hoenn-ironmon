namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Identifies the compact form of an evolution requirement.
/// </summary>
public enum EvolutionRequirementKind
{
    /// <summary>
    /// The evolution has a non-level and non-item requirement.
    /// </summary>
    Other = 0,

    /// <summary>
    /// The evolution requires reaching a level.
    /// </summary>
    Level = 1,

    /// <summary>
    /// The evolution requires using an item.
    /// </summary>
    Item = 2
}
