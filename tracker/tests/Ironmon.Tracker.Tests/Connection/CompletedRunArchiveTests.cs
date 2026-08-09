using System.Text.Json;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies compact completed-run recipe persistence.
/// </summary>
public sealed class CompletedRunArchiveTests
{
    /// <summary>
    /// Initializes completed-run archive tests.
    /// </summary>
    public CompletedRunArchiveTests()
    {
    }

    /// <summary>
    /// Verifies that a stored recipe survives archive reconstruction without lookup results.
    /// </summary>
    [Fact]
    public void StorePersistsOnlyCompletedRunRecipe()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        TrackerKnowledgeOptions options = new(root);
        CompletedRunArchive archive = new(options);
        CompletedRunRecipePayload recipe = CreateRecipe("run-archive");

        archive.Store(recipe);

        CompletedRunArchive reloaded = new(options);
        CompletedRunRecipePayload stored = Assert.Single(reloaded.Recipes);
        Assert.Equal("run-archive", stored.RunId);
        Assert.Equal(98765, stored.Seed);
        Assert.Equal(6, stored.MoveAccessGeneratorVersion);
        Assert.Equal("fusion-tutor-source", stored.FusionTutorSourceFingerprint);
        Assert.Equal(4, Assert.Single(stored.MoveAccessMetrics!.Encounters).LevelOneMoveCount);
        Assert.Equal("TACKLE", Assert.Single(stored.MoveAccessMetrics.MoveUses).MoveId);
        string recipePath = Path.Combine(root, "runs", "run-archive", "recipe.json");
        Assert.True(File.Exists(recipePath));
        Assert.DoesNotContain("lookup", File.ReadAllText(recipePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that move-access fingerprints cannot be persisted without their generator version.
    /// </summary>
    [Fact]
    public void StoreRejectsPartialMoveAccessMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        TrackerKnowledgeOptions options = new(root);
        CompletedRunArchive archive = new(options);
        CompletedRunRecipePayload recipe = CreateRecipe("run-partial-moves", null);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => archive.Store(recipe));

        Assert.Contains("generator version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an unsupported local metrics schema is rejected explicitly.
    /// </summary>
    [Fact]
    public void StoreRejectsUnsupportedMoveAccessMetricsSchema()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));
        CompletedRunRecipePayload recipe = CreateRecipe("run-bad-metrics", metricsSchemaVersion: 99);

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(recipe));
    }

    /// <summary>
    /// Creates a valid compact completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable test run identifier.</param>
    /// <param name="moveAccessGeneratorVersion">The optional move-access generator version.</param>
    /// <param name="metricsSchemaVersion">The local metrics schema version.</param>
    /// <returns>The recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId, int? moveAccessGeneratorVersion = 6, int metricsSchemaVersion = MoveAccessMetricIdentifiers.SchemaVersion) => new()
    {
        RunId = runId,
        Seed = 98765,
        Result = "lost",
        GameVersion = "6.8.0",
        IronmonVersion = "0.3.3",
        Configuration = JsonSerializer.SerializeToElement(new { wild_policy = "mixed", trainer_policy = "mixed" }),
        SpeciesGeneratorVersion = 1,
        AbilityGeneratorVersion = 3,
        PlayerFusionGeneratorVersion = 2,
        SpeciesPoolFingerprint = "species",
        AbilityPoolFingerprint = "abilities",
        FusionPoolFingerprint = "fusions",
        MoveAccessGeneratorVersion = moveAccessGeneratorVersion,
        MovePoolFingerprint = "moves",
        MoveContextualRestrictionFingerprint = "move-restrictions",
        MoveSourceFingerprint = "move-source",
        EggMoveSourceFingerprint = "egg-source",
        TmRosterFingerprint = "tm-roster",
        TmSourceFingerprint = "tm-source",
        TrRosterFingerprint = "tr-roster",
        TrSourceFingerprint = "tr-source",
        TutorCatalogFingerprint = "tutor-catalog",
        TutorSourceFingerprint = "tutor-source",
        FusionTutorCatalogFingerprint = "fusion-tutor-catalog",
        FusionTutorSourceFingerprint = "fusion-tutor-source",
        MoveAccessMetrics = new MoveAccessMetricsPayload
        {
            SchemaVersion = metricsSchemaVersion,
            Encounters =
            [
                new MoveAccessEncounterMetricPayload
                {
                    SpeciesId = "BULBASAUR",
                    SpeciesName = "Bulbasaur",
                    Level = 5,
                    Side = MoveAccessMetricIdentifiers.PlayerSide,
                    EncounterCount = 1,
                    LevelOneMoveCount = 4,
                    LevelOneDamagingMoveCount = 1,
                    LevelOneGuaranteeSatisfied = true
                }
            ],
            MoveUses =
            [
                new MoveUseMetricPayload
                {
                    Side = MoveAccessMetricIdentifiers.PlayerSide,
                    SpeciesId = "BULBASAUR",
                    SpeciesName = "Bulbasaur",
                    MoveId = "TACKLE",
                    MoveName = "Tackle",
                    Count = 2
                }
            ]
        }
    };
}
