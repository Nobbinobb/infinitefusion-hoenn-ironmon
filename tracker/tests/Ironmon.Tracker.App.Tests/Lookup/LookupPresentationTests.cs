using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Protocol.Lookup;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Resources;

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
    /// <returns>The rendered component markup.</returns>
    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new LookupLocalizer());
        services.AddSingleton(new PokemonSpriteDialogService());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString());
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
