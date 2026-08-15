namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Describes aggregate type coverage for one attacking-type selection.
/// </summary>
/// <remarks>
/// Initializes one aggregate type-coverage result.
/// </remarks>
/// <param name="selectedTypes">The normalized attacking types in chart order.</param>
/// <param name="buckets">The calculated effectiveness buckets, or an empty list when no type is selected.</param>
public sealed class TypeCoverageCalculation(IReadOnlyList<string> selectedTypes, IReadOnlyList<TypeCoverageBucketResult> buckets)
{
    /// <summary>
    /// Gets the normalized attacking types in chart order.
    /// </summary>
    public IReadOnlyList<string> SelectedTypes { get; } = selectedTypes;

    /// <summary>
    /// Gets whether at least one attacking type is selected.
    /// </summary>
    public bool HasSelection => SelectedTypes.Count > 0;

    /// <summary>
    /// Gets the calculated effectiveness buckets, or an empty list when no type is selected.
    /// </summary>
    public IReadOnlyList<TypeCoverageBucketResult> Buckets { get; } = buckets;
}
