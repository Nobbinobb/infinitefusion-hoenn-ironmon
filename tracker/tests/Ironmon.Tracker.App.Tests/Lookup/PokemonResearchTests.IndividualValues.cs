using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Protocol.Debug;
using Ironmon.Tracker.Protocol.Lookup;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Exercises the conditional IV view inside the existing Stats display selector.
/// </summary>
public sealed partial class PokemonResearchTests
{
    private const string _selectStatMode = "SelectMode";
    private const string _ivTabMarkup = ">IVs</button>";
    private const string _ivBarsMarkup = "class=\"iv-bars\"";
    private const string _baseStatsMarkup = "class=\"base-stat-visualization\"";
    private const string _baseStatTableMarkup = "class=\"base-stat-table\"";
    private const string _baseStatBarsMarkup = "class=\"base-stat-bars\"";
    private const string _mainNavigationEnd = "</nav>";
    private const string _ivTableMarkup = "<table";
    private const string _ivCardRenderVariable = "IRONMON_IV_CARD_RENDER_PATH";
    private const string _ivAttempt = "11111111111111111111111111111111";
    private const string _ivOtherAttempt = "22222222222222222222222222222222";
    private const string _ivStream = "33333333333333333333333333333333";
    private const string _ivEpoch = "44444444444444444444444444444444";
    private const string _ivProfile = "55555555555555555555555555555555";
    private const string _ivOwnedEvent = "pokemon.owned_configuration_committed";
    private const string _ivFirstSequence = "1";
    private const string _ivLost = "lost";
    private const string _ivOwnedPayload = """
        {"owned_entity_id":"early-team-member","capture_kind":"acquisition","ownership_role":"runner","team_commit_context":{"retained_after_commit":true},"species_identity":{"canonical_species_id":94,"form_id":0},"display_metadata":{"nickname":"A long nickname for an early team member"},"level":42,"experience":100,"generated_base_stats":{"hp":60,"attack":65,"defense":60,"special_attack":130,"special_defense":75,"speed":110},"computed_stats":{"maximum_hp":90,"attack":50,"defense":40,"special_attack":45,"special_defense":45,"speed":70},"moves":[],"training_traits":{"iv":{"hp":0,"attack":31,"defense":12,"special_attack":24,"special_defense":8,"speed":30}}}
        """;

    /// <summary>
    /// Adds the IV mode inside Stats only for usable live data and preserves all three original modes.
    /// </summary>
    /// <param name="hasValues">Whether the player has any known IV values.</param>
    /// <returns>A task completing after the production tab interactions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveIndividualValuesUseTheExistingStatsModeSelector(bool hasValues)
    {
        BaseStatVisualization? visualization = null;
        DebugPokemonInspectorSnapshot inspector = new()
        {
            Identity = new() { PokemonId = _version, Nickname = _speciesName, SpeciesId = _speciesId, SpeciesName = _speciesName, Gender = _female, ActiveAbilitySlot = _version, ActiveAbilityId = _abilityId, ActiveAbilityName = _abilityName },
            Stats = new() { Ivs = hasValues ? new() { Hp = 0, Attack = 31 } : new() }
        };
        await RenderAsync<PokemonLookupCard>(new()
        {
            [nameof(PokemonLookupCard.Pokemon)] = new PokemonLookupSnapshot { Identity = new() { SpeciesId = _speciesId, SpeciesName = _speciesName }, Section = PokemonLookupSection.Stats, Stats = new() },
            [nameof(PokemonLookupCard.Inspector)] = inspector
        }, false, async (card, html) =>
        {
            Assert.Equal(hasValues, html().Contains(_ivTabMarkup, StringComparison.Ordinal));
            Assert.DoesNotContain(_ivTabMarkup, html().Split(_mainNavigationEnd, StringSplitOptions.None)[0]);
            Assert.Contains(_baseStatsMarkup, html());
            Assert.Contains(_baseStatTableMarkup, html());
            Assert.DoesNotContain(_ivBarsMarkup, html());
            if (!hasValues)
                return;

            await InvokeAsync(visualization!, _selectStatMode, BaseStatDisplayMode.IndividualValues);
            Assert.Contains(_ivBarsMarkup, html());
            Assert.Contains(_baseStatsMarkup, html());
            Assert.DoesNotContain(_baseStatTableMarkup, html());
            Assert.DoesNotContain(_ivTableMarkup, html());
            await InvokeAsync(visualization!, _selectStatMode, BaseStatDisplayMode.GeneratedBars);
            Assert.Contains(_baseStatBarsMarkup, html());
            Assert.DoesNotContain(_ivBarsMarkup, html());
            await InvokeAsync(visualization!, _selectStatMode, BaseStatDisplayMode.DeltaBars);
            Assert.Contains(_baseStatBarsMarkup, html());
            await InvokeAsync(visualization!, _selectStatMode, BaseStatDisplayMode.Table);
            Assert.Contains(_baseStatTableMarkup, html());
        }, created: component => { if (component is BaseStatVisualization stats) visualization = stats; });
    }

