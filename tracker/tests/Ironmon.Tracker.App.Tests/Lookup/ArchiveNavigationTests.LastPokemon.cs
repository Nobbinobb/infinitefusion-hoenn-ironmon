using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Protocol.Lookup;
using Microsoft.AspNetCore.Components;
using System.Globalization;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Verifies compact last-runner identities in the existing save-file history overview.
/// </summary>
public sealed partial class ArchiveNavigationTests
{
    private const string _overviewProfile = "11111111111111111111111111111111";
    private const string _overviewEpoch = "22222222222222222222222222222222";
    private const string _overviewStream = "33333333333333333333333333333333";
    private const string _overviewAttempt = "44444444444444444444444444444444";
    private const string _overviewOtherAttempt = "55555555555555555555555555555555";
    private const string _overviewBranch = "66666666666666666666666666666666";
    private const string _overviewOwnedEvent = "pokemon.owned_configuration_committed";
    private const string _overviewCapture = "acquisition";
    private const string _overviewRole = "runner";
    private const string _overviewStarter = "Recorded starter";
    private const string _overviewFinal = "Syldra with a very long remembered nickname";
    private const string _overviewOther = "Raichu";
    private const string _overviewSprite = "Graphics/runner.png";
    private const string _overviewRoot = "ironmon-history-overview-tests";
    private const string _overviewImage = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";
    private const string _overviewPreviewVariable = "IRONMON_ARCHIVE_RUNS_PREVIEW";
    private const string _overviewRecords = "records";
    private const string _overviewSpecies = "species_identity";
    private const string _overviewCanonical = "canonical_species_id";
    private const string _overviewDisplay = "display_metadata";
    private const string _overviewSpriteProperty = "sprite_path";
    private const string _overviewPikachu = "PIKACHU";
    private const string _overviewSyldra = "B117H339";
    private const string _overviewRaichu = "RAICHU";
    private const string _overviewStarterEntity = "overview-starter";
    private const string _overviewFinalEntity = "overview-final";
    private const string _overviewOtherEntity = "overview-other";
    private const string _overviewLevel = "Lv. 29";
    private const string _overviewHistoryList = "archive-pokemon-history-list";
    private const string _overviewNoPokemon = "Pokémon not recorded";
    private const string _overviewImageMarkup = "class=\"pokemon-sprite-image\"";

