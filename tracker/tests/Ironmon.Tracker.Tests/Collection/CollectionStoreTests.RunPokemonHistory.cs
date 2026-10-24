using Ironmon.Tracker.Collection;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies compact archived runner chronology from exact retained collection evidence.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _historySpeciesProperty = "species_identity";
    private const string _historyCanonicalProperty = "canonical_species_id";
    private const string _historyDisplayProperty = "display_metadata";
    private const string _historyNameProperty = "species_name";
    private const string _historyLookupProperty = "species_id";
    private const string _historySpriteProperty = "sprite_path";
    private const string _historyRoleProperty = "ownership_role";
    private const string _historyCommitProperty = "team_commit_context";
    private const string _historyRetainedProperty = "retained_after_commit";
    private const string _historyLevelProperty = "level";
    private const string _historySourceProperty = "source_kind";
    private const string _historyAcquisitionProperty = "acquisition";
    private const string _historyLineageProperty = "lineage";
    private const string _historyTransformationProperty = "transformation_kind";
    private const string _historyReasonsProperty = "change_reasons";
    private const string _historyStarter = "starter";
    private const string _historyFusion = "fuse";
    private const string _historyEvolution = "evolution";
    private const string _historyExit = "runner_exit";
    private const string _historyUtility = "utility";
    private const string _historyStarterId = "PIKACHU";
    private const string _historyFusionId = "B117H339";
    private const string _historyEvolvedId = "B186H302";
    private const string _historySprite = "Graphics/CustomBattlers/local_sprites/retained.png";
    private const string _historyStarterName = "Pikachu";
    private const string _historyFusionName = "Syldra";
    private const string _historyEvolvedName = "Evolved Syldra";
    private const string _historyFormSuffix = ":0";
    private const string _historyNicknameProperty = "nickname";
    private const string _historyLegacyName = "Legacy nickname";
    private const string _historyContextProperty = "history_context";
    private const string _historyParentProperty = "saved_parent";
    private const string _historyBranchProperty = "branch_id";
    private const string _historyStreamProperty = "stream_id";
    private const string _historySequenceProperty = "sequence";
    private const string _historySecondBranch = "77777777777777777777777777777777";
    private const string _historyThirdBranch = "88888888888888888888888888888888";
    private const string _historyAncestryKnownProperty = "ancestry_known";

    /// <summary>
    /// Keeps saved ancestors and their actual peaks while pruning unsaved pivots and evolutions across process restarts.
    /// </summary>
    /// <returns>The exact saved-cursor regression task.</returns>
    [Fact]
    public async Task RunnerHistoryFollowsSavedCursorsAcrossReloadsAndStreams()
    {
        using TestDirectory directory = new();
        await using (GameplayCollectionStore store = new(directory.Path))
        {
            JsonObject starter = HistoryPayload(_runnerOne, _historyStarterId, _historyStarterName, 5);
            starter[_historyAcquisitionProperty] = new JsonObject { [_historySourceProperty] = _historyStarter };
            JsonObject savedStarter = starter.DeepClone().AsObject();
            savedStarter[_historyLevelProperty] = 12;
            JsonObject discardedPivot = HistoryPayload(_runnerTwo, _historyEvolvedId, _historyEvolvedName, 80);
            JsonObject continuedStarter = starter.DeepClone().AsObject();
            continuedStarter[_captureKindProperty] = _configurationChange;
            continuedStarter[_historyLevelProperty] = 15;
            JsonObject savedPivot = HistoryPayload(_candidateOne, _historyFusionId, _historyFusionName, 10);
            savedPivot[_historyLineageProperty] = new JsonObject { [_historyTransformationProperty] = _historyFusion };
            JsonObject discardedEvolution = HistoryPayload(_candidateOne, _historyEvolvedId, _historyEvolvedName, 99);
            JsonElement[] firstStream = [
                HistoryOccurrence(starter, 1, _branch), HistoryOccurrence(savedStarter, 2, _branch), HistoryOccurrence(discardedPivot, 3, _branch),
                HistoryOccurrence(continuedStarter, 4, _historySecondBranch, _branch, _stream, 2), HistoryOccurrence(savedPivot, 5, _historySecondBranch, _branch, _stream, 2),
                HistoryOccurrence(discardedEvolution, 6, _historySecondBranch, _branch, _stream, 2)];
            await store.AcceptBatchAsync(Batch(_stream, firstStream));
            JsonObject final = savedPivot.DeepClone().AsObject();
            final[_captureKindProperty] = _configurationChange;
            final[_historyLevelProperty] = 15;
            await store.AcceptBatchAsync(Batch(_successor, [HistoryOccurrence(final, 1, _historyThirdBranch, _historySecondBranch, _stream, 5)], _stream, "6"));
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        IReadOnlyList<RunPokemonHistoryEntry> history = await reopened.GetRunPokemonHistoryAsync([_attempt]);
        Assert.Equal([_historyFusionName, _historyStarterName], history.Select(entry => entry.Name));
        Assert.Equal([15, 15], history.Select(entry => entry.HighestLevel));
        Assert.Equal([RunPokemonTransition.FusionPivot, RunPokemonTransition.Starter], history.Select(entry => entry.Transition));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Removes obsolete legacy starter selections only at a recorded reload branch boundary.
    /// </summary>
    /// <returns>The legacy archive repair regression task.</returns>
    [Fact]
    public async Task RunnerHistoryRestartsAtLegacyStarterReselection()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonObject starter = HistoryPayload(_runnerOne, _historyStarterId, _historyStarterName, 5);
        starter[_historyAcquisitionProperty] = new JsonObject { [_historySourceProperty] = _historyStarter };
        JsonObject pivot = HistoryPayload(_runnerTwo, _historyFusionId, _historyFusionName, 40);
        JsonElement reselected = Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(starter), 3);
        JsonObject reselectedEvent = JsonNode.Parse(reselected.GetRawText())!.AsObject();
        reselectedEvent[_historyBranchProperty] = _historySecondBranch;
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(starter), 1), Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(pivot), 2), JsonSerializer.SerializeToElement(reselectedEvent)]));
        RunPokemonHistoryEntry entry = Assert.Single(await store.GetRunPokemonHistoryAsync([_attempt]));
        Assert.Equal(_historyStarterName, entry.Name);
        Assert.Equal(5, entry.HighestLevel);
    }

    /// <summary>
    /// Starts a clean timeline when the loaded save contains no parent cursor for the current attempt.
    /// </summary>
    /// <returns>The no-saved-parent regression task.</returns>
    [Fact]
    public async Task RunnerHistoryFreshBranchDropsUnsavedEarlierRunner()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonObject discarded = HistoryPayload(_runnerOne, _historyFusionId, _historyFusionName, 40);
        JsonObject current = HistoryPayload(_runnerTwo, _historyStarterId, _historyStarterName, 5);
        await store.AcceptBatchAsync(Batch(_stream, [HistoryOccurrence(discarded, 1, _branch), HistoryOccurrence(current, 2, _historySecondBranch)]));
        RunPokemonHistoryEntry entry = Assert.Single(await store.GetRunPokemonHistoryAsync([_attempt]));
        Assert.Equal(_historyStarterName, entry.Name);
        Assert.Equal(5, entry.HighestLevel);
        Assert.Equal(RunPokemonTransition.FirstRecorded, entry.Transition);
    }

    /// <summary>
    /// Preserves legacy ancestors when an existing populated save has no exact checkpoint yet.
    /// </summary>
    /// <returns>The unknown-ancestry compatibility verification task.</returns>
    [Fact]
    public async Task RunnerHistoryDoesNotEraseLegacyHistoryForUnknownSaveAncestry()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonObject starter = HistoryPayload(_runnerOne, _historyStarterId, _historyStarterName, 5);
        starter[_historyAcquisitionProperty] = new JsonObject { [_historySourceProperty] = _historyStarter };
        JsonObject fusion = HistoryPayload(_runnerTwo, _historyFusionId, _historyFusionName, 10);
        JsonObject evolved = HistoryPayload(_runnerTwo, _historyEvolvedId, _historyEvolvedName, 20);
        evolved[_historyReasonsProperty] = new JsonArray(_historyEvolution);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(starter), 1), Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(fusion), 2), HistoryOccurrence(evolved, 3, _historySecondBranch, ancestryKnown: false)]));
        IReadOnlyList<RunPokemonHistoryEntry> history = await store.GetRunPokemonHistoryAsync([_attempt]);
        Assert.Equal([_historyEvolvedName, _historyFusionName, _historyStarterName], history.Select(entry => entry.Name));
    }

    /// <summary>
    /// Adds exact branch ancestry to one retained configuration occurrence.
    /// </summary>
    /// <param name="payload">The complete retained configuration.</param>
    /// <param name="sequence">The occurrence sequence within its delivery stream.</param>
    /// <param name="branch">The current unique load branch.</param>
    /// <param name="parentBranch">The saved ancestor branch, or null for a fresh timeline.</param>
    /// <param name="parentStream">The ancestor's producer stream.</param>
    /// <param name="parentSequence">The last source sequence included in that save.</param>
    /// <param name="ancestryKnown">Whether the loaded save has exact ancestry rather than legacy unknown coverage.</param>
    /// <returns>The independent occurrence with its saved branch cursor.</returns>
    private static JsonElement HistoryOccurrence(JsonObject payload, int sequence, string branch, string? parentBranch = null, string? parentStream = null, int parentSequence = 0, bool ancestryKnown = true)
    {
        JsonObject copy = payload.DeepClone().AsObject();
        copy[_historyContextProperty] = new JsonObject
        {
            [_historyBranchProperty] = branch,
            [_historyAncestryKnownProperty] = ancestryKnown,
            [_historyParentProperty] = parentBranch is null ? null : new JsonObject { [_historyBranchProperty] = parentBranch, [_historyStreamProperty] = parentStream, [_historySequenceProperty] = parentSequence.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        };
        JsonObject occurrence = JsonNode.Parse(Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(copy), sequence).GetRawText())!.AsObject();
        occurrence[_historyBranchProperty] = branch;
        return JsonSerializer.SerializeToElement(occurrence);
    }

    /// <summary>
    /// Uses retained legacy names and explicitly partial history without inventing a starter or an image.
    /// </summary>
    /// <returns>The partial-history compatibility verification task.</returns>
    [Fact]
    public async Task RunnerHistoryDoesNotInventStarterForPartialCoverage()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonObject baseline = HistoryPayload(_runnerOne, _historyStarterId, _historyStarterName, 5);
        baseline[_captureKindProperty] = _lookupBaseline;
        JsonObject observed = baseline.DeepClone().AsObject();
        observed[_captureKindProperty] = _configurationChange;
        observed[_historyDisplayProperty] = new JsonObject { [_historyNicknameProperty] = _historyLegacyName };
        observed[_historyLevelProperty] = 10;
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(baseline), 1), Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(observed), 2)]));
        RunPokemonHistoryEntry entry = Assert.Single(await store.GetRunPokemonHistoryAsync([_attempt]));
        Assert.Equal(RunPokemonTransition.FirstRecorded, entry.Transition);
        Assert.Equal(_historyLegacyName, entry.Name);
        Assert.Equal(_historyStarterId + _historyFormSuffix, entry.SpeciesId);
        Assert.Equal(10, entry.HighestLevel);
        Assert.Null(entry.SpritePath);
    }

    /// <summary>
    /// Preserves starter, fusion and evolution stints, per-stint peak levels and newest-first ordering after reopening.
    /// </summary>
    /// <returns>The complete durable collection history regression task.</returns>
    [Fact]
    public async Task RunnerHistoryRetainsTransitionsPeakLevelsAndExactAttemptScope()
    {
        using TestDirectory directory = new();
        JsonObject starter = HistoryPayload(_runnerOne, _historyStarterId, _historyStarterName, 5);
        starter[_historyAcquisitionProperty] = new JsonObject { [_historySourceProperty] = _historyStarter };
        JsonObject starterPeak = starter.DeepClone().AsObject();
        starterPeak[_captureKindProperty] = _configurationChange;
        starterPeak[_historyLevelProperty] = 12;
        JsonObject fusion = HistoryPayload(_runnerTwo, _historyFusionId, _historyFusionName, 9);
        fusion[_historyLineageProperty] = new JsonObject { [_historyTransformationProperty] = _historyFusion };
        JsonObject fusionPeak = fusion.DeepClone().AsObject();
        fusionPeak[_captureKindProperty] = _configurationChange;
        fusionPeak[_historyLevelProperty] = 20;
        JsonObject lowered = fusionPeak.DeepClone().AsObject();
        lowered[_historyLevelProperty] = 7;
        JsonObject evolved = HistoryPayload(_runnerTwo, _historyEvolvedId, _historyEvolvedName, 21);
        evolved[_captureKindProperty] = _configurationChange;
        evolved[_historyReasonsProperty] = new JsonArray(_historyEvolution);
        JsonObject evolvedPeak = evolved.DeepClone().AsObject();
        evolvedPeak[_historyLevelProperty] = 25;
        JsonObject returned = lowered.DeepClone().AsObject();
        returned[_historyLevelProperty] = 8;
        returned[_historyReasonsProperty] = new JsonArray(_historyEvolution);
        JsonObject retired = evolvedPeak.DeepClone().AsObject();
        retired[_captureKindProperty] = _historyExit;
        retired[_historyLevelProperty] = 99;
        JsonObject utility = HistoryPayload(_candidateOne, _historyStarterId, _historyStarterName, 90);
        utility[_historyRoleProperty] = _historyUtility;
        JsonObject candidate = utility.DeepClone().AsObject();
        candidate[_historyCommitProperty]![_historyRetainedProperty] = false;
        JsonObject foreign = JsonNode.Parse(Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(starter), 12).GetRawText())!.AsObject();
        foreign[_lookupAttempt] = _successor;
        List<IReadOnlyList<string>> changed = [];
        await using (GameplayCollectionStore store = new(directory.Path))
        {
            store.OwnedHistoryChanged += _ => throw new InvalidOperationException("A view listener failed after the durable commit.");
            store.OwnedHistoryChanged += changed.Add;
            JsonElement[] events = [.. new[] { starter, starterPeak, fusion, fusionPeak, lowered, evolved, evolvedPeak, returned, retired, utility, candidate }
                .Select((payload, index) => Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(payload), index + 1)), JsonSerializer.SerializeToElement(foreign)];
            await store.AcceptBatchAsync(Batch(_stream, events));
            Assert.Single(changed);
            Assert.Contains(_attempt, changed[0]);
            Assert.Contains(_successor, changed[0]);
            await store.AcceptBatchAsync(Batch(_stream, events));
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        IReadOnlyList<RunPokemonHistoryEntry> history = await reopened.GetRunPokemonHistoryAsync([_attempt, _attempt]);
        Assert.Equal(4, history.Count);
        Assert.Equal([4, 3, 2, 1], history.Select(entry => entry.Order));
        Assert.Equal([8, 25, 20, 12], history.Select(entry => entry.HighestLevel));
        Assert.Equal([RunPokemonTransition.Evolution, RunPokemonTransition.Evolution, RunPokemonTransition.FusionPivot, RunPokemonTransition.Starter], history.Select(entry => entry.Transition));
        Assert.Equal(_historyFusionName, history[0].Name);
        Assert.Equal(_historyFusionId + _historyFormSuffix, history[0].SpeciesId);
        Assert.Equal(_historySprite, history[0].SpritePath);
        Assert.Single(await reopened.GetRunPokemonHistoryAsync([_successor]));
        Assert.Empty(await reopened.GetRunPokemonHistoryAsync([_branch]));
        Assert.Empty(await reopened.GetRunPokemonHistoryAsync([]));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Creates a complete retained configuration using the shared validated collection fixture.
    /// </summary>
    /// <param name="individual">The retained individual identity.</param>
    /// <param name="species">The native species identity.</param>
    /// <param name="name">The recorded Pokemon species name.</param>
    /// <param name="level">The recorded configuration level.</param>
    /// <returns>A complete native ownership payload.</returns>
    private static JsonObject HistoryPayload(string individual, string species, string name, int level)
    {
        JsonObject payload = JsonNode.Parse(_ownedPayload)!.AsObject();
        payload[_ownedEntityProperty] = individual;
        payload[_historySpeciesProperty]![_historyCanonicalProperty] = species;
        payload[_historyLevelProperty] = level;
        payload[_historyDisplayProperty] = new JsonObject { [_historyNameProperty] = name, [_historyLookupProperty] = species + _historyFormSuffix, [_historySpriteProperty] = _historySprite };
        return payload;
    }
}
