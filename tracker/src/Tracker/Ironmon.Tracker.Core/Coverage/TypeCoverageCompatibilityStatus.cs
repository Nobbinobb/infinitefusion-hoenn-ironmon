namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Identifies whether connected run metadata can use the packaged coverage dataset.
/// </summary>
public enum TypeCoverageCompatibilityStatus
{
    /// <summary>
    /// The connected metadata matches every population needed by its trainer policy.
    /// </summary>
    Compatible = 0,

    /// <summary>
    /// The connected game predates the optional coverage context.
    /// </summary>
    MissingContext = 1,

    /// <summary>
    /// The connected game reports an unsupported trainer policy.
    /// </summary>
    UnsupportedPolicy = 2,

    /// <summary>
    /// The connected Infinite Fusion version differs from the dataset release.
    /// </summary>
    GameVersionMismatch = 3,

    /// <summary>
    /// The eligible normal population differs from the dataset release.
    /// </summary>
    NormalPoolMismatch = 4,

    /// <summary>
    /// The eligible custom-fusion population differs from the dataset release.
    /// </summary>
    FusionPoolMismatch = 5
}
