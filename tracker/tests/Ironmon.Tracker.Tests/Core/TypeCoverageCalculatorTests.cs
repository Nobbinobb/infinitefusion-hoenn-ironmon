namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies policy-aware aggregate type-coverage calculations.
/// </summary>
public sealed class TypeCoverageCalculatorTests
{
    /// <summary>
    /// Initializes type-coverage calculator tests.
    /// </summary>
    public TypeCoverageCalculatorTests()
    {
    }

    /// <summary>
    /// Verifies normal-only counts and percentages over the normal population.
    /// </summary>
    [Fact]
    public void CalculateUsesNormalPopulationForNormalOnly()
    {
        TypeCoverageCalculation result = TypeCoverageCalculator.Calculate(CreateDataset(), TypeCoveragePolicy.NormalOnly, ["FIRE"]);

        AssertBucket(result, MoveEffectiveness.Half, 1, 50m);
        AssertBucket(result, MoveEffectiveness.Double, 1, 50m);
    }

    /// <summary>
    /// Verifies fusion-only counts and percentages over the fusion population.
    /// </summary>
    [Fact]
    public void CalculateUsesFusionPopulationForFusionOnly()
    {
        TypeCoverageCalculation result = TypeCoverageCalculator.Calculate(CreateDataset(), TypeCoveragePolicy.CustomFusionsOnly, ["FIRE"]);

        AssertBucket(result, MoveEffectiveness.Half, 5, 500m / 6m);
        AssertBucket(result, MoveEffectiveness.Double, 1, 100m / 6m);
    }

    /// <summary>
    /// Verifies that Mixed shows raw combined counts but category-first 50/50 percentages.
    /// </summary>
    [Fact]
    public void CalculateUsesCombinedCountsAndEqualCategoryWeightForMixed()
    {
        TypeCoverageCalculation result = TypeCoverageCalculator.Calculate(CreateDataset(), TypeCoveragePolicy.Mixed, ["FIRE"]);

        AssertBucket(result, MoveEffectiveness.Half, 6, (50m + 500m / 6m) / 2m);
        AssertBucket(result, MoveEffectiveness.Double, 2, (50m + 100m / 6m) / 2m);
        Assert.Equal(8, result.Buckets.Sum(bucket => bucket.Count));
        Assert.Equal(100m, result.Buckets.Sum(bucket => bucket.Percentage));
    }

    /// <summary>
    /// Verifies that every defensive profile retains its best selected attacking multiplier.
    /// </summary>
    [Fact]
    public void CalculateUsesBestSelectedAttackingType()
    {
        TypeCoverageCalculation result = TypeCoverageCalculator.Calculate(CreateDataset(), TypeCoveragePolicy.Mixed, ["electric", "FIRE", "FIRE"]);

        Assert.Equal(["FIRE", "ELECTRIC"], result.SelectedTypes);
        AssertBucket(result, MoveEffectiveness.Double, 8, 100m);
    }

    /// <summary>
    /// Verifies that no selection produces an instructional result rather than immune counts.
    /// </summary>
    [Fact]
    public void CalculateReturnsNoBucketsForEmptySelection()
    {
        TypeCoverageCalculation result = TypeCoverageCalculator.Calculate(CreateDataset(), TypeCoveragePolicy.Mixed, []);

        Assert.False(result.HasSelection);
        Assert.Empty(result.Buckets);
    }

    /// <summary>
    /// Verifies every single attacking type partitions each complete release population.
    /// </summary>
    [Fact]
    public void GeneratedReleaseDatasetPartitionsEveryPolicyForEveryType()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "type_coverage.json");
        using FileStream stream = File.OpenRead(path);
        TypeCoverageDataset dataset = TypeCoverageDatasetJson.Deserialize(stream);
        foreach (string type in PokemonTypeCatalog.StandardTypes)
        {
            AssertPopulation(TypeCoverageCalculator.Calculate(dataset, TypeCoveragePolicy.NormalOnly, [type]), dataset.NormalPoolSize);
            AssertPopulation(TypeCoverageCalculator.Calculate(dataset, TypeCoveragePolicy.CustomFusionsOnly, [type]), dataset.FusionPoolSize);
            AssertPopulation(TypeCoverageCalculator.Calculate(dataset, TypeCoveragePolicy.Mixed, [type]), (long)dataset.NormalPoolSize + dataset.FusionPoolSize);
        }
    }

    /// <summary>
    /// Verifies one expected calculated bucket.
    /// </summary>
    /// <param name="result">The calculation result.</param>
    /// <param name="effectiveness">The expected effectiveness.</param>
    /// <param name="count">The expected raw count.</param>
    /// <param name="percentage">The expected percentage.</param>
    private static void AssertBucket(TypeCoverageCalculation result, MoveEffectiveness effectiveness, long count, decimal percentage)
    {
        TypeCoverageBucketResult bucket = Assert.Single(result.Buckets, candidate => candidate.Effectiveness == effectiveness);
        Assert.Equal(count, bucket.Count);
        Assert.Equal(percentage, bucket.Percentage);
    }

    /// <summary>
    /// Verifies one calculation partitions its complete applicable population and percentage.
    /// </summary>
    /// <param name="result">The calculation result.</param>
    /// <param name="populationSize">The expected raw population size.</param>
    private static void AssertPopulation(TypeCoverageCalculation result, long populationSize)
    {
        Assert.Equal(6, result.Buckets.Count);
        Assert.Equal(populationSize, result.Buckets.Sum(bucket => bucket.Count));
        Assert.InRange(result.Buckets.Sum(bucket => bucket.Percentage), 99.999999999999999999999999m, 100.000000000000000000000001m);
    }

    /// <summary>
    /// Creates a small aggregate dataset whose category weights intentionally differ.
    /// </summary>
    /// <returns>The test dataset.</returns>
    private static TypeCoverageDataset CreateDataset()
    {
        return new()
        {
            SchemaVersion = TypeCoverageDatasetJson.SchemaVersion,
            GameVersion = "6.8.0",
            NormalPoolSize = 2,
            NormalPoolFingerprint = "0123456789abcdef",
            FusionPoolSchemaVersion = 2,
            FusionPoolSize = 6,
            FusionPoolFingerprint = "fedcba9876543210",
            Profiles =
            [
                new() { Types = ["WATER"], NormalCount = 1, FusionCount = 5 },
                new() { Types = ["GRASS"], NormalCount = 1, FusionCount = 1 }
            ]
        };
    }
}
