using System.Text.Json;

namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies the shared seeded-run compatibility contract.
/// </summary>
public sealed class RunCompatibilityFingerprintTests
{
    private const string _alternateGenerationProfileId = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string _expectedCompatibilityFingerprint = "c7260fe773407fb1764218e81494c95301d56d3ce0aa8b0c85fd2faddcf25b6d";
    private const string _generationProfilePropertyName = "generation_profile_id";
    private const string _testGenerationProfileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Verifies identical compatibility inputs always produce one lowercase SHA-256 value.
    /// </summary>
    [Fact]
    public void EquivalentRecipesProduceStableFingerprint()
    {
        string first = RunCompatibilityFingerprint.Create(CreateRecipe());
        string second = RunCompatibilityFingerprint.Create(CreateRecipe());

        Assert.Equal(first, second);
        Assert.Equal(_expectedCompatibilityFingerprint, first);
    }

    /// <summary>
    /// Verifies attempt identity, outcome, seed, and player configuration do not describe installation compatibility.
    /// </summary>
    [Fact]
    public void AttemptOwnedValuesDoNotChangeCompatibilityFingerprint()
    {
        CompletedRunRecipePayload first = CreateRecipe();
        CompletedRunRecipePayload second = CreateRecipe(
            runId: "run-2",
            seed: 987654321,
            result: "lost",
            wildPolicy: "normal_only");

        Assert.Equal(RunCompatibilityFingerprint.Create(first), RunCompatibilityFingerprint.Create(second));
    }

    /// <summary>
    /// Verifies every seeded-generator family contributes to the shared compatibility boundary.
    /// </summary>
    [Fact]
    public void InstallationAndGeneratorChangesAlterCompatibilityFingerprint()
    {
        string baseline = RunCompatibilityFingerprint.Create(CreateRecipe());
        CompletedRunRecipePayload[] incompatibleRecipes =
        [
            CreateRecipe(gameVersion: "6.8.0"),
            CreateRecipe(ironmonVersion: "0.7.7"),
            CreateRecipe(generationProfileId: _alternateGenerationProfileId),
            CreateRecipe(dataMode: "remix"),
            CreateRecipe(speciesFingerprint: "species-b"),
            CreateRecipe(abilityFingerprint: "abilities-b"),
            CreateRecipe(baseStatFingerprint: "stats-b"),
            CreateRecipe(evolutionFingerprint: "evolutions-b"),
            CreateRecipe(moveFingerprint: "moves-b"),
            CreateRecipe(fusionFingerprint: "fusions-b"),
            CreateRecipe(itemFingerprint: "items-b")
        ];

        Assert.All(incompatibleRecipes, recipe => Assert.NotEqual(baseline, RunCompatibilityFingerprint.Create(recipe)));
    }

    /// <summary>
    /// Verifies inherited reproduction fields retain their existing completed-recipe JSON shape.
    /// </summary>
    [Fact]
    public void CompletedRecipeSerializationRemainsFlat()
    {
        JsonElement json = TrackerJson.SerializePayload(CreateRecipe());

        Assert.Equal(123456789, json.GetProperty("seed").GetInt64());
        Assert.Equal(_testGenerationProfileId, json.GetProperty(_generationProfilePropertyName).GetString());
        Assert.Equal("6.7.2", json.GetProperty("game_version").GetString());
        Assert.Equal("species-a", json.GetProperty("species_generator").GetProperty("pool_fingerprint").GetString());
        Assert.False(json.TryGetProperty("reproduction", out _));
    }

