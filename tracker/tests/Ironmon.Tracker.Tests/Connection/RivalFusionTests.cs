using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies single-pair gameplay work stays exact and separate from full lookup preparation.
/// </summary>
public sealed class RivalFusionTests
{
    private const string _profileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string _runId = "rival-worker-run";
    private const string _sessionId = "rival-worker-session";
    private const string _requestId = "rival-worker-request";
    private const string _bodyIdProperty = "body_id";
    private const string _headIdProperty = "head_id";
    private const string _requestIdProperty = "request_id";
    private const string _speciesNumberProperty = "species_number";

    /// <summary>
    /// Verifies the targeted coordinator preserves both orientations of Ruby reference mappings without opening full lookup work.
    /// </summary>
    /// <returns>Completion after the reference single-pair assignments have been checked.</returns>
    [Fact]
    public async Task SinglePairMatchesRubyReferencesWithoutPreparingFullIndexes()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        PlayerFusionMappingCoordinator coordinator = new();
        FusionAssignmentRecipePayload recipe = Recipe(catalog);
        foreach (PlayerFusionReferenceMapping reference in catalog.VerificationMappings.Take(4))
        {
            Assert.Equal(reference.FirstResultId, await coordinator.MapRivalFusionAsync(recipe, reference.FirstMaterialId, reference.SecondMaterialId, CancellationToken.None));
            if (reference.FirstMaterialId != reference.SecondMaterialId)
                Assert.Equal(reference.SecondResultId, await coordinator.MapRivalFusionAsync(recipe, reference.SecondMaterialId, reference.FirstMaterialId, CancellationToken.None));
        }

        Assert.False(coordinator.CanMapAreaFusions(_runId, null));
        Assert.Equal(0, coordinator.CachedJobCount);
    }

    /// <summary>
    /// Verifies material validation fails promptly instead of starting a full catalogue fallback.
    /// </summary>
    /// <returns>Completion after an invalid material has been rejected.</returns>
    [Fact]
    public async Task InvalidMaterialIsRejected()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        PlayerFusionMappingCoordinator coordinator = new();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coordinator.MapRivalFusionAsync(Recipe(catalog), 0, 1, CancellationToken.None));
    }

    /// <summary>
    /// Verifies metadata-only preparation contains no starter material IDs or result identifier.
    /// </summary>
    [Fact]
    public void PreparationDoesNotDiscloseStarterOffers()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        JsonElement payload = TrackerJson.SerializePayload(new RivalFusionWorkPayload { SessionId = _sessionId, Recipe = Recipe(catalog) });
        Assert.False(payload.TryGetProperty(_bodyIdProperty, out _));
        Assert.False(payload.TryGetProperty(_headIdProperty, out _));
        Assert.False(payload.TryGetProperty(_requestIdProperty, out _));
        Assert.False(payload.TryGetProperty(_speciesNumberProperty, out _));
    }

    /// <summary>
    /// Verifies work from a previous process or run produces no reply or preparation state.
    /// </summary>
    [Fact]
    public void StaleSessionOrRunCannotRequestAnAssignment()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        TrackerConnectionState state = new();
        TrackerDiagnosticsStore diagnostics = new();
        using TrackerRequestSession session = new(diagnostics);
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _runId));
        TrackerRequestClient client = new(session, new TrackerConnectionOptions(0, _sessionId, false, TimeSpan.FromSeconds(2)), state, new AreaDiscoveryStore(storage));
        GameHandshakePayload game = new(_sessionId, _sessionId, true, false, TrackerTestPaths.GameRoot, _runId, null) { SessionId = _sessionId };
        state.Publish(TrackerConnectionStatus.Connected, game, new GameCurrentStatePayload(true, _runId, null, 1));
        RivalFusionWorkPayload stale = new() { SessionId = _requestId, RequestId = _requestId, Recipe = Recipe(catalog), BodyId = 1, HeadId = 4 };
        client.HandleRivalFusion(TrackerMessageFactory.CreateEvent(TrackerEvents.RivalFusionRequested, 1, stale, _runId), game, CancellationToken.None);
        RivalFusionWorkPayload wrongRun = new() { SessionId = _sessionId, RequestId = _requestId, Recipe = Recipe(catalog), BodyId = 1, HeadId = 4 };
        client.HandleRivalFusion(TrackerMessageFactory.CreateEvent(TrackerEvents.RivalFusionRequested, 2, wrongRun, _requestId), game, CancellationToken.None);
        Assert.Empty(diagnostics.Entries);
    }

    /// <summary>
    /// Creates the complete current native recipe for cross-runtime reference assignments.
    /// </summary>
    /// <param name="catalog">The immutable embedded native input catalog.</param>
    /// <returns>The exact generation metadata associated with its Ruby verification mappings.</returns>
    private static FusionAssignmentRecipePayload Recipe(PlayerFusionMappingWorkerCatalog catalog)
    {
        return new FusionAssignmentRecipePayload
        {
            GenerationProfileId = _profileId,
            NormalSpeciesCount = catalog.NormalSpeciesCount,
            BaseStatGeneratorVersion = 1,
            Seed = catalog.VerificationSeed,
            PlayerFusionGeneratorVersion = catalog.VerificationGeneratorVersion,
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
