using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies run-pinned player-fusion versions remain independent from exported catalog metadata.
/// </summary>
public sealed class PlayerFusionGeneratorCompatibilityTests
{
    /// <summary>
    /// Keeps each historical stat formula available beside the new-run formula without sharing archive cache identities.
    /// </summary>
    [Fact]
    public void HistoricalStatFormulaHasItsOwnCompatibleContext()
    {
        PlayerFusionMappingWorkerCatalog current = PlayerFusionMappingWorkerCatalog.Load();
        PlayerFusionMappingCoordinator coordinator = new();
        HashSet<PlayerFusionMaterialKey> archiveKeys = [];
        HashSet<PlayerFusionMaterialKey> activeKeys = [];
        HashSet<EvolutionAssignmentKey> archivedEvolutionKeys = [];
        HashSet<EvolutionAssignmentKey> activeEvolutionKeys = [];
        Assert.Equal(3, current.BaseStatFormulaVersion);
        Assert.Equal(new[] { 1, 2, 3 }, current.BaseStatSourceFingerprints.Keys.Order());
        foreach ((int formulaVersion, string fingerprint) in current.BaseStatSourceFingerprints)
        {
            PlayerFusionMappingWorkerCatalog pinned = current.WithBaseStatFingerprint(fingerprint);
            CompletedRunRecipePayload archivedRecipe = CreateCompletedRecipe(pinned, 2);
            FusionAssignmentRecipePayload activeRecipe = CreateActiveRecipe(pinned, 2);
            Assert.Equal(formulaVersion, pinned.BaseStatFormulaVersion);
            Assert.True(coordinator.CanMapAreaFusions(_runId, archivedRecipe));
            Assert.True(archiveKeys.Add(PlayerFusionMaterialKey.From(archivedRecipe)));
            Assert.True(activeKeys.Add(PlayerFusionMaterialKey.From(activeRecipe)));
            Assert.True(archivedEvolutionKeys.Add(EvolutionAssignmentKey.From(archivedRecipe)));
            Assert.True(activeEvolutionKeys.Add(EvolutionAssignmentKey.From(activeRecipe)));
        }

        Assert.True(archiveKeys.SetEquals(activeKeys));
        Assert.True(archivedEvolutionKeys.SetEquals(activeEvolutionKeys));
    }

    private const string _runId = "fusion-version-compatibility";
    private const string _profileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string _gameVersion = "6.8.2";
    private const string _ironmonVersion = "0.8.11";
    private const string _completedResult = "lost";
    private const string _mixedPolicy = "mixed";
    private const string _randomUnfusion = "random_component";
    private const string _fixtureFingerprint = "fixture";

    /// <summary>
    /// Accepts either retained algorithm for archive lookup while rejecting unsupported versions.
    /// </summary>
    /// <param name="version">The run-pinned player-fusion algorithm version.</param>
    /// <param name="expected">Whether this implementation can reproduce the requested algorithm.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    public void ArchiveCompatibilityUsesRetainedAlgorithms(int version, bool expected)
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        CompletedRunRecipePayload recipe = CreateCompletedRecipe(catalog, version);
        PlayerFusionMappingCoordinator coordinator = new();

