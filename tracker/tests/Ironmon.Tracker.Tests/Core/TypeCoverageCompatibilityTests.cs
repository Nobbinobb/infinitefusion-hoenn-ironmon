namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies connected coverage compatibility and policy-specific population requirements.
/// </summary>
public sealed class TypeCoverageCompatibilityTests
{
    /// <summary>
    /// Initializes compatibility tests.
    /// </summary>
    public TypeCoverageCompatibilityTests()
    {
    }

    /// <summary>
    /// Verifies that older games without context remain connected but cannot calculate coverage.
    /// </summary>
    [Fact]
    public void EvaluateReportsMissingOptionalContext()
        => Assert.Equal(TypeCoverageCompatibilityStatus.MissingContext, TypeCoverageCompatibility.Evaluate(CreateDataset(), null));

    /// <summary>
    /// Verifies that Normal Only ignores an unused custom-fusion population mismatch.
    /// </summary>
    [Fact]
    public void EvaluateNormalOnlyRequiresOnlyNormalPopulation()
    {
        TypeCoverageContext context = CreateContext(TypeCoveragePolicy.NormalOnly, fusionFingerprint: "0000000000000000");

        Assert.Equal(TypeCoverageCompatibilityStatus.Compatible, TypeCoverageCompatibility.Evaluate(CreateDataset(), context));
    }

    /// <summary>
    /// Verifies that Fusion Only ignores an unused normal population mismatch.
    /// </summary>
    [Fact]
    public void EvaluateFusionOnlyRequiresOnlyFusionPopulation()
    {
        TypeCoverageContext context = CreateContext(TypeCoveragePolicy.CustomFusionsOnly, normalFingerprint: "0000000000000000");

        Assert.Equal(TypeCoverageCompatibilityStatus.Compatible, TypeCoverageCompatibility.Evaluate(CreateDataset(), context));
    }

    /// <summary>
    /// Verifies that Mixed rejects either mismatched population.
    /// </summary>
    [Fact]
    public void EvaluateMixedRequiresBothPopulations()
    {
        TypeCoverageContext normalMismatch = CreateContext(TypeCoveragePolicy.Mixed, normalFingerprint: "0000000000000000");
        TypeCoverageContext fusionMismatch = CreateContext(TypeCoveragePolicy.Mixed, fusionFingerprint: "0000000000000000");

        Assert.Equal(TypeCoverageCompatibilityStatus.NormalPoolMismatch, TypeCoverageCompatibility.Evaluate(CreateDataset(), normalMismatch));
        Assert.Equal(TypeCoverageCompatibilityStatus.FusionPoolMismatch, TypeCoverageCompatibility.Evaluate(CreateDataset(), fusionMismatch));
    }

    /// <summary>
    /// Verifies that a different game release cannot reuse the packaged type data.
    /// </summary>
    [Fact]
    public void EvaluateRejectsDifferentGameVersion()
    {
        TypeCoverageContext context = CreateContext(TypeCoveragePolicy.Mixed, gameVersion: "6.8.1");

        Assert.Equal(TypeCoverageCompatibilityStatus.GameVersionMismatch, TypeCoverageCompatibility.Evaluate(CreateDataset(), context));
    }

    /// <summary>
    /// Creates fixed aggregate release data.
    /// </summary>
    /// <returns>The dataset under test.</returns>
    private static TypeCoverageDataset CreateDataset()
    {
        return new()
        {
            SchemaVersion = 1,
            GameVersion = "6.8.0",
            NormalPoolSize = 2,
            NormalPoolFingerprint = "0123456789abcdef",
            FusionPoolSchemaVersion = 2,
            FusionPoolSize = 3,
            FusionPoolFingerprint = "fedcba9876543210",
            Profiles = [new() { Types = ["NORMAL"], NormalCount = 2, FusionCount = 3 }]
        };
    }

    /// <summary>
    /// Creates connected population metadata with optional mismatches.
    /// </summary>
    /// <param name="policy">The trainer policy.</param>
    /// <param name="gameVersion">The connected game version.</param>
    /// <param name="normalFingerprint">The connected normal fingerprint.</param>
    /// <param name="fusionFingerprint">The connected fusion fingerprint.</param>
    /// <returns>The connected context.</returns>
    private static TypeCoverageContext CreateContext(TypeCoveragePolicy policy, string gameVersion = "6.8.0", string normalFingerprint = "0123456789abcdef", string fusionFingerprint = "fedcba9876543210")
        => new(gameVersion, policy, 2, normalFingerprint, 2, 3, fusionFingerprint);
}
