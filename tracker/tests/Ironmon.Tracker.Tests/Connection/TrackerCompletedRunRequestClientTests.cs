using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies completed-run calculation reuse updates progress without requesting new game work.
/// </summary>
public sealed class TrackerCompletedRunRequestClientTests
{
    private const string _runId = "cached-completed-run";
    private const string _cacheKey = _runId + "|NONE||";
    private const string _completePhase = "complete";
    private const string _profileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string _gameVersion = "6.8.0";
    private const string _ironmonVersion = "0.10.0";
    private const string _mixedPolicy = "mixed";
    private const string _unfusionSetting = "random_component";
    private const string _fingerprint = "cached-metadata";
    private const string _lost = "lost";

    /// <summary>
    /// Initializes the completed-run request regressions.
    /// </summary>
    public TrackerCompletedRunRequestClientTests()
    {
    }

    /// <summary>
    /// Restores completed Archive progress from a retained response after a run transition clears the visible progress state.
    /// </summary>
    /// <returns>Completion after the cached request updates its observer without a connected game session.</returns>
    [Fact]
    public async Task CachedCalculationPublishesCompletedProgressAfterRunTransition()
    {
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        TrackerResponseCache cache = new();
        PokemonObtainabilityResponsePayload completed = new() { Complete = true, BackgroundComplete = true, Phase = _completePhase, ObtainableCount = 42 };
        cache.Set(TrackerCommands.PokemonObtainability, _cacheKey, completed);
        TrackerCompletedRunRequestClient client = new(session, cache, new PlayerFusionMappingCoordinator());
        CompletedRunRecipePayload recipe = new()
        {
            RunId = _runId,
            GenerationProfileId = _profileId,
            GameVersion = _gameVersion,
            IronmonVersion = _ironmonVersion,
            Configuration = new() { WildPolicy = _mixedPolicy, TrainerPolicy = _mixedPolicy, UnfusionSetting = _unfusionSetting },
            SpeciesGenerator = new() { PoolFingerprint = _fingerprint },
            AbilityGenerator = new() { PoolFingerprint = _fingerprint },
            PlayerFusionGenerator = new() { PoolFingerprint = _fingerprint },
            Result = _lost
        };
        TrackerObtainabilityProgressState progress = new();
        progress.Begin(_runId, TrackerObtainabilityProgressScope.ActiveRun);
        progress.Report(_runId, TrackerObtainabilityProgressScope.ActiveRun, completed);
        progress.Reset();
        progress.Begin(_runId, TrackerObtainabilityProgressScope.ArchivedRun);

        PokemonObtainabilityResponsePayload response = await client.AdvanceObtainabilityAsync(recipe, null, null, null, false, value => progress.Report(_runId, TrackerObtainabilityProgressScope.ArchivedRun, value), CancellationToken.None);

        Assert.Same(completed, response);
        Assert.Equal(TrackerObtainabilityProgressStatus.Complete, progress.Snapshot.Status);
        Assert.Equal(_completePhase, progress.Snapshot.Phase);
        Assert.Equal(42, progress.Snapshot.ObtainableCount);
        Assert.True(progress.HasCompletedPreparation(_runId));
    }
}
