using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Debug;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Tracker.Connection.Access;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Debug;
using Ironmon.Tracker.Protocol.Lookup;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Access;

/// <summary>
/// Verifies development removal, evolution availability, and permission-scoped diagnostic presentation.
/// </summary>
public sealed class DiagnosticToolsViewTests
{
    private const string _storageName = "IronmonToolsViewTests";
    private const string _version = "test";
    private const string _publish = "Publish";
    private const string _render = "StateHasChanged";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _applyState = "ApplyState";
    private const string _clear = "ClearAsync";
    private const string _changeMove = "SetMoveSelectionAsync";
    private const string _clearHistory = "ClearHistory";
    private const string _moveId = "SHADOWBALL";
    private const string _moveName = "Shadow Ball";
    private const string _pokemonId = "test-pokemon";
    private const string _speciesId = "HAUNTER";
    private const string _speciesName = "Haunter";
    private const string _abilityId = "LEVITATE";
    private const string _abilityName = "Levitate";
    private const string _entryName = "sample-lifecycle";
    private const string _entryValue = "Sample diagnostic detail.";
    private const string _matchesField = "_matches";
    private const string _matchTotalField = "_matchTotal";
    private const BindingFlags _instanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Keeps shared search wording specific to the active or archived run in empty and populated states.
    /// </summary>
    /// <param name="debugMode">Whether the search reads the active diagnostic run.</param>
    /// <returns>A task representing both search presentations.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LookupSearchDescribesItsRunContext(bool debugMode)
    {
        await RenderAsync<PokemonLookupExplorer>(new() { [nameof(PokemonLookupExplorer.DebugMode)] = debugMode }, true, async (component, html) =>
        {
            Assert.Contains(debugMode ? "obtainable in the active run" : "obtainable in the selected archived run", html());
            PokemonSearchMatch[] matches = [new() { SpeciesName = _speciesName, SpeciesId = _speciesId, ObtainabilityStatus = PokemonObtainabilityStatus.Obtainable }];
            typeof(PokemonLookupExplorer).GetField(_matchesField, _instanceMembers)!.SetValue(component, matches);
            typeof(PokemonLookupExplorer).GetField(_matchTotalField, _instanceMembers)!.SetValue(component, matches.Length);
            await InvokeAsync(component, _render);
            Assert.Contains(debugMode ? "1 matches in this active run" : "1 matches in this archived run", html());
            Assert.Contains(_speciesName, html());
            Assert.Contains(_speciesId, html());
            Assert.DoesNotContain("Redesign.Tools.", html());
            if (debugMode)
                Assert.DoesNotContain("archived run", html());
        });
    }

    /// <summary>
    /// Shows only available evolution directions and removes the section when neither is available.
    /// </summary>
    /// <param name="evolve">Whether an evolution exists.</param>
    /// <param name="devolve">Whether a devolution exists.</param>
    /// <returns>A task representing production component rendering.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EvolutionDirectionsAreIndependent(bool evolve, bool devolve)
    {
        await RenderAsync<DebugDevelopmentControls>([], true, async (component, html) =>
        {
            await InvokeAsync(component, _applyState, CreateState(evolve, devolve));
            Assert.Equal(evolve, html().Contains("aria-label=\"Evolve\"", StringComparison.Ordinal));
            Assert.Equal(devolve, html().Contains("aria-label=\"Devolve\"", StringComparison.Ordinal));
            Assert.Equal(evolve || devolve, html().Contains("tools-evolution-options", StringComparison.Ordinal));
            Assert.Contains("Remove move from slot 1", html());
            Assert.Contains("No move", html());
            Assert.DoesNotContain("Redesign.Tools.", html());
        });
    }

    /// <summary>
    /// Prevents loaded development data from exposing controls without their grants.
    /// </summary>
    /// <returns>A task representing permission-limited rendering.</returns>
    [Fact]
    public async Task DevelopmentControlsRequirePermission()
    {
        await RenderAsync<DebugDevelopmentControls>([], false, async (component, html) =>
        {
            await InvokeAsync(component, _applyState, CreateState(true, true));
            Assert.DoesNotContain("tools-moves", html());
            Assert.DoesNotContain("tools-item-row", html());
            Assert.DoesNotContain("tools-evolution-options", html());
            Assert.DoesNotContain("tools-player-controls", html());
        });
    }

