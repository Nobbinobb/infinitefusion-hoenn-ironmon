using System.Text.Json;
using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Checks production fusion totals against the independently sampled design examples.
/// </summary>
public sealed class FusionBaseStatsTests
{
    private const string _fixtureFile = "fusion-stats-v2.json";
    private const string _versionThreeFixtureFile = "fusion-stats-v3.json";
    private const string _seedProperty = "seed";
    private const string _resultProperty = "resultId";
    private const string _bodyProperty = "bodyStats";
    private const string _headProperty = "headStats";
    private const string _statsProperty = "stats";

    /// <summary>
    /// Reproduces all calibrated fusion examples, including unequal material inputs.
    /// </summary>
    [Fact]
    public void FixedFormulaMatchesIndependentExamples()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, _fixtureFile)));
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            int[] body = [.. row.GetProperty(_bodyProperty).EnumerateArray().Select(value => value.GetInt32())];
            int[] head = [.. row.GetProperty(_headProperty).EnumerateArray().Select(value => value.GetInt32())];
            int expected = row.GetProperty(_statsProperty).EnumerateArray().Sum(value => value.GetInt32());
            Assert.Equal(expected, FusionBaseStats.Total(row.GetProperty(_seedProperty).GetInt64(), row.GetProperty(_resultProperty).GetInt32(), body, head, 2));
        }
    }

    /// <summary>
    /// Reproduces the selected 85/75/two-thirds vectors recorded by the independently audited native experiment.
    /// </summary>
    [Fact]
    public void VersionThreeMatchesAuditedNativeExamples()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, _versionThreeFixtureFile)));
        int differingHistoricalTotals = 0;
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            long seed = row.GetProperty(_seedProperty).GetInt64();
            int resultId = row.GetProperty(_resultProperty).GetInt32();
            int[] body = [.. row.GetProperty(_bodyProperty).EnumerateArray().Select(value => value.GetInt32())];
            int[] head = [.. row.GetProperty(_headProperty).EnumerateArray().Select(value => value.GetInt32())];
            int expected = row.GetProperty(_statsProperty).EnumerateArray().Sum(value => value.GetInt32());
            Assert.Equal(expected, FusionBaseStats.Total(seed, resultId, body, head, 3));
            if (expected != FusionBaseStats.Total(seed, resultId, body, head, 2))
                differingHistoricalTotals++;
        }

        Assert.True(differingHistoricalTotals > 0);
    }

    /// <summary>
    /// Rejects unsupported versions instead of silently applying a retained formula to an unknown run.
    /// </summary>
    /// <param name="version">The unsupported fusion formula identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void UnknownFormulaIsRejected(int version)
    {
        int[] stats = [100, 100, 100, 100, 100, 100];
        Assert.Throws<ArgumentOutOfRangeException>(() => FusionBaseStats.Total(42, 75744, stats, stats, version));
    }

    /// <summary>
    /// Retains the original per-term flooring independently of seed or fusion identity.
    /// </summary>
    [Fact]
    public void HistoricalFormulaKeepsNativeFlooring()
    {
        int[] body = [90, 126, 122, 22, 73, 102];
        int[] head = [101, 132, 18, 119, 132, 16];
        Assert.Equal(583, FusionBaseStats.Total(1279932942, 75744, body, head, 1));
        Assert.Equal(583, FusionBaseStats.Total(42, 90000, body, head, 1));
    }
}
