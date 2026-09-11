using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Protocol.Pokemon;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Resources;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Checks disclosure boundaries and split progress in the production lookup presentation.
/// </summary>
public sealed class LookupPresentationTests
{
    private const string _lost = "lost";
    private const string _bag = "Bag";
    private const string _entryId = "item:10:1";
    private const string _itemId = "POTION";
    private const string _itemName = "Potion";
    private const string _secondItemId = "ANTIDOTE";
    private const string _secondItemName = "Antidote";
    private const string _category = "hp_recovery";
    private const string _kind = "ground";
    private const string _areaId = "area:10";
    private const string _areaName = "Route 102";
    private const string _trainerId = "trainer:10:1";
    private const string _trainerType = "Youngster";
    private const string _trainerName = "Allen";
    private const string _speciesId = "GENGAR";
    private const string _speciesName = "Gengar";
    private const string _fusionId = "encounter_fusion:10:standard_cross:0:Land:1:0:Water:2";
    private const string _origin = "standard_cross";
    private const string _land = "Land";
    private const string _water = "Water";
    private const string _environment = "grass";
    private const string _abilityId = "VOLTABSORB";
    private const string _abilityName = "Volt Absorb";
    private const string _moveId = "BUBBLEBEAM";
    private const string _moveName = "Bubble Beam";
    private const string _moveType = "WATER";
    private const string _moveDescription = "May lower the target's Speed.";
    private const string _togglePokemon = "TogglePokemon";
    private const string _selectDescription = "SelectDescription";
    private const string _openPokemon = "OpenPokemonAsync";
    private const string _stateHasChanged = "StateHasChanged";
    private const string _renderPathVariable = "IRONMON_LOOKUP_RENDER_PATH";
    private const string _longSpeciesName = "Feraligatross";
    private const string _partyChoiceMarkup = "class=\"obsidian-lookup-party-choice\"";
    private const string _partyLinkMarkup = "class=\"obsidian-lookup-party-link\"";
    private const string _battleMoveMarkup = "class=\"obsidian-lookup-battle-move ";
    private const string _expandedMarkup = "aria-expanded=\"true\"";
    private const string _waterTypeClass = "type-water";
    private const string _specialCategoryClass = "category-icon special";
    private const string _effectMarkup = "role=\"status\">";
    private const string _paragraphEnd = "</p>";
    private const BindingFlags _privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Exercises a full party with a selected lower row and retains every direct lookup action.
    /// </summary>
    /// <returns>The dense production markup checks.</returns>
    [Fact]
    public async Task DenseTrainerPartyKeepsAllSixMembersAndTheirLookupActions()
    {
        AreaTrainerEntryPayload trainer = new()
        {
            EntryId = _trainerId, TrainerType = _trainerType, TrainerName = _trainerName,
            DetailsRevealed = true, PartySize = 6,
            Party = [.. Enumerable.Range(1, 6).Select(slot => new AreaTrainerPokemonPayload
            {
                Slot = slot, SpeciesId = _speciesId, SpeciesName = _longSpeciesName, Level = 100,
                AbilitiesRevealed = true, MovesRevealed = true,
                Abilities = [new() { Id = _abilityId, Name = _abilityName, Description = _moveDescription }],
                Moves = [.. Enumerable.Range(1, 4).Select(_ => new AreaTrainerMovePayload { Id = _moveId, Name = _moveName, Type = _moveType, Category = MoveCategory.Special, Description = _moveDescription })]
            })]
        };
        string html = await RenderAsync<ObsidianLookupTrainer>(new()
        {
            [nameof(ObsidianLookupTrainer.Trainer)] = trainer,
            [nameof(ObsidianLookupTrainer.CanViewAbilities)] = true,
            [nameof(ObsidianLookupTrainer.CanViewMoves)] = true,
            [nameof(ObsidianLookupTrainer.CanLookupPokemon)] = true
        }, (component, markup) =>
        {
            typeof(ObsidianLookupTrainer).GetMethod(_togglePokemon, _privateInstance)!.Invoke(component, [4]);
            typeof(ObsidianLookupTrainer).GetMethod(_selectDescription, _privateInstance)!.Invoke(component, [_moveDescription]);
            typeof(ComponentBase).GetMethod(_stateHasChanged, _privateInstance)!.Invoke(component, null);
            Assert.Equal(6, markup().Split(_partyChoiceMarkup, StringSplitOptions.None).Length - 1);
            Assert.Equal(6, markup().Split(_partyLinkMarkup, StringSplitOptions.None).Length - 1);
            Assert.Equal(4, markup().Split(_battleMoveMarkup, StringSplitOptions.None).Length - 1);
            Assert.Single(markup().Split(_expandedMarkup, StringSplitOptions.None).Skip(1));
            return Task.CompletedTask;
        });
        string? renderPath = Environment.GetEnvironmentVariable(_renderPathVariable);
        if (!string.IsNullOrEmpty(renderPath))
            await File.WriteAllTextAsync(renderPath, html);
    }

