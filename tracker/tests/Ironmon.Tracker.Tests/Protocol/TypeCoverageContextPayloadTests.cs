namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies optional non-sensitive type-coverage context serialization.
/// </summary>
public sealed class TypeCoverageContextPayloadTests
{
    /// <summary>
    /// Initializes type-coverage context protocol tests.
    /// </summary>
    public TypeCoverageContextPayloadTests()
    {
    }

    /// <summary>
    /// Verifies current-state recovery round-trips only aggregate compatibility metadata.
    /// </summary>
    [Fact]
    public void CurrentStateRoundTripsCoverageContext()
    {
        TypeCoverageContextPayload coverage = new()
        {
            TrainerPolicy = "mixed",
            NormalPoolSize = 576,
            NormalPoolFingerprint = "5ab45fb7fa469aa5",
            FusionPoolSchemaVersion = 2,
            FusionPoolSize = 174_348,
            FusionPoolFingerprint = "a71c6c1c12491b47"
        };
        GameCurrentStatePayload state = new(true, "run-7", null, 12, typeCoverage: coverage);

        JsonElement json = TrackerJson.SerializePayload(state);
        GameCurrentStatePayload result = TrackerJson.DeserializePayload<GameCurrentStatePayload>(json);

        Assert.Equal("mixed", result.TypeCoverage?.TrainerPolicy);
        Assert.Equal(174_348, result.TypeCoverage?.FusionPoolSize);
        Assert.False(json.GetProperty("type_coverage").TryGetProperty("species", out _));
        Assert.False(json.GetProperty("type_coverage").TryGetProperty("seed", out _));
    }

    /// <summary>
    /// Verifies older current-state payloads remain valid without coverage metadata.
    /// </summary>
    [Fact]
    public void CurrentStateAcceptsAbsentCoverageContext()
    {
        using JsonDocument document = JsonDocument.Parse("""
            { "ironmon_active": true, "run_id": "run-7", "battle_id": null, "sequence": 12 }
            """);

        GameCurrentStatePayload result = TrackerJson.DeserializePayload<GameCurrentStatePayload>(document.RootElement);

        Assert.Null(result.TypeCoverage);
    }

    /// <summary>
    /// Verifies protocol context conversion recognizes all supported policies.
    /// </summary>
    [Theory]
    [InlineData("mixed", TypeCoveragePolicy.Mixed)]
    [InlineData("custom_fusions_only", TypeCoveragePolicy.CustomFusionsOnly)]
    [InlineData("normal_only", TypeCoveragePolicy.NormalOnly)]
    [InlineData("future_policy", TypeCoveragePolicy.Unknown)]
    public void ContextFactoryMapsTrainerPolicy(string policy, TypeCoveragePolicy expected)
    {
        GameHandshakePayload game = new("6.8.0", "0.7.5", true, false, "C:\\Game", "run-7", null);
        TypeCoverageContextPayload coverage = new()
        {
            TrainerPolicy = policy,
            NormalPoolSize = 576,
            NormalPoolFingerprint = "5ab45fb7fa469aa5",
            FusionPoolSchemaVersion = 2,
            FusionPoolSize = 174_348,
            FusionPoolFingerprint = "a71c6c1c12491b47"
        };

        GameCurrentStatePayload state = new(true, "run-7", null, 12, typeCoverage: coverage);
        TypeCoverageContext? context = TrackerTypeCoverageContextFactory.Create(game, state);

        Assert.Equal(expected, context?.Policy);
    }
}