    /// <summary>
    /// Adds archived IVs to the Stats selector and hides that mode for another attempt using the same run ID.
    /// </summary>
    /// <param name="hasOwnedValues">Whether the selected run owns the recorded individual.</param>
    /// <returns>A task completing after durable collection ingestion and tab interactions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchivedIndividualValuesStayInsideStatsAndUseOnlyTheSelectedRun(bool hasOwnedValues)
    {
        BaseStatVisualization? visualization = null;
        using TestWorkspace workspace = new();
        await using GameplayCollectionStore collection = new(workspace.Root);
        JsonElement owned = JsonSerializer.Deserialize<JsonElement>(_ivOwnedPayload);
        await collection.AcceptBatchAsync(JsonSerializer.SerializeToElement(new { schema_version = 1, profile_id = _ivProfile, epoch_id = _ivEpoch, stream_id = _ivStream, events = new[] { new { sequence = _ivFirstSequence, type = _ivOwnedEvent, attempt_id = _ivAttempt, context = new { }, payload = owned } } }));
        CompletedRunRecipePayload recipe = new()
        {
            RunId = _version, Result = _ivLost, CollectionAttemptIds = [hasOwnedValues ? _ivAttempt : _ivOtherAttempt], GenerationProfileId = _ivProfile, GameVersion = _version, IronmonVersion = _version,
            Configuration = new() { WildPolicy = _version, TrainerPolicy = _version, UnfusionSetting = _version }, SpeciesGenerator = new() { PoolFingerprint = _ivProfile }, AbilityGenerator = new() { PoolFingerprint = _ivProfile }, PlayerFusionGenerator = new() { PoolFingerprint = _ivProfile }
        };
        await RenderAsync<PokemonLookupCard>(new()
        {
            [nameof(PokemonLookupCard.Pokemon)] = new PokemonLookupSnapshot { Identity = new() { SpeciesId = _speciesId, SpeciesName = _speciesName, NumericSpeciesId = 94 }, Section = PokemonLookupSection.Stats, Stats = new() },
            [nameof(PokemonLookupCard.Recipe)] = recipe
        }, false, async (card, html) =>
        {
            Assert.Equal(hasOwnedValues, html().Contains(_ivTabMarkup, StringComparison.Ordinal));
            Assert.DoesNotContain(_ivTabMarkup, html().Split(_mainNavigationEnd, StringSplitOptions.None)[0]);
            Assert.DoesNotContain(_ivBarsMarkup, html());
            if (!hasOwnedValues)
                return;

            await InvokeAsync(visualization!, _selectStatMode, BaseStatDisplayMode.IndividualValues);
            Assert.Contains(_ivBarsMarkup, html());
            Assert.Contains(_baseStatsMarkup, html());
            Assert.DoesNotContain(_baseStatTableMarkup, html());
            Assert.DoesNotContain(_ivTableMarkup, html());
            string? renderPath = Environment.GetEnvironmentVariable(_ivCardRenderVariable);
            if (!string.IsNullOrWhiteSpace(renderPath))
                await File.WriteAllTextAsync(renderPath, html());

        }, collection, component => { if (component is BaseStatVisualization stats) visualization = stats; });
    }
}