    /// <summary>
    /// Keeps trainer abilities, equipped moves, and direct navigation independently gated.
    /// </summary>
    /// <param name="abilities">Whether ability information is allowed.</param>
    /// <param name="moves">Whether move information is allowed.</param>
    /// <returns>The production component interaction checks.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TrainerBattleSetUsesIndependentGrantsAndEffectOnlyDescriptions(bool abilities, bool moves)
    {
        string? navigated = null;
        AreaTrainerPokemonPayload pokemon = new()
        {
            Slot = 1, SpeciesId = _speciesId, SpeciesName = _speciesName, Level = 35,
            AbilitiesRevealed = true, MovesRevealed = true,
            Abilities = [new() { Id = _abilityId, Name = _abilityName, Description = _moveDescription }],
            Moves = [new() { Id = _moveId, Name = _moveName, Type = _moveType, Category = MoveCategory.Special, Description = _moveDescription }]
        };
        AreaTrainerEntryPayload trainer = new()
        {
            EntryId = _trainerId, TrainerType = _trainerType, TrainerName = _trainerName,
            DetailsRevealed = true, PartySize = 1, Party = [pokemon]
        };
        await RenderAsync<ObsidianLookupTrainer>(new()
        {
            [nameof(ObsidianLookupTrainer.Trainer)] = trainer,
            [nameof(ObsidianLookupTrainer.CanViewAbilities)] = abilities,
            [nameof(ObsidianLookupTrainer.CanViewMoves)] = moves,
            [nameof(ObsidianLookupTrainer.CanLookupPokemon)] = abilities || moves,
            [nameof(ObsidianLookupTrainer.PokemonSelected)] = EventCallback.Factory.Create<string>(this, species => navigated = species)
        }, async (component, html) =>
        {
            Assert.DoesNotContain(_moveName, html());
            typeof(ObsidianLookupTrainer).GetMethod(_togglePokemon, _privateInstance)!.Invoke(component, [1]);
            typeof(ComponentBase).GetMethod(_stateHasChanged, _privateInstance)!.Invoke(component, null);
            Assert.Equal(abilities, html().Contains(_abilityName, StringComparison.Ordinal));
            Assert.Equal(moves, html().Contains(_moveName, StringComparison.Ordinal));
            if (moves)
            {
                Assert.Contains(_waterTypeClass, html());
                Assert.Contains(_specialCategoryClass, html());
                typeof(ObsidianLookupTrainer).GetMethod(_selectDescription, _privateInstance)!.Invoke(component, [_moveDescription]);
                typeof(ComponentBase).GetMethod(_stateHasChanged, _privateInstance)!.Invoke(component, null);
                string effect = html().Split(_effectMarkup, StringSplitOptions.None)[1].Split(_paragraphEnd, StringSplitOptions.None)[0];
                Assert.Contains("Speed", effect);
                Assert.DoesNotContain(_moveName, effect);
                Assert.DoesNotContain("Special", effect);
                Assert.DoesNotContain(_moveType, effect);
            }

            await (Task)typeof(ObsidianLookupTrainer).GetMethod(_openPokemon, _privateInstance)!.Invoke(component, [pokemon])!;
            Assert.Equal(abilities || moves ? _speciesId : null, navigated);
        });
    }