    /// <summary>
    /// Creates a complete recipe with independently adjustable reproduction inputs.
    /// </summary>
    /// <param name="runId">The attempt identifier.</param>
    /// <param name="seed">The run seed.</param>
    /// <param name="result">The completion result.</param>
    /// <param name="gameVersion">The game version.</param>
    /// <param name="ironmonVersion">The Ironmon version.</param>
    /// <param name="generationProfileId">The pinned immutable generation profile.</param>
    /// <param name="dataMode">The game-data mode.</param>
    /// <param name="wildPolicy">The wild-species policy.</param>
    /// <param name="speciesFingerprint">The species-pool fingerprint.</param>
    /// <param name="abilityFingerprint">The ability-pool fingerprint.</param>
    /// <param name="baseStatFingerprint">The base-stat source fingerprint.</param>
    /// <param name="evolutionFingerprint">The evolution source fingerprint.</param>
    /// <param name="moveFingerprint">The move-pool fingerprint.</param>
    /// <param name="fusionFingerprint">The player-fusion-pool fingerprint.</param>
    /// <param name="itemFingerprint">The ground-item-pool fingerprint.</param>
    /// <returns>A complete completed-run recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId = "run-1", long seed = 123456789, string result = "won", string gameVersion = "6.7.2", string ironmonVersion = "0.7.6", string generationProfileId = _testGenerationProfileId, string dataMode = "classic", string wildPolicy = "mixed", string speciesFingerprint = "species-a", string abilityFingerprint = "abilities-a", string baseStatFingerprint = "stats-a", string evolutionFingerprint = "evolutions-a", string moveFingerprint = "moves-a", string fusionFingerprint = "fusions-a", string itemFingerprint = "items-a")
    {
        return new CompletedRunRecipePayload
        {
            RunId = runId,
            Seed = seed,
            Result = result,
            GenerationProfileId = generationProfileId,
            GameVersion = gameVersion,
            IronmonVersion = ironmonVersion,
            DataMode = dataMode,
            Configuration = new RunConfigurationPayload
            {
                SchemaVersion = 1,
                WildPolicy = wildPolicy,
                TrainerPolicy = "custom_only",
                UnfusionSetting = "random_component"
            },
            SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = speciesFingerprint },
            AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 1, PoolSize = 310, PoolFingerprint = abilityFingerprint },
            BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = baseStatFingerprint },
            EvolutionGenerator = new EvolutionGeneratorRecipePayload
            {
                Version = 1,
                RulesVersion = 1,
                SourceFingerprint = evolutionFingerprint,
                TaxonomyFingerprint = "taxonomy-a",
                MethodFingerprint = "methods-a",
                TargetFingerprint = "targets-a",
                BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = baseStatFingerprint },
                Fusion = new FusionEvolutionGeneratorRecipePayload
                {
                    Version = 1,
                    RulesVersion = 1,
                    TargetPool = new VersionedPoolRecipePayload { Version = 1, Size = 174348, Fingerprint = fusionFingerprint }
                }
            },
            MoveAccessGenerator = new MoveAccessGeneratorRecipePayload
            {
                Version = 1,
                PoolFingerprint = moveFingerprint,
                ContextualRestrictionFingerprint = "move-rules-a",
                LevelUpSourceFingerprint = "level-up-a",
                EggSourceFingerprint = "egg-a",
                Tm = new MachineSourceRecipePayload { RosterFingerprint = "tm-roster-a", SourceFingerprint = "tm-source-a" },
                Tr = new MachineSourceRecipePayload { RosterFingerprint = "tr-roster-a", SourceFingerprint = "tr-source-a" },
                Tutor = new TutorSourceRecipePayload { CatalogFingerprint = "tutor-catalog-a", SourceFingerprint = "tutor-source-a" },
                FusionTutor = new TutorSourceRecipePayload { CatalogFingerprint = "fusion-tutor-catalog-a", SourceFingerprint = "fusion-tutor-source-a" }
            },
            PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 1, PoolSize = 174348, PoolFingerprint = fusionFingerprint },
            ItemGenerator = new ItemGeneratorRecipePayload
            {
                Version = 1,
                RulesVersion = 1,
                GroundPoolSize = 580,
                GroundTotalWeight = 5253,
                GroundPoolFingerprint = itemFingerprint,
                TmPoolSize = 124,
                TmPoolFingerprint = "tm-items-a",
                ResultBans = ["DNAREVERSER", "DNASPLICERS"],
                ResultBanFingerprint = "item-bans-a",
                ShopPolicyVersion = 1
            }
        };
    }
}
