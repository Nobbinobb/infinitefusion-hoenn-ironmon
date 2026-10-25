using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Collection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Stats;

/// <summary>
/// Checks production IV presentation and exact species matching for recorded individuals.
/// </summary>
public sealed class IndividualValuesTests
{
    private const string _resources = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _species = "PIKACHU:0";
    private const string _wrongForm = "PIKACHU:1";
    private const string _otherSpecies = "RAICHU:0";
    private const string _payload = """
        {"species_identity":{"canonical_species_id":"PIKACHU","form_id":0},"display_metadata":{"nickname":"A nickname long enough to fill a narrow lookup panel"},"level":42,"ownership_role":"runner","training_traits":{"iv":{"hp":0,"attack":31},"effective_iv":{"hp":31,"attack":31}}}
        """;
    private const string _legacyPayload = """
        {"species_identity":{"canonical_species_id":25},"level":5}
        """;
    private const string _zeroCell = "iv-bar-value\">0";
    private const string _perfectCell = "iv-bar-value\">31";
    private const string _effectiveHeading = "Effective";
    private const string _table = "<table";
    private const string _attemptId = "11111111111111111111111111111111";
    private const string _otherAttemptId = "22222222222222222222222222222222";
    private const string _streamId = "33333333333333333333333333333333";
    private const string _epochId = "44444444444444444444444444444444";
    private const string _profileId = "55555555555555555555555555555555";
    private const string _ownedEvent = "pokemon.owned_configuration_committed";
    private const string _ownedEntity = "owned_entity_id";
    private const string _displayMetadata = "display_metadata";
    private const string _nicknameProperty = "nickname";
    private const string _trainingTraits = "training_traits";
    private const string _secondIndividual = "second";
    private const string _secondNickname = "Early team member";
    private const string _foreignNickname = "Different run";
    private const string _optionMarkup = "<option";
    private const string _lost = "lost";
    private const string _run = "selected-run";
    private const string _speciesName = "Pikachu";
    private const string _renderPathVariable = "IRONMON_IV_RENDER_PATH";
    private const string _hpLabel = "HP";
    private const string _specialAttackLabel = "SPA";
    private const string _specialDefenseLabel = "SPD";
    private const string _attackLabel = "ATK";
    private const string _defenseLabel = "DEF";
    private const string _speedLabel = "SPE";
    private const string _statCellPrefix = ">";
    private const string _statCellSuffix = "</strong>";
    private const string _emptyWidth = "width: 0%";
    private const string _fullWidth = "width: 100%";
    private const string _selectMode = "SelectMode";
    private const string _render = "StateHasChanged";
    private const string _ivModeButton = ">IVs</button>";
    private const string _ivBars = "class=\"iv-bars\"";
    private const BindingFlags _privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string _ownedFixture = """
        {"owned_entity_id":"first","capture_kind":"acquisition","ownership_role":"runner","team_commit_context":{"retained_after_commit":true},"species_identity":{"canonical_species_id":"PIKACHU","form_id":0},"display_metadata":{"nickname":"A nickname long enough to fill a narrow lookup panel"},"level":42,"experience":100,"generated_base_stats":{"hp":35,"attack":55,"defense":40,"special_attack":50,"special_defense":50,"speed":90},"computed_stats":{"maximum_hp":90,"attack":50,"defense":40,"special_attack":45,"special_defense":45,"speed":70},"moves":[],"training_traits":{"iv":{"hp":0,"attack":31,"defense":12,"special_attack":24,"special_defense":8,"speed":30},"effective_iv":{"hp":31,"attack":31,"defense":12,"special_attack":24,"special_defense":8,"speed":30}}}
        """;

    /// <summary>
    /// Matches exact symbolic or legacy numeric species and preserves missing values and real zero IVs.
    /// </summary>
    [Fact]
    public void RecordedIndividualsRequireTheSelectedSpeciesAndForm()
    {
        JsonElement payload = JsonSerializer.Deserialize<JsonElement>(_payload);
        OwnedPokemonIvRecord record = Assert.IsType<OwnedPokemonIvRecord>(OwnedPokemonIvRecord.Read(payload, _species, 25));
        Assert.Equal(0, record.Ivs!.Hp);
        Assert.Equal(31, record.Ivs.Attack);
        Assert.Null(record.Ivs.Speed);
        Assert.Equal(31, record.EffectiveIvs!.Hp);
        Assert.True(record.Runner);
        Assert.Equal(42, record.Level);
        Assert.Null(OwnedPokemonIvRecord.Read(payload, _otherSpecies, 26));
        Assert.Null(OwnedPokemonIvRecord.Read(payload, _wrongForm, 25));
        OwnedPokemonIvRecord legacy = Assert.IsType<OwnedPokemonIvRecord>(OwnedPokemonIvRecord.Read(JsonSerializer.Deserialize<JsonElement>(_legacyPayload), _species, 25));
        Assert.Null(legacy.Ivs);
        Assert.Null(legacy.EffectiveIvs);
    }

