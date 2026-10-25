using Ironmon.Tracker.Collection;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies completed-run reads retain owned history without mixing attempts or snapshots.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _lookupSpecies = "species_identity";
    private const string _lookupCanonicalSpecies = "canonical_species_id";
    private const string _lookupLevel = "level";
    private const string _lookupRawIv = "iv";
    private const string _lookupHp = "hp";
    private const string _lookupAttempt = "attempt_id";
    private const string _lookupBaseline = "first_observed_baseline";
    private const string _lookupResult = "lost";

    /// <summary>
    /// Reads actual owned IVs after completion and reopen, with one latest configuration per species and individual.
    /// </summary>
    /// <returns>A task completing after durable ingestion, reopen and isolated ownership assertions.</returns>
    [Fact]
    public async Task OwnedLookupKeepsEarlyIndividualsAndSpeciesHistoryButExcludesOtherAttemptsAndBaselines()
    {
        using TestDirectory directory = new();
        JsonObject first = JsonNode.Parse(_ownedPayload)!.AsObject();
        JsonObject updated = first.DeepClone().AsObject();
        updated[_captureKindProperty] = _configurationChange;
        updated[_lookupLevel] = 50;
        updated[_trainingProperty]![_lookupRawIv]![_lookupHp] = 0;
        JsonObject second = first.DeepClone().AsObject();
        second[_ownedEntityProperty] = _runnerTwo;
        JsonObject evolved = updated.DeepClone().AsObject();
        evolved[_lookupSpecies]![_lookupCanonicalSpecies] = 26;
        JsonObject foreign = JsonNode.Parse(Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(first), 5).GetRawText())!.AsObject();
        foreign[_lookupAttempt] = _successor;
        JsonObject baseline = first.DeepClone().AsObject();
        baseline[_ownedEntityProperty] = _candidateOne;
        baseline[_captureKindProperty] = _lookupBaseline;
        await using (GameplayCollectionStore store = new(directory.Path))
        {
            await store.AcceptBatchAsync(Batch(_stream,
            [
                Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(first), 1),
                Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(updated), 2),
                Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(second), 3),
                Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(evolved), 4),
                JsonSerializer.SerializeToElement(foreign),
                Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(baseline), 6),
                Occurrence(_damageEvent, Damage(40), 7),
                Occurrence(_attemptEndEvent, JsonSerializer.SerializeToElement(new { result = _lookupResult }), 8)
            ]));
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        IReadOnlyList<JsonElement> records = await reopened.GetOwnedConfigurationsAsync([_attempt, _attempt]);
        Assert.Equal(3, records.Count);
        JsonElement[] originalSpecies = [.. records.Where(record => record.GetProperty(_lookupSpecies).GetProperty(_lookupCanonicalSpecies).GetInt32() == 25)];
        Assert.Equal(2, originalSpecies.Length);
        JsonElement runner = Assert.Single(originalSpecies, record => record.GetProperty(_ownedEntityProperty).GetString() == _runnerOne);
        Assert.Equal(50, runner.GetProperty(_lookupLevel).GetInt32());
        Assert.Equal(0, runner.GetProperty(_trainingProperty).GetProperty(_lookupRawIv).GetProperty(_lookupHp).GetInt32());
        Assert.Single(await reopened.GetOwnedConfigurationsAsync([_successor]));
        Assert.Empty(await reopened.GetOwnedConfigurationsAsync([_branch]));
        Assert.Empty(await reopened.GetOwnedConfigurationsAsync([]));
        AssertIntegrity(directory.Path);
    }
}