    /// <summary>
    /// Verifies hidden and ground pickups cannot expose identities or categories before disclosure.
    /// </summary>
    /// <param name="hidden">Whether the original event is a hidden pickup.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcealedItemsUseOneUnknownIconWithoutCategoryLeaks(bool hidden)
    {
        AreaItemEntryPayload item = new() { EntryId = _entryId, Kind = _kind, Hidden = hidden, Items = [new() { ItemId = _itemId, ItemName = _itemName, Category = _category }] };
        string html = await RenderAsync<ObsidianLookupItem>(new() { [nameof(ObsidianLookupItem.Item)] = item });
        Assert.Contains("Unknown item", html);
        Assert.Contains(hidden ? "Hidden item" : "Ground item", html);
        Assert.DoesNotContain(_itemName, html);
        Assert.DoesNotContain("HP recovery", html);
        Assert.DoesNotContain("title=", html);
        Assert.Contains("Not collected", html);
    }

    /// <summary>
    /// Verifies authorized visibility does not imply collection and preserves every item identity.
    /// </summary>
    [Fact]
    public async Task RevealedItemsKeepIdentityCategoriesSeparateFromCollection()
    {
        AreaItemEntryPayload item = new()
        {
            EntryId = _entryId, Kind = _kind, DetailsRevealed = true,
            Items = [new() { ItemId = _itemId, ItemName = _itemName, Category = _category }, new() { ItemId = _secondItemId, ItemName = _secondItemName }]
        };

        string html = await RenderAsync<ObsidianLookupItem>(new() { [nameof(ObsidianLookupItem.Item)] = item });
        Assert.Contains(_itemName, html);
        Assert.Contains(_secondItemName, html);
        Assert.Contains("HP recovery", html);
        Assert.Contains("General utility", html);
        Assert.Contains("Not collected", html);
        Assert.DoesNotContain("obsidian-lookup-item-icon collected", html);
    }

    /// <summary>
    /// Verifies undisclosed parties show their size and slots but never species or levels.
    /// </summary>
    [Fact]
    public async Task ConcealedTrainerPartyIgnoresUnrevealedIdentityData()
    {
        AreaTrainerEntryPayload trainer = new()
        {
            EntryId = _trainerId, TrainerType = _trainerType, TrainerName = _trainerName, PartySize = 2,
            Party = [new() { SpeciesId = _speciesId, SpeciesName = _speciesName, Level = 87 }]
        };

        string html = await RenderAsync<ObsidianLookupTrainer>(new() { [nameof(ObsidianLookupTrainer.Trainer)] = trainer });
        Assert.Contains(_trainerName, html);
        Assert.Contains("Slot 1", html);
        Assert.Contains("Slot 2", html);
        Assert.Contains("Not defeated", html);
        Assert.DoesNotContain(_speciesName, html);
        Assert.DoesNotContain("87", html);
    }

    /// <summary>
    /// Verifies concealed chance fusions retain both source slots, levels, origin and conditional chance.
    /// </summary>
    [Fact]
    public async Task ChanceFusionRetainsItsMechanicAndSourcesWithoutIdentity()
    {
        AreaEncounterFusionEntryPayload fusion = new()
        {
            EntryId = _fusionId, Origin = _origin, Environment = _environment, CrossEnvironment = true,
            FirstEncounterType = _land, FirstSlot = 1, SecondEncounterType = _water, SecondSlot = 2,
            MinimumLevel = 4, MaximumLevel = 6, FusionChancePercent = 5, SpeciesName = _speciesName
        };

        string html = await RenderAsync<ObsidianLookupEncounter>(new() { [nameof(ObsidianLookupEncounter.Fusion)] = fusion });
        Assert.Contains("Land slot 1", html);
        Assert.Contains("Water slot 2", html);
        Assert.Contains("5% fusion roll", html);
        Assert.Contains("standard encounter", html);
        Assert.Contains("Unknown", html);
        Assert.DoesNotContain(_speciesName, html);
    }

