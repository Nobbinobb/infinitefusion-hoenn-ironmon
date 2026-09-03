using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Enemy;
using Ironmon.Tracker.App.Components.Player;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Protocol.Live;
using Ironmon.Tracker.Protocol.Pokemon;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Defense;

/// <summary>
/// Exercises production defense navigation, live refresh and conditional-state rendering.
/// </summary>
public sealed class DefenseViewTests
{
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _detailsElement = "<details";
    private const string _openAttribute = " open";
    private const string _openAction = "OpenDefense";
    private const string _closeAction = "CloseDefense";
    private const string _renderAction = "StateHasChanged";
    private const string _viewClass = "defense-view";
    private const string _rootName = "IronmonDefenseUiTests";
    private const string _firstId = "first";
    private const string _secondId = "second";
    private const string _species = "CHARIZARD";
    private const string _type = "GRASS";
    private const string _fireType = "FIRE";
    private const string _normalType = "NORMAL";
    private const string _rockType = "ROCK";
    private const string _waterType = "WATER";
    private const string _leechSeedProtection = "type_leech_seed";
    private const string _status = "NONE";
    private const string _gender = "unknown";
    private const string _transformedHeading = "TRANSFORMED";
    private const string _transformedSpeciesId = "PIKACHU:0";
    private const string _transformedSpecies = "Pikachu";
    private const string _transformedNickname = "Ditto";
    private const string _transformedAbility = "Static";
    private const string _electricType = "ELECTRIC";
    private const string _copiedMoveId = "THUNDERBOLT";
    private const string _copiedMove = "Thunderbolt";
    private const string _storedMoveId = "TRANSFORM";
    private const string _storedMove = "Transform";
    private const string _storedMoveMarkup = ">Transform<";
    private const string _hardyNature = "Hardy";
    private const string _copiedStatsLabel = "copied stats";
    private const string _copiedAttackMarkup = ">120<";
    private const string _ppItemMovesMethod = "GetPpItemMoves";

    /// <summary>
    /// Verifies both card entry points refresh an open view and close it when its individual changes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CardsNavigateAndRefreshWithoutCarryingDefenseToAnotherPokemon(bool enemy)
    {
        CapturingActivator activator = new();
        string root = Path.Combine(Path.GetTempPath(), _rootName, Guid.NewGuid().ToString());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new DefenseLocalizer());
        services.AddSingleton(new TrackerKnowledgeStore(new TrackerKnowledgeOptions(root)));
        services.AddSingleton(new PokemonSpriteDialogService());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Type cardType = enemy ? typeof(EnemyCard) : typeof(PlayerCard);
            var view = await renderer.RenderComponentAsync(cardType, CardParameters(enemy, _firstId, Example(enemy)));
            ComponentBase card = activator.Card!;
            Assert.DoesNotContain(_viewClass, view.ToHtmlString());
            Assert.Contains("pokemon-type-link", view.ToHtmlString());
            Assert.DoesNotContain("class=\"defense-link\"", view.ToHtmlString());
            Invoke(card, _openAction);
            string open = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains(_viewClass, open);
            Assert.Contains("Hyper Voice", open);
            Assert.Contains("Sound", open);
            Assert.DoesNotContain("Soundproof", open);
            Assert.DoesNotContain("Blocks sound moves", open);
            Assert.DoesNotContain("defense-neutral", open);
            Assert.DoesNotContain("Damage modifiers</h3>", open);
            Assert.DoesNotContain("Not active", open);
            Assert.DoesNotContain("Details & conditions", open);
            Assert.Contains($"0{CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}125×", open);
            Assert.Equal(enemy, open.Contains("Known defenses only", StringComparison.Ordinal));

