namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Describes aggregate normal and custom-fusion populations for one defensive type profile.
/// </summary>
public sealed class TypeCoverageProfile
{
    /// <summary>
    /// Initializes an empty profile for dataset deserialization.
    /// </summary>
    public TypeCoverageProfile()
    {
    }

    /// <summary>
    /// Gets or initializes the one or two defensive types in stable chart order.
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>
    /// Gets or initializes the number of eligible normal species with this profile.
    /// </summary>
    public int NormalCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of eligible custom fusions with this profile.
    /// </summary>
    public int FusionCount { get; init; }
}
