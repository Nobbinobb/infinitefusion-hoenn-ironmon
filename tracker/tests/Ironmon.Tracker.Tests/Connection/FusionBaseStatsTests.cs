using System.Text.Json;
using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Checks production fusion totals against the independently sampled design examples.
/// </summary>
public sealed class FusionBaseStatsTests
{
    private const string _fixtureFile = "fusion-stats-v2.json";
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
