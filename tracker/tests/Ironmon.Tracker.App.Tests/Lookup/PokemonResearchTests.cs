using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Debug;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Debug;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Protocol.Pokemon;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Checks research disclosure, ability details, and the active-run-only swap confirmation boundary.
/// </summary>
public sealed class PokemonResearchTests
{
    private const string _speciesId = "GENGAR";
    private const string _speciesName = "Gengar";
    private const string _abilityId = "SUPERLUCK";
    private const string _abilityName = "Super Luck";
    private const string _originalId = "KEENEYE";
    private const string _originalName = "Keen Eye";
    private const string _description = "Heightens the critical-hit ratios of moves.";
    private const string _storageName = "IronmonResearchTests";
    private const string _version = "test";
    private const string _publish = "Publish";
    private const string _requestSwap = "RequestDevelopmentAction";
    private const string _cancelSwap = "CancelDevelopmentAction";
    private const string _confirmSwap = "ConfirmDevelopmentActionAsync";
    private const string _selectSlot = "SelectSlotAsync";
    private const string _render = "StateHasChanged";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _descriptionJson = "{\"ability_id\":\"SUPERLUCK\",\"ability_name\":\"Super Luck\",\"ability_description\":\"Heightens the critical-hit ratios of moves.\"}";
    private const BindingFlags _instanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Keeps swap unavailable for archives and unprivileged active-run lookup.
    /// </summary>
    /// <param name="active">Whether the card represents an active run.</param>
    /// <param name="authorized">Whether the game and tracker authorize development access.</param>
    /// <returns>A task representing rendering and optional confirmation checks.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SwapRequiresActiveRunPermissionAndConfirmation(bool active, bool authorized)
    {
        await RenderAsync<PokemonLookupCard>(new()
        {
            [nameof(PokemonLookupCard.Pokemon)] = new PokemonLookupSnapshot
            {
                Identity = new() { SpeciesId = _speciesId, SpeciesName = _speciesName },
                Section = PokemonLookupSection.Abilities,
                Abilities = new() { Values = [new() { Id = _abilityId, Name = _abilityName, Description = _description }] }
            },
            [nameof(PokemonLookupCard.DebugMode)] = active
        }, authorized, async (card, html) =>
        {
            Assert.Equal(active && authorized, html().Contains("research-swap", StringComparison.Ordinal));
            Assert.DoesNotContain("role=\"dialog\"", html());
            if (!active || !authorized)
                return;

            await InvokeAsync(card, _requestSwap, DebugDevelopmentAction.SwapPokemon);
            Assert.Contains("role=\"dialog\"", html());
            Assert.Contains(_speciesName, html());
            Assert.Contains("obsidian-dialog-scope", html());
            await InvokeAsync(card, _cancelSwap);
            Assert.DoesNotContain("role=\"dialog\"", html());
            await InvokeAsync(card, _requestSwap, DebugDevelopmentAction.SwapPokemon);
            await InvokeAsync(card, _confirmSwap);
            Assert.Contains("role=\"dialog\"", html());
            Assert.Contains("role=\"alert\"", html());
            Assert.DoesNotContain("disabled", html().Split("development-confirmation-actions", StringSplitOptions.None)[1]);
        });
    }

    /// <summary>
    /// Keeps slot names, eligibility, provenance, and component-only descriptions while hiding raw IDs.
    /// </summary>
    /// <returns>A task representing the rendered slot and description selection.</returns>
    [Fact]
    public async Task RedesignedSlotsOpenComponentDescriptionsWithoutDuplicatingIdentifiers()
    {
        AbilitySnapshot? selected = null;
        DebugAbilitySlotSnapshot slot = new()
        {
            Group = DebugAbilitySlotGroup.BodyGenerated, Kind = DebugAbilitySlotKind.Hidden, Index = 2,
            AbilityId = _abilityId, AbilityName = _abilityName, AbilityDescription = _description,
            OriginalAbilityId = _originalId, OriginalAbilityName = _originalName,
            Eligibility = DebugAbilityEligibility.Universal, RestrictedSourceReplaced = true
        };

        await RenderAsync<DebugAbilitySlots>(new()
        {
            [nameof(DebugAbilitySlots.Slots)] = new[] { slot },
            [nameof(DebugAbilitySlots.Selected)] = EventCallback.Factory.Create<AbilitySnapshot>(this, value => selected = value)
        }, false, async (component, html) =>
        {
            Assert.Contains(_abilityName, html());
            Assert.Contains(_originalName, html());
            Assert.Contains("Hidden 2", html());
            Assert.Contains("Universal", html());
            Assert.DoesNotContain(_abilityId, html());
            Assert.DoesNotContain(_originalId, html());
            await InvokeAsync(component, _selectSlot, slot);
            Assert.Equal(_description, selected?.Description);
        });
    }

