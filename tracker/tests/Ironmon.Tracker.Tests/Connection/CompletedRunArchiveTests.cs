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
        Assert.Equal(1, stored.EvolutionGeneratorVersion);
        Assert.Equal("fusion-evolution-targets", stored.FusionEvolutionTargetPoolFingerprint);
        Assert.Equal(4, Assert.Single(stored.MoveAccessMetrics!.Encounters).LevelOneMoveCount);
        Assert.Equal("TACKLE", Assert.Single(stored.MoveAccessMetrics.MoveUses).MoveId);
        EvolutionMetricPayload evolution = Assert.Single(stored.EvolutionMetrics!.Events);
        Assert.Equal("BULBASAUR", evolution.SourceSpeciesId);
        Assert.Equal(EvolutionMetricIdentifiers.CompletedOutcome, evolution.Outcome);
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
    /// Verifies that an unsupported evolution metrics schema is rejected explicitly.
    /// </summary>
    [Fact]
    public void StoreRejectsUnsupportedEvolutionMetricsSchema()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));
        CompletedRunRecipePayload recipe = CreateRecipe("run-bad-evolution-metrics", evolutionMetricsSchemaVersion: 99);

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(recipe));
    }

    /// <summary>
    /// Verifies that evolution metrics cannot bypass generator validation in a legacy move-access recipe.
    /// </summary>
    [Fact]
    public void StoreRejectsEvolutionMetricsWithoutEvolutionMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));
        CompletedRunRecipePayload recipe = CreateRecipe("run-orphaned-evolution-metrics", null, includeMoveAccessMetadata: false, includeEvolutionMetadata: false, includeEvolutionMetrics: true);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => archive.Store(recipe));

        Assert.Contains("Evolution metrics", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that evolution fingerprints cannot be persisted without their generator version.
    /// </summary>
    [Fact]
    public void StoreRejectsPartialEvolutionMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));
        CompletedRunRecipePayload recipe = CreateRecipe("run-partial-evolutions", partialEvolutionMetadata: true);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => archive.Store(recipe));

        Assert.Contains("generator version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a recipe predating evolution randomization remains loadable.
    /// </summary>
    [Fact]
    public void StoreAcceptsLegacyRecipeWithoutEvolutionMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));

        archive.Store(CreateRecipe("run-legacy-evolutions", includeEvolutionMetadata: false));

        Assert.Null(Assert.Single(archive.Recipes).EvolutionGeneratorVersion);
    }

    /// <summary>
    /// Creates a valid compact completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable test run identifier.</param>
    /// <param name="moveAccessGeneratorVersion">The optional move-access generator version.</param>
    /// <param name="metricsSchemaVersion">The local metrics schema version.</param>
    /// <param name="includeMoveAccessMetadata">Whether move-access metadata and metrics are included.</param>
    /// <param name="includeEvolutionMetadata">Whether current evolution metadata is included.</param>
    /// <param name="partialEvolutionMetadata">Whether evolution fingerprints intentionally omit their generator version.</param>
    /// <param name="evolutionMetricsSchemaVersion">The evolution metrics schema version.</param>
    /// <param name="includeEvolutionMetrics">Whether evolution metrics are included.</param>
    /// <returns>The recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId, int? moveAccessGeneratorVersion = 6, int metricsSchemaVersion = MoveAccessMetricIdentifiers.SchemaVersion, bool includeMoveAccessMetadata = true, bool includeEvolutionMetadata = true, bool partialEvolutionMetadata = false, int evolutionMetricsSchemaVersion = EvolutionMetricIdentifiers.SchemaVersion, bool? includeEvolutionMetrics = null) => new()
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
        EvolutionGeneratorVersion = partialEvolutionMetadata ? null : includeEvolutionMetadata ? 1 : null,
        EvolutionRulesVersion = includeEvolutionMetadata ? 1 : null,
        EvolutionSourceFingerprint = includeEvolutionMetadata ? "evolution-source" : null,
        EvolutionTaxonomyFingerprint = includeEvolutionMetadata ? "evolution-taxonomy" : null,
        EvolutionMethodFingerprint = includeEvolutionMetadata ? "evolution-methods" : null,
        EvolutionTargetFingerprint = includeEvolutionMetadata ? "evolution-targets" : null,
        EvolutionBaseStatGeneratorVersion = includeEvolutionMetadata ? 1 : null,
        EvolutionBaseStatSourceFingerprint = includeEvolutionMetadata ? "evolution-stats" : null,
        FusionEvolutionGeneratorVersion = includeEvolutionMetadata ? 1 : null,
        FusionEvolutionRulesVersion = includeEvolutionMetadata ? 1 : null,
        FusionEvolutionTargetPoolVersion = includeEvolutionMetadata ? 2 : null,
        FusionEvolutionTargetPoolSize = includeEvolutionMetadata ? 100 : null,
        FusionEvolutionTargetPoolFingerprint = includeEvolutionMetadata ? "fusion-evolution-targets" : null,
        MoveAccessGeneratorVersion = moveAccessGeneratorVersion,
        MovePoolFingerprint = includeMoveAccessMetadata ? "moves" : null,
        MoveContextualRestrictionFingerprint = includeMoveAccessMetadata ? "move-restrictions" : null,
        MoveSourceFingerprint = includeMoveAccessMetadata ? "move-source" : null,
        EggMoveSourceFingerprint = includeMoveAccessMetadata ? "egg-source" : null,
        TmRosterFingerprint = includeMoveAccessMetadata ? "tm-roster" : null,
        TmSourceFingerprint = includeMoveAccessMetadata ? "tm-source" : null,
        TrRosterFingerprint = includeMoveAccessMetadata ? "tr-roster" : null,
        TrSourceFingerprint = includeMoveAccessMetadata ? "tr-source" : null,
        TutorCatalogFingerprint = includeMoveAccessMetadata ? "tutor-catalog" : null,
        TutorSourceFingerprint = includeMoveAccessMetadata ? "tutor-source" : null,
        FusionTutorCatalogFingerprint = includeMoveAccessMetadata ? "fusion-tutor-catalog" : null,
        FusionTutorSourceFingerprint = includeMoveAccessMetadata ? "fusion-tutor-source" : null,
        MoveAccessMetrics = includeMoveAccessMetadata ? new MoveAccessMetricsPayload
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
        } : null,
        EvolutionMetrics = (includeEvolutionMetrics ?? includeEvolutionMetadata) ? new EvolutionMetricsPayload
        {
            SchemaVersion = evolutionMetricsSchemaVersion,
            Events =
            [
                new EvolutionMetricPayload
                {
                    EventId = 1,
                    PokemonId = "123",
                    SourceKind = "normal",
                    SourceSpeciesId = "BULBASAUR",
                    SourceSpeciesName = "Bulbasaur",
                    TargetSpeciesId = "IVYSAUR",
                    TargetSpeciesName = "Ivysaur",
                    Level = 16,
                    ActivationContext = "level_up",
                    EffectiveMethod = "Level",
                    EffectiveParameter = "16",
                    SourceBst = 318,
                    ReferenceBst = 405,
                    TargetBst = 405,
                    Outcome = EvolutionMetricIdentifiers.CompletedOutcome
                }
            ]
        } : null
    };
}