            await card.SetParametersAsync(CardParameters(enemy, _firstId, new DefenseOverviewSnapshot { InBattle = true, AbilityName = "Updated ability", Protections = [new() { Label = "Hail", Active = true }] }));
            Assert.Contains(_viewClass, view.ToHtmlString());
            Assert.Contains("Hail", view.ToHtmlString());
            Assert.DoesNotContain("Updated ability", view.ToHtmlString());
            Assert.DoesNotContain("Hyper Voice", view.ToHtmlString());
            Invoke(card, _closeAction);
            Assert.DoesNotContain(_viewClass, view.ToHtmlString());
            Invoke(card, _openAction);
            await card.SetParametersAsync(CardParameters(enemy, _secondId, Example(enemy)));
            Assert.DoesNotContain(_viewClass, view.ToHtmlString());
        });
    }

    /// <summary>
    /// Verifies the player card renders copied Transform state while retaining the original PP-item targets.
    /// </summary>
    [Fact]
    public async Task TransformedPlayerCardUsesCurrentBattleProjectionAndStoredItemMoves()
    {
        CapturingActivator activator = new();
        string root = Path.Combine(Path.GetTempPath(), _rootName, Guid.NewGuid().ToString());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new DefenseLocalizer());
        services.AddSingleton(new TrackerKnowledgeStore(new TrackerKnowledgeOptions(root)));
        services.AddSingleton(new PokemonSpriteDialogService());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        PlayerMoveSnapshot currentMove = new() { Id = _copiedMoveId, Name = _copiedMove, Type = _electricType, CurrentPp = 5, TotalPp = 5 };
        PlayerMoveSnapshot storedMove = new() { Id = _storedMoveId, Name = _storedMove, Type = _normalType, CurrentPp = 9, TotalPp = 10 };
        PlayerPokemonSnapshot player = new()
        {
            PokemonId = _firstId,
            SpeciesId = _transformedSpeciesId,
            Nickname = _transformedNickname,
            SpeciesName = _transformedSpecies,
            Transformed = true,
            Gender = _gender,
            Status = _status,
            Ability = _transformedAbility,
            Nature = _hardyNature,
            Healing = new(),
            Types = [_electricType],
            Moves = [currentMove],
            StoredMoves = [storedMove],
            Attack = 120
        };

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<PlayerCard>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PlayerCard.Player)] = player }));
            string html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains(_transformedHeading, html);
            Assert.Contains(_transformedSpecies, html);
            Assert.Contains(_transformedAbility, html);
            Assert.Contains(_copiedMove, html);
            Assert.DoesNotContain(_storedMoveMarkup, html);
            Assert.Contains(_hardyNature, html);
            Assert.DoesNotContain(_copiedStatsLabel, html);
            Assert.Contains(_copiedAttackMarkup, html);

            PlayerCard card = Assert.IsType<PlayerCard>(activator.Card);
            MethodInfo method = typeof(PlayerCard).GetMethod(_ppItemMovesMethod, BindingFlags.NonPublic | BindingFlags.Instance)!;
            IReadOnlyList<PlayerMoveSnapshot> itemMoves = Assert.IsType<IReadOnlyList<PlayerMoveSnapshot>>(method.Invoke(card, null), exactMatch: false);
            Assert.Same(storedMove, Assert.Single(itemMoves));
        });
    }

    /// <summary>
    /// Verifies older snapshots show an unavailable state rather than fabricated defenses.
    /// </summary>
    [Fact]
    public async Task MissingOverviewExplainsCompatibility()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new DefenseLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<PokemonDefenseView>();
            Assert.Contains("updated Ironmon scripts", view.ToHtmlString());
            Assert.DoesNotContain("Incoming types", view.ToHtmlString());
        });
    }

    /// <summary>
    /// Verifies nullable conditions and fractional multipliers survive the actual protocol configuration.
    /// </summary>
    [Fact]
    public void ProtocolPreservesUnknownConditionsAndFractions()
    {
        DefenseOverviewSnapshot original = Example(true);
        string json = JsonSerializer.Serialize(original, TrackerJson.Options);
        DefenseOverviewSnapshot copy = JsonSerializer.Deserialize<DefenseOverviewSnapshot>(json, TrackerJson.Options)!;
        Assert.Null(copy.Modifiers[0].Active);
        Assert.False(copy.Modifiers[1].Active);
        Assert.True(copy.MoveProtections[0].Active);
        Assert.Equal(0.125m, copy.TypeMatchups[1].Multiplier);
        Assert.True(copy.LimitedInformation);
    }

    /// <summary>
    /// Verifies combined neutral results disappear and neutral category values are omitted from split rows.
    /// </summary>
    [Fact]
    public async Task CombinedMatchupsHideNeutralTypesAndAllSourceExplanations()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new DefenseLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        DefenseOverviewSnapshot defense = new()
        {
            AbilityName = "Thick Fat", AbilityDescription = "Halves Fire and Ice damage.",
            TypeMatchups =
            [
                new() { Type = _fireType, Name = "Fire", BaseMultiplier = 2, Multiplier = 2, PhysicalMin = 1, PhysicalMax = 1, SpecialMin = 1, SpecialMax = 1 },
                new() { Type = _normalType, Name = "Normal", BaseMultiplier = 1, Multiplier = 1 },
                new() { Type = _rockType, Name = "Rock", BaseMultiplier = 2, Multiplier = 2, PhysicalMin = 1, PhysicalMax = 1, SpecialMin = 2, SpecialMax = 2 },
                new() { Type = _waterType, Name = "Water", BaseMultiplier = 1, Multiplier = 1, PhysicalMin = 0.5m, PhysicalMax = 1, SpecialMin = 0.5m, SpecialMax = 1 }
            ],
            StatusProtections = [new() { Id = _leechSeedProtection, Source = "Grass typing", Description = "Immune to Leech Seed.", Summary = "Leech Seed immunity", Active = true }],
            Protections = [new() { Label = "Leech Seed", Active = true }, new() { Label = "Powder", Active = true, Moves = ["Spore", "Sleep Powder"] }, new() { Label = "Hail", Active = true }, new() { Label = "Hail", Active = true }],
            Recovery = [new() { Label = "Hail", HealingAmounts = ["+1/16 HP per turn"], Active = true }]
        };
        DefenseOverviewSnapshot copy = TrackerJson.DeserializePayload<DefenseOverviewSnapshot>(TrackerJson.SerializePayload(defense));
        Assert.Equal(1, copy.TypeMatchups[0].PhysicalMax);
        Assert.Equal("Halves Fire and Ice damage.", copy.AbilityDescription);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<PokemonDefenseView>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PokemonDefenseView.Defense)] = copy }));
            string html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.DoesNotContain("Halves Fire and Ice damage.", html);
            Assert.DoesNotContain("Thick Fat", html);
            Assert.DoesNotContain(">Fire</span>", html);
            Assert.DoesNotContain(">Normal</span>", html);
            Assert.Contains(">Rock</span>", html);
            Assert.DoesNotContain("<strong>1×</strong> physical", html);
            Assert.Contains("<strong>2×</strong> special", html);
            Assert.Contains("–1×", html);
            Assert.Contains("Leech Seed", html);
            Assert.DoesNotContain("immunity", html);
            Assert.DoesNotContain("Grass typing", html);
            Assert.Contains("<li>Spore</li>", html);
            Assert.Contains("Recovery", html);
            Assert.Contains("+1/16 HP per turn", html);
            Assert.DoesNotContain("Hail · +1/16 HP per turn", html);
            Assert.DoesNotContain("Details & conditions", html);
            Assert.DoesNotContain("Current types alone", html);
            Assert.Equal(2, html.Split("<details", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("defense-neutral", html);
        });
    }

    /// <summary>
    /// Verifies independent heals stay counted, disclosures start closed, and empty recovery is omitted.
    /// </summary>
    [Fact]
    public async Task RecoveryPreservesStackingAndOmitsEmptySections()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new DefenseLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            DefenseEffectSnapshot[] effects =
            [
                new() { Label = "Turn end", Active = true, HealingAmounts = ["+1/16 HP per turn"] },
                new() { Label = "Turn end", Active = true, HealingAmounts = ["+1/16 HP per turn"] },
                new() { Label = "Next turn end", Active = true, HealingAmounts = ["+1/2 original max HP (once)"] },
                new() { Label = "Suppressed", Active = false, HealingAmounts = ["+1/4 HP"] }
            ];
            var view = await renderer.RenderComponentAsync<DefenseRuleList>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DefenseRuleList.Heading)] = "Recovery", [nameof(DefenseRuleList.Effects)] = effects }));
            string html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("×2", html);
            Assert.Equal(1, html.Split("+1/16 HP per turn", StringSplitOptions.None).Length - 1);
            Assert.Contains("Next turn end", html);
            Assert.Contains("+1/2 original max HP (once)", html);
            Assert.DoesNotContain("Suppressed", html);
            Assert.DoesNotContain(_openAttribute, html);
            Assert.Equal(2, html.Split(_detailsElement, StringSplitOptions.None).Length - 1);
            var empty = await renderer.RenderComponentAsync<DefenseRuleList>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DefenseRuleList.Heading)] = "Recovery", [nameof(DefenseRuleList.Effects)] = Array.Empty<DefenseEffectSnapshot>() }));
            Assert.DoesNotContain("Recovery", empty.ToHtmlString());
        });
    }

    /// <summary>
    /// Builds visible sample data with distinct active, inactive and unknown prerequisites.
    /// </summary>
    private static DefenseOverviewSnapshot Example(bool enemy) => new()
    {
        InBattle = true, LimitedInformation = enemy, AbilityName = "Soundproof", AbilityDescription = "Blocks sound moves.",
        TypeMatchups = [new() { Name = "Rock", Type = _rockType, BaseMultiplier = 8, Multiplier = 8 }, new() { Name = "Grass", Type = _type, BaseMultiplier = 0.125m, Multiplier = 0.125m }],
        Modifiers = [new() { Source = "Conditional defense", Description = "Requires unknown HP.", Active = null }, new() { Source = "Suppressed defense", Description = "Suppressed.", Active = false }],
        MoveProtections = [new() { Source = "Soundproof", Description = "Blocks sound moves.", Active = true, Moves = ["Hyper Voice", "Sing"] }],
        Protections = [new() { Label = "Sound", Active = true, Moves = ["Hyper Voice", "Sing"] }]
    };

    /// <summary>
    /// Builds legal card parameters for either side.
    /// </summary>
    private static ParameterView CardParameters(bool enemy, string id, DefenseOverviewSnapshot defense)
    {
        if (enemy)
            return ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(EnemyCard.Enemies)] = new EnemyPokemonSnapshot[] { new() { EnemyId = id, SpeciesId = _species, SpeciesName = "Charizard", Level = 50, Types = [_type], DefensiveOverview = defense } } });

        PlayerPokemonSnapshot player = new() { PokemonId = id, SpeciesId = _species, Nickname = "Charizard", SpeciesName = "Charizard", Gender = _gender, Status = _status, Ability = "Soundproof", Healing = new(), Types = [_type], DefensiveOverview = defense };
        return ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PlayerCard.Player)] = player });
    }

    /// <summary>
    /// Runs a production event handler and renders the resulting component state.
    /// </summary>
    private static void Invoke(ComponentBase component, string action)
    {
        component.GetType().GetMethod(action, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
        typeof(ComponentBase).GetMethod(_renderAction, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
    }

    /// <summary>
    /// Captures the actual card while leaving ordinary component activation unchanged.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the rendered player or enemy card.
        /// </summary>
        public ComponentBase? Card { get; private set; }

        /// <summary>
        /// Creates one framework component and records the relevant card.
        /// </summary>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is PlayerCard or EnemyCard)
                Card = (ComponentBase)component;

            return component;
        }
    }

    /// <summary>
    /// Resolves the production resource text without desktop startup dependencies.
    /// </summary>
    private sealed class DefenseLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the production text for one key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Formats production text with the supplied values.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns an empty catalog because the renderer requests individual keys.
        /// </summary>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