    /// <summary>
    /// Restores the last remaining move when removal fails local validation.
    /// </summary>
    /// <returns>A task representing rejection and authoritative field restoration.</returns>
    [Fact]
    public async Task LastMoveCannotBeRemoved()
    {
        await RenderAsync<DebugDevelopmentControls>([], true, async (component, html) =>
        {
            await InvokeAsync(component, _applyState, CreateState(false, false));
            await InvokeAsync(component, _changeMove, 0, null!);
            Assert.Contains("at least one move", html());
            Assert.Contains("value=\"Shadow Ball\"", html());
        });
    }

    /// <summary>
    /// Emits one null selection from an explicit clear action while respecting disabled state.
    /// </summary>
    /// <param name="disabled">Whether the picker is disabled during a request.</param>
    /// <returns>A task representing the clear interaction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitClearRespectsBusyState(bool disabled)
    {
        int calls = 0;
        string? selected = _moveId;
        await RenderAsync<DevelopmentCatalogPicker>(new()
        {
            [nameof(DevelopmentCatalogPicker.Options)] = new[] { new DebugDevelopmentCatalogEntry { Id = _moveId, Name = _moveName } },
            [nameof(DevelopmentCatalogPicker.SelectedId)] = _moveId,
            [nameof(DevelopmentCatalogPicker.AllowClear)] = true,
            [nameof(DevelopmentCatalogPicker.ClearLabel)] = "Remove move",
            [nameof(DevelopmentCatalogPicker.Disabled)] = disabled,
            [nameof(DevelopmentCatalogPicker.SelectedIdChanged)] = EventCallback.Factory.Create<string?>(this, value => { calls++; selected = value; })
        }, false, async (component, html) =>
        {
            await InvokeAsync(component, _clear);
            Assert.Equal(disabled ? 0 : 1, calls);
            Assert.Equal(disabled ? _moveId : null, selected);
        });
    }

    /// <summary>
    /// Keeps tracker-owned data and history hidden without access and preserves raw state after clearing history.
    /// </summary>
    /// <param name="authorized">Whether local diagnostic access is available.</param>
    /// <returns>A task representing diagnostic rendering and history clearing.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtocolDataAndActionsRequireTheirGrants(bool authorized)
    {
        await RenderAsync<DebugProtocolDiagnostics>([], authorized, async (component, html) =>
        {
            Assert.Equal(authorized, html().Contains("Current connection state", StringComparison.Ordinal));
            Assert.Equal(authorized, html().Contains(_entryValue, StringComparison.Ordinal));
            Assert.Equal(authorized, html().Contains("Export report", StringComparison.Ordinal));
            if (!authorized)
                return;

            Assert.Contains("1 retained", html());
            await InvokeAsync(component, _clearHistory);
            Assert.Contains("0 retained", html());
            Assert.Contains("Current connection state", html());
            Assert.DoesNotContain(_entryValue, html());
        });
    }

    /// <summary>
    /// Creates one current player with a single move and independently available evolution directions.
    /// </summary>
    /// <param name="evolve">Whether to include a direct evolution.</param>
    /// <param name="devolve">Whether to include a direct predecessor.</param>
    /// <returns>The detached development state.</returns>
    private static DebugDevelopmentStateSnapshot CreateState(bool evolve, bool devolve)
    {
        return new()
        {
            Player = new() { PokemonId = _pokemonId, SpeciesId = _speciesId, SpeciesName = _speciesName, AbilityId = _abilityId, AbilityName = _abilityName, Level = 32, MoveIds = [_moveId] },
            Abilities = [new() { Id = _abilityId, Name = _abilityName }],
            Moves = [new() { Id = _moveId, Name = _moveName }],
            Evolutions = evolve ? [new() { Id = _speciesId, Name = _speciesName }] : [],
            Devolutions = devolve ? [new() { Id = _speciesId, Name = _speciesName }] : []
        };
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
        ToolsActivator<T> activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(connection.Requests);
        services.AddSingleton(state);
        services.AddSingleton(new Ironmon.Tracker.Connection.RunState.TrackerRunState());
        services.AddSingleton(new TrackerKnowledgeStore(storage));
        TrackerDiagnosticsStore diagnostics = new();
        diagnostics.RecordLifecycle(_entryName, _entryValue);
        services.AddSingleton(diagnostics);
        services.AddSingleton(new DiagnosticAccessService(storage, new(new([])), authorized));
        services.AddSingleton(new PokemonSpriteDialogService());
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, ToolsLocalizer>();
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
    private sealed class ToolsActivator<T> : IComponentActivator where T : IComponent
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
    /// Resolves production tool strings without desktop startup.
    /// </summary>
    private sealed class ToolsLocalizer : IStringLocalizer<TrackerResources>
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
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