    /// <summary>
    /// Renders real zeroes, unknown entries and Hyper Training without changing the base-stat views.
    /// </summary>
    /// <returns>A task completing after the production IV component has rendered.</returns>
    [Fact]
    public async Task IndividualValuesRenderBarsAndHideMissingRecords()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new IvLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            OwnedPokemonIvRecord record = OwnedPokemonIvRecord.Read(JsonSerializer.Deserialize<JsonElement>(_payload), _species, 25)!;
            var view = await renderer.RenderComponentAsync<PokemonIndividualValues>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PokemonIndividualValues.Ivs)] = record.Ivs, [nameof(PokemonIndividualValues.EffectiveIvs)] = record.EffectiveIvs }));
            string html = view.ToHtmlString();
            Assert.Contains(_zeroCell, html);
            Assert.Contains(_perfectCell, html);
            Assert.Contains(_effectiveHeading, html);
            Assert.Contains(_emptyWidth, html);
            Assert.Contains(_fullWidth, html);
            Assert.DoesNotContain(_table, html);
            string[] labels = [_hpLabel, _specialAttackLabel, _specialDefenseLabel, _attackLabel, _defenseLabel, _speedLabel];
            int position = -1;
            foreach (string label in labels)
            {
                int next = html.IndexOf($"{_statCellPrefix}{label}{_statCellSuffix}", StringComparison.Ordinal);
                Assert.True(next > position);
                position = next;
            }

            var missing = await renderer.RenderComponentAsync<PokemonIndividualValues>(ParameterView.Empty);
            Assert.True(string.IsNullOrWhiteSpace(missing.ToHtmlString()));
            Assert.False(PokemonIndividualValues.HasKnownValues(new() { Hp = -1, Attack = 32 }));
            Assert.True(PokemonIndividualValues.HasKnownValues(new() { Hp = 0 }));
            var ordinary = await renderer.RenderComponentAsync<PokemonIndividualValues>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PokemonIndividualValues.Ivs)] = record.Ivs, [nameof(PokemonIndividualValues.EffectiveIvs)] = record.Ivs }));
            Assert.DoesNotContain(_effectiveHeading, ordinary.ToHtmlString());
        });
    }

    /// <summary>
    /// Renders the archive picker from the real collection store, including an earlier owned individual and excluding a different run.
    /// </summary>
    /// <returns>A task completing after collection ingestion and production component rendering.</returns>
    [Fact]
    public async Task ArchivePickerReadsOnlySelectedRunAndPreservesEarlierOwnedIndividuals()
    {
        using TestWorkspace workspace = new();
        await using GameplayCollectionStore store = new(workspace.Root);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new IvLocalizer());
        services.AddSingleton(store);
        IvComponentActivator components = new();
        services.AddSingleton<IComponentActivator>(components);
        await using ServiceProvider provider = services.BuildServiceProvider();
        JsonObject first = JsonNode.Parse(_ownedFixture)!.AsObject();
        JsonObject second = first.DeepClone().AsObject();
        second[_ownedEntity] = _secondIndividual;
        second[_displayMetadata]![_nicknameProperty] = _secondNickname;
        JsonObject foreign = first.DeepClone().AsObject();
        foreign[_displayMetadata]![_nicknameProperty] = _foreignNickname;
        foreign.Remove(_trainingTraits);
        JsonElement[] occurrences = [OwnedOccurrence(first, 1, _attemptId), OwnedOccurrence(second, 2, _attemptId), OwnedOccurrence(foreign, 3, _otherAttemptId)];
        await store.AcceptBatchAsync(JsonSerializer.SerializeToElement(new { schema_version = 1, profile_id = _profileId, epoch_id = _epochId, stream_id = _streamId, events = occurrences }));
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<OwnedPokemonIndividualValues>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OwnedPokemonIndividualValues.Recipe)] = CreateRecipe(_attemptId),
                [nameof(OwnedPokemonIndividualValues.SpeciesId)] = _species,
                [nameof(OwnedPokemonIndividualValues.SpeciesName)] = _speciesName
            }));
            typeof(BaseStatVisualization).GetMethod(_selectMode, _privateInstance)!.Invoke(components.Stats, [BaseStatDisplayMode.IndividualValues]);
            typeof(ComponentBase).GetMethod(_render, _privateInstance)!.Invoke(components.Stats, null);
            string html = view.ToHtmlString();
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, _optionMarkup).Count);
            Assert.Contains(_secondNickname, html);
            Assert.DoesNotContain(_foreignNickname, html);
            Assert.Contains(_zeroCell, html);
            string? renderPath = Environment.GetEnvironmentVariable(_renderPathVariable);
            if (!string.IsNullOrWhiteSpace(renderPath))
                await File.WriteAllTextAsync(renderPath, html);

            var missing = await renderer.RenderComponentAsync<OwnedPokemonIndividualValues>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OwnedPokemonIndividualValues.Recipe)] = CreateRecipe(_otherAttemptId),
                [nameof(OwnedPokemonIndividualValues.SpeciesId)] = _species,
                [nameof(OwnedPokemonIndividualValues.SpeciesName)] = _speciesName
            }));
            Assert.DoesNotContain(_ivModeButton, missing.ToHtmlString());
            Assert.DoesNotContain(_ivBars, missing.ToHtmlString());
        });
    }

    /// <summary>
    /// Creates two recipes whose replayed seed shares a run ID but whose actual collection attempts differ.
    /// </summary>
    /// <param name="attempt">The exact collection association for this completed attempt.</param>
    /// <returns>The selected archive recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string attempt) => new()
    {
        RunId = _run,
        Result = _lost,
        CollectionAttemptIds = [attempt],
        GenerationProfileId = _profileId,
        GameVersion = _run,
        IronmonVersion = _run,
        Configuration = new() { WildPolicy = _run, TrainerPolicy = _run, UnfusionSetting = _run },
        SpeciesGenerator = new() { PoolFingerprint = _profileId },
        AbilityGenerator = new() { PoolFingerprint = _profileId },
        PlayerFusionGenerator = new() { PoolFingerprint = _profileId }
    };

    /// <summary>
    /// Wraps an actual owned configuration in its exact attempt-scoped collection occurrence.
    /// </summary>
    /// <param name="payload">The recorded owned configuration.</param>
    /// <param name="sequence">The contiguous source sequence.</param>
    /// <param name="attempt">The actual collection attempt identity.</param>
    /// <returns>The native-shaped event envelope.</returns>
    private static JsonElement OwnedOccurrence(JsonObject payload, int sequence, string attempt)
        => JsonSerializer.SerializeToElement(new { sequence = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), type = _ownedEvent, attempt_id = attempt, context = new { }, payload });

    /// <summary>
    /// Captures the actual Stats grid for switching its existing display mode.
    /// </summary>
    private sealed class IvComponentActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the latest rendered production Stats grid.
        /// </summary>
        public BaseStatVisualization? Stats { get; private set; }

        /// <summary>
        /// Creates a component and captures the grid used by the owned-Pokemon presentation.
        /// </summary>
        /// <param name="componentType">The requested production component type.</param>
        /// <returns>The actual component instance.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is BaseStatVisualization stats)
                Stats = stats;

            return component;
        }
    }

    /// <summary>
    /// Resolves production localization resources for component rendering.
    /// </summary>
    private sealed class IvLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly System.Resources.ResourceManager _manager = new(_resources, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the production resource value.
        /// </summary>
        /// <param name="name">The exact resource key.</param>
        /// <returns>The localized resource value.</returns>
        public LocalizedString this[string name] => new(name, _manager.GetString(name) ?? name);

        /// <summary>
        /// Gets a formatted production resource value.
        /// </summary>
        /// <param name="name">The exact resource key.</param>
        /// <param name="arguments">The values substituted into the localized resource.</param>
        /// <returns>The formatted localized resource value.</returns>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns an empty catalog because this fixture requests individual resources.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent cultures are included.</param>
        /// <returns>An empty catalog.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
