using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.App.Seeds;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Tracker.Connection.Access;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Seeds;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Verifies archive navigation scope, real run selection, and save-file history filtering.
/// </summary>
public sealed class ArchiveNavigationTests
{
    private const string _fixture = "navigation-fixture";
    private const string _firstRun = "first-run";
    private const string _secondRun = "second-run";
    private const string _legacyRun = "legacy-run";
    private const string _firstSlot = "File A";
    private const string _secondSlot = "File B";
    private const string _result = "lost";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _render = "StateHasChanged";
    private const string _selectRun = "SelectRun";
    private const string _showHistory = "ShowHistory";
    private const string _toggle = "Toggle";
    private const string _openHistory = "OpenHistoryAsync";
    private const string _keyDown = "HandleKeyDown";
    private const string _selectSlot = "SelectSlot";
    private const string _selectSection = "SelectSection";
    private const string _selectedSection = "_selectedSection";
    private const string _historyRun = "OpenHistoryRun";
    private const string _runTabs = "obsidian-lookup-tabs obsidian-archive-tabs";
    private const string _navigationSection = "archive-navigation";
    private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Keeps the history view outside run tabs and lets header navigation reopen the selected run.
    /// </summary>
    /// <returns>The integrated archive navigation verification task.</returns>
    [Fact]
    public async Task HeaderSelectionAndGlobalHistoryHaveSeparateScopes()
    {
        await RenderAsync<ArchiveHost>([], async (components, html) =>
        {
            Assert.Contains("archive-run-trigger", html());
            Assert.Contains(_runTabs, html());
            ArchiveView archive = components.Get<ArchiveView>();
            PokemonLookupExplorer explorer = components.Get<PokemonLookupExplorer>();
            await InvokeAsync(archive, _selectSection, ArchiveSection.Pokemon);
            await InvokeAsync(archive, _selectSection, ArchiveSection.Summary);
            await InvokeAsync(archive, _selectSection, ArchiveSection.Pokemon);
            Assert.Same(explorer, components.Get<PokemonLookupExplorer>());
            Assert.Contains("aria-label=\"Pokémon\"", html());
            Assert.Contains("aria-selected=\"true\"", html());
            await InvokeAsync(archive, _showHistory);
            Assert.DoesNotContain(_runTabs, html());
            Assert.Contains("Save-file history", html());
            await InvokeAsync(archive, _selectRun, _secondRun);
            Assert.Equal(ArchiveSection.Pokemon, typeof(ArchiveView).GetField(_selectedSection, _members)!.GetValue(archive));
            Assert.Contains(_runTabs, html());
            Assert.Contains("Run 2", html());
            Assert.DoesNotContain("archive-save-history", html());
            await InvokeAsync(archive, _selectRun, _fixture);
            Assert.Contains("Run 2", html());
            await InvokeAsync(archive, _showHistory);
            await InvokeAsync(archive, _historyRun, _firstRun);
            Assert.Equal(ArchiveSection.Summary, typeof(ArchiveView).GetField(_selectedSection, _members)!.GetValue(archive));
        });
    }

    /// <summary>
    /// Exposes legacy recipes and global history while allowing keyboard dismissal.
    /// </summary>
    /// <returns>The picker interaction verification task.</returns>
    [Fact]
    public async Task PickerRetainsLegacyRunsAndDismissesBeforeHistory()
    {
        int historyRequests = 0;
        await RenderAsync<ArchiveRunPicker>(new()
        {
            [nameof(ArchiveRunPicker.Recipes)] = new[] { CreateRecipe(_firstRun, _firstSlot, 1), CreateRecipe(_legacyRun, null, 0) },
            [nameof(ArchiveRunPicker.HistoryRequested)] = EventCallback.Factory.Create(this, () => historyRequests++)
        }, async (components, html) =>
        {
            ArchiveRunPicker picker = components.Get<ArchiveRunPicker>();
            await InvokeAsync(picker, _toggle);
            Assert.Contains("Save file not recorded", html());
            Assert.Contains("All save files", html());
            await InvokeAsync(picker, _keyDown, new KeyboardEventArgs { Key = "Escape" });
            Assert.DoesNotContain("archive-run-menu", html());
            await InvokeAsync(picker, _toggle);
            await InvokeAsync(picker, _openHistory);
            Assert.Equal(1, historyRequests);
            Assert.DoesNotContain("archive-run-menu", html());
        });
    }

    /// <summary>
    /// Filters recorded runs by file without substituting archive counts for authoritative totals.
    /// </summary>
    /// <returns>The history rendering verification task.</returns>
    [Fact]
    public async Task HistoryFiltersFilesAndKeepsUnknownRecipesAccessible()
    {
        CompletedRunRecipePayload first = CreateRecipe(_firstRun, _firstSlot, 31);
        CompletedRunRecipePayload second = CreateRecipe(_secondRun, _secondSlot, 42);
        await RenderAsync<SaveSlotAttemptHistory>(new()
        {
            [nameof(SaveSlotAttemptHistory.Slots)] = new[] { new KeyValuePair<string, RunStatisticsPayload>(_firstSlot, first.Statistics!), new KeyValuePair<string, RunStatisticsPayload>(_secondSlot, second.Statistics!) },
            [nameof(SaveSlotAttemptHistory.CurrentSaveSlot)] = _firstSlot,
            [nameof(SaveSlotAttemptHistory.Recipes)] = new[] { first, second, CreateRecipe(_legacyRun, null, 0) }
        }, async (components, html) =>
        {
            Assert.Contains("Run 31", html());
            Assert.DoesNotContain("Run 42", html());
            Assert.Contains("Save file not recorded", html());
            Assert.Contains("<strong>7</strong>", html());
            await InvokeAsync(components.Get<SaveSlotAttemptHistory>(), _selectSlot, _secondSlot);
            Assert.DoesNotContain("Run 31", html());
            Assert.Contains("Run 42", html());
        });
    }