        Assert.Equal(expected, coordinator.CanMapAreaFusions(_runId, recipe));
        Assert.Equal(version, recipe.PlayerFusionGenerator.Version);
    }

    /// <summary>
    /// Keeps historical and adaptive material indexes separate for otherwise identical run inputs.
    /// </summary>
    [Fact]
    public void MaterialCacheIdentityIncludesPinnedAlgorithm()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        CompletedRunRecipePayload legacy = CreateCompletedRecipe(catalog, 1);
        CompletedRunRecipePayload adaptive = CreateCompletedRecipe(catalog, 2);
        FusionAssignmentRecipePayload activeLegacy = CreateActiveRecipe(catalog, 1);
        FusionAssignmentRecipePayload activeAdaptive = CreateActiveRecipe(catalog, 2);

        Assert.NotEqual(PlayerFusionMaterialKey.From(legacy), PlayerFusionMaterialKey.From(adaptive));
        Assert.NotEqual(PlayerFusionMaterialKey.From(activeLegacy), PlayerFusionMaterialKey.From(activeAdaptive));
        Assert.Equal(PlayerFusionMaterialKey.From(legacy), PlayerFusionMaterialKey.From(activeLegacy));
        Assert.Equal(PlayerFusionMaterialKey.From(adaptive), PlayerFusionMaterialKey.From(activeAdaptive));
    }

    /// <summary>
    /// Rejects unknown active-run algorithms before starting or exporting native assignment work.
    /// </summary>
    /// <returns>A task that completes after the incompatible export is rejected.</returns>
    [Fact]
    public async Task UnknownActiveAlgorithmDoesNotPrepareAssignments()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        FusionAssignmentRecipePayload recipe = CreateActiveRecipe(catalog, 3);
        PlayerFusionMappingCoordinator coordinator = new();

        Assert.False(coordinator.PrepareActiveFusionAssignments(_runId, recipe));
        Assert.Null(await coordinator.ExportGameplayPairingAsync(recipe, CancellationToken.None));
        Assert.Equal(0, coordinator.CachedJobCount);
    }

    /// <summary>
    /// Creates an archived recipe using the embedded data with an independently pinned pairing algorithm.
    /// </summary>
    /// <param name="catalog">The shared game-data catalog.</param>
    /// <param name="version">The player-fusion algorithm version carried by the run.</param>
    /// <returns>The complete recipe used by archive compatibility checks.</returns>
    private static CompletedRunRecipePayload CreateCompletedRecipe(PlayerFusionMappingWorkerCatalog catalog, int version)
    {
        return new CompletedRunRecipePayload
        {
            RunId = _runId,
            Result = _completedResult,
            Seed = catalog.VerificationSeed,
            GenerationProfileId = _profileId,
            GameVersion = _gameVersion,
            IronmonVersion = _ironmonVersion,
            Configuration = new RunConfigurationPayload { SchemaVersion = 1, WildPolicy = _mixedPolicy, TrainerPolicy = _mixedPolicy, UnfusionSetting = _randomUnfusion },
            SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = _fixtureFingerprint },
            AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 1, PoolSize = 1, PoolFingerprint = _fixtureFingerprint },
            BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = catalog.BaseStatSourceFingerprint },
            PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = version, PoolSize = catalog.CustomFusionPool.Count, PoolFingerprint = catalog.CustomFusionPoolFingerprint },
            EvolutionGenerator = new EvolutionGeneratorRecipePayload
            {
                Version = 1,
                RulesVersion = 1,
                SourceFingerprint = catalog.EvolutionSourceFingerprint,
                TaxonomyFingerprint = catalog.EvolutionTaxonomyFingerprint,
                MethodFingerprint = catalog.EvolutionMethodFingerprint,
                TargetFingerprint = _fixtureFingerprint,
                BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = catalog.BaseStatSourceFingerprint },
                Fusion = new FusionEvolutionGeneratorRecipePayload
                {
                    Version = catalog.FusionEvolutionGeneratorVersion,
                    RulesVersion = catalog.FusionEvolutionRulesVersion,
                    TargetPool = new VersionedPoolRecipePayload { Version = catalog.CustomFusionPoolVersion, Size = catalog.CustomFusionPool.Count, Fingerprint = catalog.CustomFusionPoolFingerprint }
                }
            }
        };
    }

    /// <summary>
    /// Creates an active assignment recipe using the same data and seed as the archived fixture.
    /// </summary>
    /// <param name="catalog">The shared game-data catalog.</param>
    /// <param name="version">The independently pinned player-fusion algorithm version.</param>
    /// <returns>The authoritative active-run assignment inputs.</returns>
    private static FusionAssignmentRecipePayload CreateActiveRecipe(PlayerFusionMappingWorkerCatalog catalog, int version)
    {
        return new FusionAssignmentRecipePayload
        {
            GenerationProfileId = _profileId,
            NormalSpeciesCount = catalog.NormalSpeciesCount,
            BaseStatGeneratorVersion = 1,
            Seed = catalog.VerificationSeed,
            PlayerFusionGeneratorVersion = version,
            GeneratorVersion = catalog.FusionEvolutionGeneratorVersion,
            RulesVersion = catalog.FusionEvolutionRulesVersion,
            SourceFingerprint = catalog.EvolutionSourceFingerprint,
            TaxonomyFingerprint = catalog.EvolutionTaxonomyFingerprint,
            MethodFingerprint = catalog.EvolutionMethodFingerprint,
            BaseStatSourceFingerprint = catalog.BaseStatSourceFingerprint,
            TargetPoolVersion = catalog.CustomFusionPoolVersion,
            TargetPoolSize = catalog.CustomFusionPool.Count,
            TargetPoolFingerprint = catalog.CustomFusionPoolFingerprint
        };
    }
}