    /// <summary>
    /// Verifies old peers keep a combined total while new peers expose both independent counters.
    /// </summary>
    /// <param name="split">Whether the peer supplies the new count fields.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SummarySplitsOnlyWhenBothTotalsAreAvailable(bool split)
    {
        AreaSummaryPayload area = new()
        {
            AreaId = _areaId, Name = _areaName, EncounterTotal = 100, Encountered = 3,
            EncounterSlotTotal = split ? 18 : null, EncounterFusionTotal = split ? 82 : null,
            EncounterSlotsEncountered = 2, EncounterFusionsEncountered = 1
        };

        string html = await RenderAsync<ObsidianLookupProgress>(new() { [nameof(ObsidianLookupProgress.Area)] = area, [nameof(ObsidianLookupProgress.Category)] = AreaContentCategory.Encounter });
        if (split)
        {
            Assert.Contains("2 / 18", html);
            Assert.Contains("1 / 82", html);
            Assert.Contains("Chance fusions", html);
        }
        else
        {
            Assert.Contains("3 / 100", html);
            Assert.DoesNotContain("Chance fusions", html);
        }
    }

    /// <summary>
    /// Verifies archive summaries keep numeric BST boundaries without exposing obsolete species rankings.
    /// </summary>
    [Fact]
    public async Task ArchiveSummaryKeepsUsageAndNumericBoundaries()
    {
        RunStatisticsPayload statistics = new()
        {
            Result = _lost, TrainerDefeatedBstMinimum = 195, TrainerDefeatedBstMaximum = 640,
            TrainerSpeciesCounts = new Dictionary<string, int>() { [_speciesId] = 7 }, TrainerSpeciesNames = new Dictionary<string, string>() { [_speciesId] = _speciesName },
            ItemsBySource = new Dictionary<string, Dictionary<string, int>>() { [_bag] = new() { [_itemId] = 2 } }
        };

        string html = await RenderAsync<RunStatistics>(new() { [nameof(RunStatistics.Statistics)] = statistics });
        Assert.Contains("195", html);
        Assert.Contains("640", html);
        Assert.Contains(_itemName, html);
        Assert.DoesNotContain(_speciesId, html);
        Assert.DoesNotContain(_speciesName, html);
        Assert.DoesNotContain("Most encountered", html);
        Assert.DoesNotContain("Items by source", html);
    }

    /// <summary>
    /// Renders one production component using the actual resource set.
    /// </summary>
    /// <typeparam name="T">The component being exercised.</typeparam>
    /// <param name="parameters">The input payloads and presentation parameters.</param>
    /// <param name="check">Optional interaction checks on the renderer dispatcher.</param>
    /// <returns>The rendered component markup.</returns>
    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters, Func<T, Func<string>, Task>? check = null) where T : IComponent
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new LookupLocalizer());
        services.AddSingleton(new PokemonSpriteDialogService());
        LookupActivator<T> activator = new();
        services.AddSingleton<IComponentActivator>(activator);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            if (check is not null)
                await check(activator.Component!, view.ToHtmlString);

            return view.ToHtmlString();
        });
    }

    /// <summary>
    /// Captures a production component for interaction checks on the render dispatcher.
    /// </summary>
    /// <typeparam name="T">The component being tested.</typeparam>
    private sealed class LookupActivator<T> : IComponentActivator where T : IComponent
    {
        /// <summary>
        /// Gets the component created by the renderer.
        /// </summary>
        public T? Component { get; private set; }

        /// <summary>
        /// Creates and records the requested component.
        /// </summary>
        /// <param name="componentType">The renderer's component type.</param>
        /// <returns>The requested component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is T match)
                Component = match;

            return component;
        }
    }

    /// <summary>
    /// Reads production lookup strings without desktop startup services.
    /// </summary>
    private sealed class LookupLocalizer : IStringLocalizer<TrackerResources>
    {
        private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the production text for one resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets formatted production text for one resource key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Provides the resource enumeration contract unused by these component tests.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent cultures are included.</param>
        /// <returns>An empty sequence because the components request individual keys.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
