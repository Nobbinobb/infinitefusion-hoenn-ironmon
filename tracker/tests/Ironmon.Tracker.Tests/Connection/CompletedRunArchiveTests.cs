namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies compact completed-run recipe persistence.
/// </summary>
public sealed class CompletedRunArchiveTests : IDisposable
{
    private readonly List<string> _roots = [];

    /// <summary>
    /// Initializes completed-run archive tests.
    /// </summary>
    public CompletedRunArchiveTests()
    {
    }

    /// <summary>
    /// Removes every temporary archive root owned by the current test.
    /// </summary>
    public void Dispose()
    {
        foreach (string root in _roots)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
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
        Assert.Equal(14, stored.Statistics!.ItemsUsed);
        Assert.Equal("HYPERPOTION", stored.ItemMappings["POTION"]);
        Assert.Equal("TM02", stored.TmMappings["TM01"]);
        Assert.Equal(600, stored.ItemGenerator!.GroundPoolSize);
        Assert.Equal(600, stored.ItemGenerator.GroundTotalWeight);
        Assert.Equal(["DNASPLICERS", "DYNAMITE"], stored.ItemGenerator.ResultBans);
        Assert.Equal("run-archive", archive.RequestedRunId);
        string recipePath = Path.Combine(root, "runs", "run-archive", "recipe.json");
        Assert.True(File.Exists(recipePath));
        Assert.DoesNotContain("lookup", File.ReadAllText(recipePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that recipes archived before analysis removal remain readable.
    /// </summary>
    [Fact]
    public void LoadIgnoresRemovedAnalysisFields()
    {
        string root = CreateRoot();
        string runDirectory = Path.Combine(root, "runs", "run-old-analysis");
        Directory.CreateDirectory(runDirectory);
        string json = JsonSerializer.Serialize(CreateRecipe("run-old-analysis"), TrackerJson.Options);
        string oldAnalysis = ",\"move_access_metrics\":{\"schema_version\":1},\"evolution_metrics\":{\"schema_version\":1}";
        File.WriteAllText(Path.Combine(runDirectory, "recipe.json"), json.Insert(json.Length - 1, oldAnalysis));

        CompletedRunRecipePayload stored = Assert.Single(new CompletedRunArchive(new TrackerKnowledgeOptions(root)).Recipes);

        Assert.Equal("run-old-analysis", stored.RunId);
    }

    /// <summary>
    /// Verifies that each stored completion requests selection of that exact run.
    /// </summary>
    [Fact]
    public void StoreRequestsNewestCompletedRunSelection()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));
        int requests = 0;
        archive.SelectionRequested += (_, _) => requests++;
        archive.Store(CreateRecipe("run-first"));
        archive.Store(CreateRecipe("run-second"));
        archive.Store(CreateRecipe("run-second"));

        Assert.Equal("run-second", archive.RequestedRunId);
        Assert.Equal("run-second", archive.Recipes[0].RunId);
        Assert.Equal(2, requests);
    }