    /// <summary>
    /// Reads the optional slot description through the same snake-case protocol contract as the game.
    /// </summary>
    [Fact]
    public void ComponentDescriptionDeserializesFromGamePayload()
    {
        DebugAbilitySlotSnapshot slot = System.Text.Json.JsonSerializer.Deserialize<DebugAbilitySlotSnapshot>(_descriptionJson, TrackerJson.Options)!;
        Assert.Equal(_description, slot.AbilityDescription);
    }

    /// <summary>
    /// Renders real components with an isolated request client that cannot contact a game.
    /// </summary>
    /// <typeparam name="T">The component under test.</typeparam>
    /// <param name="parameters">The component inputs.</param>
    /// <param name="authorized">Whether the detached connection grants development access.</param>
    /// <param name="check">The checks performed on the render dispatcher.</param>
    /// <returns>A task representing rendering and checks.</returns>
    private static async Task RenderAsync<T>(Dictionary<string, object?> parameters, bool authorized, Func<T, Func<string>, Task> check) where T : IComponent
    {
        string directory = Path.Combine(Path.GetTempPath(), _storageName, Guid.NewGuid().ToString());
        TrackerKnowledgeOptions storage = new(directory);
        TrackerConnectionState state = new();
        GameHandshakePayload game = new(_version, _version, true, authorized, directory, null, null);
        typeof(TrackerConnectionState).GetMethod(_publish, _instanceMembers)!.Invoke(state, [TrackerConnectionStatus.Connected, game, null, null]);
        await using TrackerConnectionService connection = new(new(0, _version, authorized, TimeSpan.FromSeconds(1)), new(), state, new(), new(storage), new(storage), new(storage));
        ResearchActivator<T> activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(connection.Requests);
        services.AddSingleton(state);
        services.AddSingleton(new PokemonSpriteDialogService());
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, ResearchLocalizer>();
        try
        {
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
                await check(activator.Component!, view.ToHtmlString);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Invokes a component interaction and updates its rendered state on the dispatcher.
    /// </summary>
    /// <param name="component">The production component.</param>
    /// <param name="method">The interaction method.</param>
    /// <param name="arguments">The interaction arguments.</param>
    /// <returns>A task representing the interaction and render.</returns>
    private static async Task InvokeAsync(IComponent component, string method, params object[] arguments)
    {
        if (component.GetType().GetMethod(method, _instanceMembers)!.Invoke(component, arguments) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(_render, _instanceMembers)!.Invoke(component, null);
    }

    /// <summary>
    /// Captures the real component instance created by the renderer.
    /// </summary>
    /// <typeparam name="T">The component to capture.</typeparam>
    private sealed class ResearchActivator<T> : IComponentActivator where T : IComponent
    {
        /// <summary>
        /// Gets the captured production component.
        /// </summary>
        internal T? Component { get; private set; }

        /// <summary>
        /// Creates the requested component and captures the matching instance.
        /// </summary>
        /// <param name="componentType">The component type requested by the renderer.</param>
        /// <returns>The new component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is T match)
                Component = match;

            return component;
        }
    }

    /// <summary>
    /// Resolves production research strings without desktop startup.
    /// </summary>
    private sealed class ResearchLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the localized value for one key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets the formatted localized value for one key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns the unused enumeration contract for these key-based tests.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent resources should be included.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
