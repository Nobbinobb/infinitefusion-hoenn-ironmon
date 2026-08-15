namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Identifies the trainer population represented by a type-coverage calculation.
/// </summary>
public enum TypeCoveragePolicy
{
    /// <summary>
    /// The connected trainer policy is absent or unsupported.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Trainer slots select the normal or custom-fusion category with equal probability.
    /// </summary>
    Mixed = 1,

    /// <summary>
    /// Trainer slots select only eligible custom fusions.
    /// </summary>
    CustomFusionsOnly = 2,

    /// <summary>
    /// Trainer slots select only eligible normal Pokemon.
    /// </summary>
    NormalOnly = 3
}
