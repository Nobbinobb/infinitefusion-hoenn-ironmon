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
    /// Verifies that a stored nested recipe survives archive reconstruction without lookup results.
    /// </summary>
    [Fact]
    public void StorePersistsOnlyCompletedRunRecipe()
    {
        string root = CreateRoot();
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));
        archive.Store(CreateRecipe("run-archive"));

        CompletedRunRecipePayload stored = Assert.Single(new CompletedRunArchive(new TrackerKnowledgeOptions(root)).Recipes);

        Assert.Equal("run-archive", stored.RunId);
        Assert.Equal(98765, stored.Seed);
        Assert.Equal(6, stored.MoveAccessGenerator!.Version);
        Assert.Equal("fusion-tutor-source", stored.MoveAccessGenerator.FusionTutor.SourceFingerprint);
        Assert.Equal(1, stored.EvolutionGenerator!.Version);
        Assert.Equal("fusion-evolution-targets", stored.EvolutionGenerator.Fusion.TargetPool.Fingerprint);
        Assert.Equal(4, Assert.Single(stored.MoveAccessMetrics!.Encounters).LevelOneMoveCount);
        Assert.Equal("TACKLE", Assert.Single(stored.MoveAccessMetrics.MoveUses).MoveId);
        Assert.Equal(EvolutionMetricIdentifiers.CompletedOutcome, Assert.Single(stored.EvolutionMetrics!.Events).Outcome);
        string recipePath = Path.Combine(root, "runs", "run-archive", "recipe.json");
        Assert.True(File.Exists(recipePath));
        Assert.DoesNotContain("lookup", File.ReadAllText(recipePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that move metrics cannot be persisted without their generator manifest.
    /// </summary>
    [Fact]
    public void StoreRejectsMoveMetricsWithoutGenerator()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));
        CompletedRunRecipePayload recipe = CreateRecipe("run-orphaned-moves", includeMoveGenerator: false, includeMoveMetrics: true);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => archive.Store(recipe));

        Assert.Contains("Move-access metrics", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an unsupported local metrics schema is rejected explicitly.
    /// </summary>
    [Fact]
    public void StoreRejectsUnsupportedMoveAccessMetricsSchema()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(CreateRecipe("run-bad-metrics", moveMetricsSchemaVersion: 99)));
    }

    /// <summary>
    /// Verifies that evolution metrics cannot be persisted without their generator manifest.
    /// </summary>
    [Fact]
    public void StoreRejectsEvolutionMetricsWithoutGenerator()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));
        CompletedRunRecipePayload recipe = CreateRecipe("run-orphaned-evolutions", includeEvolutionGenerator: false, includeEvolutionMetrics: true);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => archive.Store(recipe));

        Assert.Contains("Evolution metrics", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an unsupported recipe schema is rejected instead of being migrated implicitly.
    /// </summary>
    [Fact]
    public void StoreRejectsUnsupportedRecipeSchema()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(CreateRecipe("run-old-schema", schemaVersion: 0)));
    }

    /// <summary>
    /// Verifies that a run which did not enable optional generators remains valid.
    /// </summary>
    [Fact]
    public void StoreAcceptsRecipeWithoutOptionalGenerators()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));

        archive.Store(CreateRecipe("run-minimal", includeMoveGenerator: false, includeMoveMetrics: false, includeEvolutionGenerator: false, includeEvolutionMetrics: false));

        CompletedRunRecipePayload stored = Assert.Single(archive.Recipes);
        Assert.Null(stored.MoveAccessGenerator);
        Assert.Null(stored.EvolutionGenerator);
    }

    /// <summary>
    /// Verifies that legacy flat recipes are ignored instead of being interpreted as the new contract.
    /// </summary>
    [Fact]
    public void LoadIgnoresLegacyFlatRecipe()
    {
        string root = CreateRoot();
        string runDirectory = Path.Combine(root, "runs", "legacy-run");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "recipe.json"), """
            {
              "run_id": "legacy-run",
              "seed": 123,
              "result": "lost",
              "game_version": "6.8.0",
              "ironmon_version": "0.6.1",
              "species_generator_version": 3,
              "ability_generator_version": 3,
              "player_fusion_generator_version": 2
            }
            """);

        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(root));

        Assert.Empty(archive.Recipes);
        Assert.NotNull(archive.LastError);
        Assert.Contains("Ignored incompatible completed-run recipe", archive.LastError, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a unique archive root.
    /// </summary>
    /// <returns>The temporary root.</returns>
    private static string CreateRoot()
        => Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates a valid nested completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable test run identifier.</param>
    /// <param name="schemaVersion">The recipe schema version.</param>
    /// <param name="moveMetricsSchemaVersion">The move metrics schema version.</param>
    /// <param name="includeMoveGenerator">Whether the move generator manifest is included.</param>
    /// <param name="includeMoveMetrics">Whether move metrics are included.</param>
    /// <param name="includeEvolutionGenerator">Whether the evolution generator manifest is included.</param>
    /// <param name="includeEvolutionMetrics">Whether evolution metrics are included.</param>
    /// <returns>The recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId, int schemaVersion = 1, int moveMetricsSchemaVersion = MoveAccessMetricIdentifiers.SchemaVersion, bool includeMoveGenerator = true, bool includeMoveMetrics = true, bool includeEvolutionGenerator = true, bool includeEvolutionMetrics = true) => new()
    {
        SchemaVersion = schemaVersion,
        RunId = runId,
        Seed = 98765,
        Result = "lost",
        GameVersion = "6.8.0",
        IronmonVersion = "0.3.3",
        Configuration = new RunConfigurationPayload { SchemaVersion = 1, WildPolicy = "mixed", TrainerPolicy = "mixed", UnfusionSetting = "random_component" },
        SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = "species" },
        AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 3, PoolSize = 100, PoolFingerprint = "abilities" },
        BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = "base-stats" },
        EvolutionGenerator = includeEvolutionGenerator ? CreateEvolutionGenerator() : null,
        MoveAccessGenerator = includeMoveGenerator ? CreateMoveGenerator() : null,
        PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 2, PoolSize = 100, PoolFingerprint = "fusions" },
        MoveAccessMetrics = includeMoveMetrics ? CreateMoveMetrics(moveMetricsSchemaVersion) : null,
        EvolutionMetrics = includeEvolutionMetrics ? CreateEvolutionMetrics() : null
    };

    /// <summary>
    /// Creates valid evolution-generator metadata.
    /// </summary>
    /// <returns>The generator metadata.</returns>
    private static EvolutionGeneratorRecipePayload CreateEvolutionGenerator() => new()
    {
        Version = 1,
        RulesVersion = 1,
        SourceFingerprint = "evolution-source",
        TaxonomyFingerprint = "evolution-taxonomy",
        MethodFingerprint = "evolution-methods",
        TargetFingerprint = "evolution-targets",
        BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = "evolution-stats" },
        Fusion = new FusionEvolutionGeneratorRecipePayload
        {
            Version = 1,
            RulesVersion = 1,
            TargetPool = new VersionedPoolRecipePayload { Version = 2, Size = 100, Fingerprint = "fusion-evolution-targets" }
        }
    };

    /// <summary>
    /// Creates valid move-access-generator metadata.
    /// </summary>
    /// <returns>The generator metadata.</returns>
    private static MoveAccessGeneratorRecipePayload CreateMoveGenerator() => new()
    {
        Version = 6,
        PoolFingerprint = "moves",
        ContextualRestrictionFingerprint = "move-restrictions",
        LevelUpSourceFingerprint = "move-source",
        EggSourceFingerprint = "egg-source",
        Tm = new MachineSourceRecipePayload { RosterFingerprint = "tm-roster", SourceFingerprint = "tm-source" },
        Tr = new MachineSourceRecipePayload { RosterFingerprint = "tr-roster", SourceFingerprint = "tr-source" },
        Tutor = new TutorSourceRecipePayload { CatalogFingerprint = "tutor-catalog", SourceFingerprint = "tutor-source" },
        FusionTutor = new TutorSourceRecipePayload { CatalogFingerprint = "fusion-tutor-catalog", SourceFingerprint = "fusion-tutor-source" }
    };

    /// <summary>
    /// Creates move-access metrics.
    /// </summary>
    /// <param name="schemaVersion">The metrics schema version.</param>
    /// <returns>The metrics.</returns>
    private static MoveAccessMetricsPayload CreateMoveMetrics(int schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
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
    };

    /// <summary>
    /// Creates evolution metrics.
    /// </summary>
    /// <returns>The metrics.</returns>
    private static EvolutionMetricsPayload CreateEvolutionMetrics() => new()
    {
        SchemaVersion = EvolutionMetricIdentifiers.SchemaVersion,
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
    };
}