    /// <summary>
    /// Shows only each run's final runner, keeps file filtering and navigation, and refreshes late terminal records.
    /// </summary>
    /// <returns>The durable collection and production rendering verification task.</returns>
    [Fact]
    public async Task SaveFileHistoryShowsOnlyLastPokemonAndRefreshesItsPeakLevel()
    {
        string root = Path.Combine(Path.GetTempPath(), _overviewRoot, Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, Path.GetDirectoryName(_overviewSprite)!));
        File.WriteAllBytes(Path.Combine(root, _overviewSprite), Convert.FromBase64String(_overviewImage));
        string? nativeFixture = Environment.GetEnvironmentVariable(_historyNativeFixtureVariable);
        using JsonDocument? native = string.IsNullOrWhiteSpace(nativeFixture) ? null : JsonDocument.Parse(await File.ReadAllTextAsync(nativeFixture));
        string gameRoot = native?.RootElement.GetProperty(_historyGameRootProperty).GetString() ?? root;
        CompletedRunRecipePayload first = CreateRecipe(_firstRun, _firstSlot, 31, [_overviewAttempt]);
        CompletedRunRecipePayload second = CreateRecipe(_secondRun, _secondSlot, 42, [_overviewOtherAttempt]);
        GameplayCollectionStore? collection = null;
        string? selected = null;
        try
        {
            await RenderAsync<SaveSlotAttemptHistory>(new()
            {
                [nameof(SaveSlotAttemptHistory.Slots)] = new[] { new KeyValuePair<string, RunStatisticsPayload>(_firstSlot, first.Statistics!), new KeyValuePair<string, RunStatisticsPayload>(_secondSlot, second.Statistics!) },
                [nameof(SaveSlotAttemptHistory.CurrentSaveSlot)] = _firstSlot,
                [nameof(SaveSlotAttemptHistory.Recipes)] = new[] { first, second, CreateRecipe(_legacyRun, null, 0) },
                [nameof(SaveSlotAttemptHistory.GameRoot)] = gameRoot,
                [nameof(SaveSlotAttemptHistory.RunSelected)] = EventCallback.Factory.Create<string>(this, (string id) => { selected = id; })
            }, async (components, html) =>
            {
                Assert.Contains(_overviewFinal, html());
                Assert.DoesNotContain(_overviewStarter, html());
                Assert.DoesNotContain(_overviewOther, html());
                Assert.DoesNotContain(_overviewHistoryList, html());
                Assert.Contains(_overviewNoPokemon, html());
                Assert.Contains(_overviewImageMarkup, html());
                string? preview = Environment.GetEnvironmentVariable(_overviewPreviewVariable);
                if (!string.IsNullOrWhiteSpace(preview))
                    await File.WriteAllTextAsync(preview, html());

                await collection!.AcceptBatchAsync(OverviewBatch([OverviewOccurrence(_overviewSyldra, _overviewFinalEntity, _overviewFinal, 29, _overviewAttempt, 4, OverviewSprite(native, _overviewSyldra))]));
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                while (!html().Contains(_overviewLevel, StringComparison.Ordinal))
                    await Task.Delay(10, timeout.Token);

                SaveSlotAttemptHistory history = components.Get<SaveSlotAttemptHistory>();
                await history.RunSelected.InvokeAsync(_firstRun);
                Assert.Equal(_firstRun, selected);
                await InvokeAsync(history, _selectSlot, _secondSlot);
                while (!html().Contains(_overviewOther, StringComparison.Ordinal))
                    await Task.Delay(10, timeout.Token);

                Assert.Contains(_overviewOther, html());
                Assert.DoesNotContain(_overviewFinal, html());
                Assert.Contains(_overviewNoPokemon, html());
            }, async store =>
            {
                collection = store;
                await store.AcceptBatchAsync(OverviewBatch([
                    OverviewOccurrence(_overviewPikachu, _overviewStarterEntity, _overviewStarter, 5, _overviewAttempt, 1, OverviewSprite(native, _overviewPikachu)),
                    OverviewOccurrence(_overviewSyldra, _overviewFinalEntity, _overviewFinal, 27, _overviewAttempt, 2, OverviewSprite(native, _overviewSyldra)),
                    OverviewOccurrence(_overviewRaichu, _overviewOtherEntity, _overviewOther, 19, _overviewOtherAttempt, 3, OverviewSprite(native, _overviewRaichu))]));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Uses real bundled artwork in optional visual rehearsals and a valid local fixture in normal tests.
    /// </summary>
    /// <param name="native">The optional native ownership fixture.</param>
    /// <param name="species">The recorded canonical species identity.</param>
    /// <returns>The corresponding local image path.</returns>
    private static string OverviewSprite(JsonDocument? native, string species)
        => native is null ? _overviewSprite : native.RootElement.GetProperty(_overviewRecords).EnumerateArray().Single(record => record.GetProperty(_overviewSpecies).GetProperty(_overviewCanonical).GetString() == species).GetProperty(_overviewDisplay).GetProperty(_overviewSpriteProperty).GetString()!;

    /// <summary>
    /// Wraps exact ownership evidence in one durable producer batch.
    /// </summary>
    /// <param name="events">Contiguous producer occurrences.</param>
    /// <returns>The native collection batch.</returns>
    private static JsonElement OverviewBatch(JsonElement[] events)
        => JsonSerializer.SerializeToElement(new { schema_version = 1, profile_id = _overviewProfile, epoch_id = _overviewEpoch, stream_id = _overviewStream, events });

    /// <summary>
    /// Creates complete recorded runner evidence without requiring generated lookup or a connected game.
    /// </summary>
    /// <param name="species">The recorded canonical species identity.</param>
    /// <param name="entity">The retained individual identity.</param>
    /// <param name="name">The recorded display name.</param>
    /// <param name="level">The observed level.</param>
    /// <param name="attempt">The exact associated attempt identity.</param>
    /// <param name="sequence">The contiguous source sequence.</param>
    /// <param name="sprite">The recorded local artwork path.</param>
    /// <returns>A complete native ownership occurrence.</returns>
    private static JsonElement OverviewOccurrence(string species, string entity, string name, int level, string attempt, int sequence, string sprite)
    {
        return JsonSerializer.SerializeToElement(new
        {
            sequence = sequence.ToString(CultureInfo.InvariantCulture), type = _overviewOwnedEvent, attempt_id = attempt, branch_id = _overviewBranch,
            occurred_at_us = sequence.ToString(CultureInfo.InvariantCulture), context = new Dictionary<string, int>(),
            payload = new
            {
                owned_entity_id = entity, configuration_revision = sequence, capture_kind = _overviewCapture, ownership_role = _overviewRole,
                team_commit_context = new { retained_after_commit = true }, species_identity = new { canonical_species_id = species, form_id = 0 }, level, experience = 125,
                generated_base_stats = new { hp = 35, attack = 55, defense = 40, special_attack = 50, special_defense = 50, speed = 90 },
                computed_stats = new { maximum_hp = 20, attack = 12, defense = 10, special_attack = 11, special_defense = 11, speed = 15 },
                training_traits = new Dictionary<string, int>(), moves = Array.Empty<object>(), display_metadata = new { nickname = name, sprite_path = sprite }
            }
        });
    }
}
