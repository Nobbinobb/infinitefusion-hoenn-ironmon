namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Compares connected aggregate population metadata with the packaged release dataset.
/// </summary>
public static class TypeCoverageCompatibility
{
    /// <summary>
    /// Evaluates whether the connected run can use the packaged dataset.
    /// </summary>
    /// <param name="dataset">The packaged release dataset.</param>
    /// <param name="context">The optional connected coverage context.</param>
    /// <returns>The precise compatibility state.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the dataset is null.</exception>
    public static TypeCoverageCompatibilityStatus Evaluate(TypeCoverageDataset dataset, TypeCoverageContext? context)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (context is null)
            return TypeCoverageCompatibilityStatus.MissingContext;

        if (!string.Equals(dataset.GameVersion, context.GameVersion, StringComparison.Ordinal))
            return TypeCoverageCompatibilityStatus.GameVersionMismatch;

        if (context.Policy == TypeCoveragePolicy.Unknown)
            return TypeCoverageCompatibilityStatus.UnsupportedPolicy;

        if ((context.Policy is TypeCoveragePolicy.NormalOnly or TypeCoveragePolicy.Mixed) && !NormalPoolMatches(dataset, context))
            return TypeCoverageCompatibilityStatus.NormalPoolMismatch;

        if ((context.Policy is TypeCoveragePolicy.CustomFusionsOnly or TypeCoveragePolicy.Mixed) && !FusionPoolMatches(dataset, context))
            return TypeCoverageCompatibilityStatus.FusionPoolMismatch;

        return TypeCoverageCompatibilityStatus.Compatible;
    }

    /// <summary>
    /// Compares the connected normal population with the packaged population.
    /// </summary>
    /// <param name="dataset">The packaged release dataset.</param>
    /// <param name="context">The connected coverage context.</param>
    /// <returns>Whether the normal population matches exactly.</returns>
    private static bool NormalPoolMatches(TypeCoverageDataset dataset, TypeCoverageContext context)
        => dataset.NormalPoolSize == context.NormalPoolSize && string.Equals(dataset.NormalPoolFingerprint, context.NormalPoolFingerprint, StringComparison.Ordinal);

    /// <summary>
    /// Compares the connected custom-fusion population with the packaged population.
    /// </summary>
    /// <param name="dataset">The packaged release dataset.</param>
    /// <param name="context">The connected coverage context.</param>
    /// <returns>Whether the custom-fusion population matches exactly.</returns>
    private static bool FusionPoolMatches(TypeCoverageDataset dataset, TypeCoverageContext context)
    {
        return dataset.FusionPoolSchemaVersion == context.FusionPoolSchemaVersion
            && dataset.FusionPoolSize == context.FusionPoolSize
            && string.Equals(dataset.FusionPoolFingerprint, context.FusionPoolFingerprint, StringComparison.Ordinal);
    }
}