    /// <summary>
    /// Creates a valid deterministic recipe with optional recorded statistics.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <param name="slot">The save-file identity, or null for a legacy recipe.</param>
    /// <param name="attempt">The recorded attempt number.</param>
    /// <returns>A recipe accepted by the production archive.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string id, string? slot, int attempt) => new()
    {
        RunId = id, Result = _result, GenerationProfileId = new string('a', 64), GameVersion = _fixture, IronmonVersion = _fixture,
        Configuration = new() { SchemaVersion = 1, WildPolicy = _fixture, TrainerPolicy = _fixture, UnfusionSetting = _fixture },
        SpeciesGenerator = new() { Version = 1, PoolFingerprint = _fixture }, AbilityGenerator = new() { Version = 1, PoolSize = 1, PoolFingerprint = _fixture }, PlayerFusionGenerator = new() { Version = 1, PoolSize = 1, PoolFingerprint = _fixture },
        Statistics = slot is null ? null : new() { SchemaVersion = 1, Result = _result, SaveSlot = slot, AttemptNumber = attempt, AttemptsStarted = 7 }
    };

    /// <summary>
    /// Renders production components with isolated persistence and no live game transport.
    /// </summary>
    /// <typeparam name="T">The root component type.</typeparam>
    /// <param name="parameters">The component parameters.</param>
    /// <param name="check">The rendered interaction checks.</param>
    /// <returns>The complete rendering and verification task.</returns>
    private static async Task RenderAsync<T>(Dictionary<string, object?> parameters, Func<CapturingActivator, Func<string>, Task> check) where T : IComponent
    {
        string directory = Path.Combine(Path.GetTempPath(), _fixture, Guid.NewGuid().ToString());
        TrackerKnowledgeOptions storage = new(directory);
        CompletedRunArchive archive = new(storage);
        archive.Store(CreateRecipe(_firstRun, _firstSlot, 1), false);
        archive.Store(CreateRecipe(_secondRun, _secondSlot, 2), false);
        TrackerConnectionState state = new();
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        TrackerRequestClient requests = new(session, new TrackerConnectionOptions(0, _fixture, false, TimeSpan.FromSeconds(1)), state, new AreaDiscoveryStore(storage));
        CapturingActivator components = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStringLocalizer<TrackerResources>, Localizer>();
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton(archive);
        services.AddSingleton(state);
        services.AddSingleton(requests);
        services.AddSingleton(new AreaDiscoveryStore(storage));
        services.AddSingleton(new DiagnosticAccessService(storage, new(new([])), false));
        services.AddSingleton(new SeedTokenCodec(SeedTokenSharedKey.Material));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SeedTokenFileSaver>();
        try
        {
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
                await check(components, () => System.Net.WebUtility.HtmlDecode(view.ToHtmlString()));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Invokes a production event handler and refreshes the rendered component.
    /// </summary>
    /// <param name="component">The rendered component.</param>
    /// <param name="method">The event handler name.</param>
    /// <param name="arguments">The event arguments.</param>
    /// <returns>The handler and rendering task.</returns>
    private static async Task InvokeAsync(IComponent component, string method, params object[] arguments)
    {
        if (component.GetType().GetMethod(method, _members)!.Invoke(component, arguments) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(_render, _members)!.Invoke(component, null);
    }

    /// <summary>
    /// Hosts the real archive and its application-header section outlet.
    /// </summary>
    private sealed class ArchiveHost : ComponentBase
    {
        /// <summary>
        /// Renders the outlet before the component that supplies its content.
        /// </summary>
        /// <param name="builder">The render tree builder.</param>
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SectionOutlet>(0);
            builder.AddAttribute(1, nameof(SectionOutlet.SectionName), _navigationSection);
            builder.CloseComponent();
            builder.OpenComponent<ArchiveView>(2);
            builder.CloseComponent();
        }
    }

    /// <summary>
    /// Retains real component instances for invoking production interactions.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        private readonly Dictionary<Type, IComponent> _components = [];

        /// <summary>
        /// Gets a rendered component instance.
        /// </summary>
        /// <typeparam name="T">The requested component type.</typeparam>
        /// <returns>The captured component.</returns>
        public T Get<T>() where T : IComponent => (T)_components[typeof(T)];

        /// <summary>
        /// Creates and captures a production component.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The new component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            _components[componentType] = component;
            return component;
        }
    }

    /// <summary>
    /// Resolves production resources without desktop startup.
    /// </summary>
    private sealed class Localizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets a production resource.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets a formatted production resource.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns the unused resource enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent resources should be included.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