    /// <summary>
    /// Verifies that automatic resets archive a completed run without selecting it for preparation.
    /// </summary>
    [Fact]
    public void StoreCanSuppressCompletedRunSelection()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));
        int requests = 0;
        archive.SelectionRequested += (_, _) => requests++;

        archive.Store(CreateRecipe("run-automatic-reset"), requestSelection: false);

        Assert.Equal("run-automatic-reset", Assert.Single(archive.Recipes).RunId);
        Assert.Null(archive.RequestedRunId);
        Assert.Equal(0, requests);
    }

    /// <summary>
    /// Verifies connected save-slot totals override an older archived lineage using the same slot name.
    /// </summary>
    [Fact]
    public void CurrentSaveSlotTotalsOverrideOlderArchiveTotals()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));
        archive.Store(CreateRecipe("old-file-a"));
        RunStatisticsPayload recreatedSaveCompletion = new()
        {
            SchemaVersion = 1,
            AttemptNumber = 2,
            SaveSlot = "File A",
            Result = "lost",
            AttemptsStarted = 2,
            AttemptsLost = 1
        };
        archive.Store(CreateRecipe("new-file-a", statistics: recreatedSaveCompletion));

        KeyValuePair<string, RunStatisticsPayload> archivedSlot = Assert.Single(archive.GetLatestSaveSlotStatistics(null));

        Assert.Same(recreatedSaveCompletion, archivedSlot.Value);
        RunStatisticsPayload current = new()
        {
            SchemaVersion = 1,
            AttemptNumber = 3,
            SaveSlot = "File A",
            Result = "active",
            AttemptsStarted = 3,
            AttemptsLost = 2
        };

        KeyValuePair<string, RunStatisticsPayload> slot = Assert.Single(archive.GetLatestSaveSlotStatistics(current));

        Assert.Equal("File A", slot.Key);
        Assert.Same(current, slot.Value);
        Assert.Equal(3, slot.Value.AttemptsStarted);
        Assert.Equal(2, slot.Value.AttemptsLost);
    }

    /// <summary>
    /// Verifies that an unsupported statistics schema is rejected explicitly.
    /// </summary>
    [Fact]
    public void StoreRejectsUnsupportedStatisticsSchema()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));
        CompletedRunRecipePayload recipe = CreateRecipe("run-bad-statistics", statisticsSchemaVersion: 99);

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(recipe));
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
    /// Verifies that malformed item generator metadata is rejected explicitly.
    /// </summary>
    [Fact]
    public void StoreRejectsMalformedItemGenerator()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(CreateRecipe("run-bad-items", itemRulesVersion: 0)));
    }

    /// <summary>
    /// Verifies that weighted rules require a positive ticket total.
    /// </summary>
    [Fact]
    public void StoreRejectsWeightedItemsWithoutTickets()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.Store(CreateRecipe("run-bad-item-weights", itemRulesVersion: 3, itemGroundTotalWeight: 0)));
    }

    /// <summary>
    /// Verifies that a run which did not enable optional generators remains valid.
    /// </summary>
    [Fact]
    public void StoreAcceptsRecipeWithoutOptionalGenerators()
    {
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(CreateRoot()));

        archive.Store(CreateRecipe("run-minimal", includeMoveGenerator: false, includeEvolutionGenerator: false));

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
    private string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        return root;
    }

    /// <summary>
    /// Creates a valid nested completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable test run identifier.</param>
    /// <param name="schemaVersion">The recipe schema version.</param>
    /// <param name="includeMoveGenerator">Whether the move generator manifest is included.</param>
    /// <param name="includeEvolutionGenerator">Whether the evolution generator manifest is included.</param>
    /// <param name="statisticsSchemaVersion">The authoritative statistics schema version.</param>
    /// <param name="itemRulesVersion">The item pool rules version.</param>
    /// <param name="itemGroundTotalWeight">An optional ground-selection ticket total.</param>
    /// <param name="statistics">Optional authoritative attempt statistics.</param>
    /// <returns>The recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId, int schemaVersion = 1, bool includeMoveGenerator = true, bool includeEvolutionGenerator = true, int statisticsSchemaVersion = 1, int itemRulesVersion = 1, int? itemGroundTotalWeight = null, RunStatisticsPayload? statistics = null) => new()
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
        ItemGenerator = new ItemGeneratorRecipePayload
        {
            Version = 1,
            RulesVersion = itemRulesVersion,
            GroundPoolSize = 600,
            GroundTotalWeight = itemGroundTotalWeight ?? (itemRulesVersion >= 3 ? 5253 : 600),
            GroundPoolFingerprint = "ground-items",
            TmPoolSize = 124,
            TmPoolFingerprint = "tm-items",
            ResultBans = ["DNASPLICERS", "DYNAMITE"],
            ResultBanFingerprint = "item-bans",
            ShopPolicyVersion = 1
        },
        ItemMappings = new Dictionary<string, string> { ["POTION"] = "HYPERPOTION" },
        TmMappings = new Dictionary<string, string> { ["TM01"] = "TM02" },
        Statistics = statistics ?? CreateStatistics(statisticsSchemaVersion)
    };

    /// <summary>
    /// Creates authoritative attempt statistics.
    /// </summary>
    /// <param name="schemaVersion">The statistics schema version.</param>
    /// <returns>The statistics payload.</returns>
    private static RunStatisticsPayload CreateStatistics(int schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
        AttemptNumber = 4,
        SaveSlot = "File A",
        Seed = 98765,
        Result = "lost",
        ActiveSeconds = 3723,
        AttemptsStarted = 4,
        AttemptsLost = 3,
        AttemptsWon = 0,
        AttemptsAbandoned = 1,
        BattlesCompleted = 18,
        HighestPlayerLevel = 42,
        BadgesEarned = 3,
        TotalItemHealing = 350,
        WastedItemHealing = 45,
        ItemsUsed = 14,
        ItemsBySource = new Dictionary<string, Dictionary<string, int>>
        {
            ["Bag"] = new() { ["POTION"] = 3 },
            ["Held"] = new() { ["ORANBERRY"] = 1 }
        },
        TrainerSpeciesCounts = new Dictionary<string, int> { ["BULBASAUR"] = 2 },
        TrainerSpeciesNames = new Dictionary<string, string> { ["BULBASAUR"] = "Bulbasaur" },
        TrainerSpeciesDistinct = 1,
        TrainerSpeciesMostEncountered = ["BULBASAUR"],
        TrainerDefeatedCount = 12,
        TrainerDefeatedBstAverage = 401.5,
        TrainerDefeatedBstMinimum = 300,
        TrainerDefeatedBstMinimumSpecies = ["BULBASAUR"],
        TrainerDefeatedBstMaximum = 534,
        TrainerDefeatedBstMaximumSpecies = ["CHARIZARD"]
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

}
