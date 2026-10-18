using Ironmon.Tracker.Core.Moves;

namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Calculates best available attacking-type coverage over release aggregate profiles.
/// </summary>
public static class TypeCoverageCalculator
{
    private static readonly IReadOnlyList<MoveEffectiveness> _bucketOrder =
    [
        MoveEffectiveness.Immune,
        MoveEffectiveness.Quarter,
        MoveEffectiveness.Half,
        MoveEffectiveness.Neutral,
        MoveEffectiveness.Double,
        MoveEffectiveness.Quadruple
    ];

    /// <summary>
    /// Calculates coverage for one trainer policy and attacking-type selection.
    /// </summary>
    /// <param name="dataset">The validated aggregate release dataset.</param>
    /// <param name="policy">The active trainer species policy.</param>
    /// <param name="attackingTypes">The selected attacking type identifiers.</param>
    /// <returns>The aggregate counts and policy-weighted percentages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required input is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a selected type is unsupported.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the policy is unsupported.</exception>
    public static TypeCoverageCalculation Calculate(TypeCoverageDataset dataset, TypeCoveragePolicy policy, IEnumerable<string> attackingTypes)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(attackingTypes);
        IReadOnlyList<string> selectedTypes = NormalizeTypes(attackingTypes);
        if (selectedTypes.Count == 0)
            return new(selectedTypes, []);

        Dictionary<MoveEffectiveness, long> normalCounts = CreateEmptyCounts();
        Dictionary<MoveEffectiveness, long> fusionCounts = CreateEmptyCounts();
        foreach (TypeCoverageProfile profile in dataset.Profiles)
        {
            MoveEffectiveness best = selectedTypes.Max(type => TypeEffectivenessRules.Calculate(type, profile.Types));
            normalCounts[best] += profile.NormalCount;
            fusionCounts[best] += profile.FusionCount;
        }

        IReadOnlyList<TypeCoverageBucketResult> buckets = policy switch
        {
            TypeCoveragePolicy.NormalOnly => CreateSinglePopulationBuckets(normalCounts, dataset.NormalPoolSize),
            TypeCoveragePolicy.CustomFusionsOnly => CreateSinglePopulationBuckets(fusionCounts, dataset.FusionPoolSize),
            TypeCoveragePolicy.Mixed => CreateMixedBuckets(normalCounts, fusionCounts, dataset),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "The trainer policy is unsupported.")
        };

        return new(selectedTypes, buckets);
    }

    /// <summary>
    /// Normalizes, validates, deduplicates, and orders attacking types.
    /// </summary>
    /// <param name="attackingTypes">The selected attacking types.</param>
    /// <returns>The stable normalized selection.</returns>
    private static IReadOnlyList<string> NormalizeTypes(IEnumerable<string> attackingTypes)
    {
        HashSet<string> selected = new(StringComparer.Ordinal);
        foreach (string type in attackingTypes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            string normalized = type.ToUpperInvariant();
            PokemonTypeCatalog.GetOrder(normalized);
            selected.Add(normalized);
        }

        return [.. PokemonTypeCatalog.StandardTypes.Where(selected.Contains)];
    }

    /// <summary>
    /// Creates zeroed counts for every supported effectiveness bucket.
    /// </summary>
    /// <returns>The mutable bucket counts.</returns>
    private static Dictionary<MoveEffectiveness, long> CreateEmptyCounts()
        => _bucketOrder.ToDictionary(effectiveness => effectiveness, _ => 0L);

    /// <summary>
    /// Creates buckets for one uniformly selected population.
    /// </summary>
    /// <param name="counts">The population counts by effectiveness.</param>
    /// <param name="populationSize">The complete eligible population size.</param>
    /// <returns>The stable calculated buckets.</returns>
    private static IReadOnlyList<TypeCoverageBucketResult> CreateSinglePopulationBuckets(IReadOnlyDictionary<MoveEffectiveness, long> counts, int populationSize)
        => [.. _bucketOrder.Select(effectiveness => new TypeCoverageBucketResult(effectiveness, counts[effectiveness], counts[effectiveness] * 100m / populationSize))];

    /// <summary>
    /// Creates raw combined counts and category-first 50/50 percentages for Mixed policy.
    /// </summary>
    /// <param name="normalCounts">The normal population counts.</param>
    /// <param name="fusionCounts">The custom-fusion population counts.</param>
    /// <param name="dataset">The aggregate release dataset.</param>
    /// <returns>The stable calculated buckets.</returns>
    private static IReadOnlyList<TypeCoverageBucketResult> CreateMixedBuckets(IReadOnlyDictionary<MoveEffectiveness, long> normalCounts, IReadOnlyDictionary<MoveEffectiveness, long> fusionCounts, TypeCoverageDataset dataset)
    {
        return [.. _bucketOrder.Select(effectiveness => new TypeCoverageBucketResult(
            effectiveness,
            normalCounts[effectiveness] + fusionCounts[effectiveness],
            (normalCounts[effectiveness] * 100m / dataset.NormalPoolSize + fusionCounts[effectiveness] * 100m / dataset.FusionPoolSize) / 2m))];
    }
}
