using Ironmon.Tracker.Access;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.App.Seeds;
using Ironmon.Tracker.Connection.Access;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Seeds;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Verifies archive navigation reuses completed preparation and keeps history outside run preparation.
/// </summary>
public sealed class ArchivePreparationTests
{
    private const string _runId = "archive-prepared";
    private const string _nextRunId = "archive-early-loss";
    private const string _version = "0.8.6";
    private const string _profileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string _policy = "mixed";
    private const string _rootName = "IronmonArchivePreparationTests";
    private const string _runTabs = "obsidian-lookup-tabs obsidian-archive-tabs";
    private const string _historyContent = "archive-history-content";
    private const string _showHistory = "ShowHistory";
    private const string _selectRun = "SelectRun";
    private const string _render = "StateHasChanged";
    private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Verifies run content stays visible while only unprepared runs start archive preparation.
    /// </summary>
    /// <param name="prepared">Whether full preparation has completed.</param>
    /// <param name="scope">The surface reporting preparation.</param>
    [Theory]
    [InlineData(false, TrackerObtainabilityProgressScope.ActiveRun)]
    [InlineData(true, TrackerObtainabilityProgressScope.ActiveRun)]
    [InlineData(true, TrackerObtainabilityProgressScope.ArchivedRun)]
    public Task InitialNavigationReusesCompletedPreparation(bool prepared, TrackerObtainabilityProgressScope scope)
    {
        return RenderAsync(prepared, scope, (component, markup, archive, requests) =>
        {
            Assert.Contains(_runTabs, markup());
            Assert.Equal(prepared, requests.ObtainabilityProgress.HasCompletedPreparation(_runId));
            if (prepared)
            {
                Assert.Equal(scope, requests.ObtainabilityProgress.Snapshot.Scope);
                Assert.Equal(TrackerObtainabilityProgressStatus.Complete, requests.ObtainabilityProgress.Snapshot.Status);
                Assert.False(requests.ArchiveObtainabilityPrecalculationSelected);
            }
            else
            {
                Assert.Equal(TrackerObtainabilityProgressScope.ArchivedRun, requests.ObtainabilityProgress.Snapshot.Scope);
            }
        });
    }

    /// <summary>
    /// Verifies preparation completion and recipe refresh preserve history until a run is explicitly selected.
    /// </summary>
    [Fact]
    public Task PreparationAndRecipeRefreshRespectHistorySelection()
    {
        return RenderAsync(true, TrackerObtainabilityProgressScope.ActiveRun, (component, markup, archive, requests) =>
        {
            Invoke(component, _showHistory);
            Assert.Contains(_historyContent, markup());
            Assert.DoesNotContain(_runTabs, markup());
            Assert.False(requests.ArchiveObtainabilityPrecalculationSelected);

            requests.ObtainabilityProgress.Begin(_nextRunId, TrackerObtainabilityProgressScope.ActiveRun);
            requests.ObtainabilityProgress.Report(_nextRunId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload { BackgroundComplete = true });
            TrackerObtainabilityProgressSnapshot completed = requests.ObtainabilityProgress.Snapshot;
            archive.Store(CreateRecipe(_runId), false);
            archive.Store(CreateRecipe(_nextRunId), false);
            Assert.Contains(_historyContent, markup());
            Assert.DoesNotContain(_runTabs, markup());
            Assert.Same(completed, requests.ObtainabilityProgress.Snapshot);
            Assert.False(requests.ArchiveObtainabilityPrecalculationSelected);

            Invoke(component, _selectRun, _nextRunId);
            Assert.Contains(_runTabs, markup());
            Assert.DoesNotContain(_historyContent, markup());
            Assert.Same(completed, requests.ObtainabilityProgress.Snapshot);
            Assert.False(requests.ArchiveObtainabilityPrecalculationSelected);
        });
    }

    /// <summary>
    /// Verifies completion navigation opens a newly prepared run without starting preparation again.
    /// </summary>
    [Fact]
    public Task PreparedCompletionSelectionOpensWithoutStartingWork()
    {
        return RenderAsync(true, TrackerObtainabilityProgressScope.ActiveRun, (component, markup, archive, requests) =>
        {
            Invoke(component, _showHistory);
            requests.ObtainabilityProgress.Begin(_nextRunId, TrackerObtainabilityProgressScope.ActiveRun);
            requests.ObtainabilityProgress.Report(_nextRunId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload { BackgroundComplete = true });
            TrackerObtainabilityProgressSnapshot completed = requests.ObtainabilityProgress.Snapshot;
            archive.Store(CreateRecipe(_nextRunId));
            Assert.Contains(_runTabs, markup());
            Assert.DoesNotContain(_historyContent, markup());
            Assert.Same(completed, requests.ObtainabilityProgress.Snapshot);
            Assert.False(requests.ArchiveObtainabilityPrecalculationSelected);
        });
    }

    /// <summary>
    /// Invokes a production navigation handler and renders its updated selection.
    /// </summary>
    /// <param name="component">The rendered archive.</param>
    /// <param name="method">The navigation handler name.</param>
    /// <param name="arguments">The handler arguments.</param>
    private static void Invoke(ArchiveView component, string method, params object[] arguments)
    {
        typeof(ArchiveView).GetMethod(method, _members)!.Invoke(component, arguments);
        typeof(ComponentBase).GetMethod(_render, _members)!.Invoke(component, null);
    }

    /// <summary>
    /// Renders the production archive against synthetic recipes and a connection without a game transport.
    /// </summary>
    /// <param name="prepared">Whether the initial run has full preparation.</param>
    /// <param name="scope">The initial preparation scope.</param>
    /// <param name="check">The lifecycle assertions to run on the rendering dispatcher.</param>
    /// <returns>A task representing rendering and verification.</returns>
    private static async Task RenderAsync(bool prepared, TrackerObtainabilityProgressScope scope, Action<ArchiveView, Func<string>, CompletedRunArchive, TrackerRequestClient> check)
    {
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _rootName, Guid.NewGuid().ToString()));
        CompletedRunArchive archive = new(storage);
        archive.Store(CreateRecipe(_runId));
        TrackerConnectionState state = new();
        state.Publish(TrackerConnectionStatus.Connected, currentState: new GameCurrentStatePayload(false, _runId, null, 0));
        AreaDiscoveryStore discoveries = new(storage);
        using DiagnosticAccessService access = new(storage, new DiagnosticAccessTokenValidator(new DiagnosticAccessKeyring([])));
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        TrackerRequestClient requests = new(session, new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), state, discoveries);
        requests.ObtainabilityProgress.Begin(_runId, scope);
        requests.ObtainabilityProgress.Report(_runId, scope, new PokemonObtainabilityResponsePayload { Complete = true, BackgroundComplete = prepared });
        ArchiveActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, Settings.SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new ArchiveLocalizer());
        services.AddSingleton(archive);
        services.AddSingleton(state);
        services.AddSingleton(requests);
        services.AddSingleton(discoveries);
        services.AddSingleton(access);
        services.AddSingleton(new SeedTokenCodec(SeedTokenSharedKey.Material));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SeedTokenFileSaver>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<ArchiveView>(ParameterView.Empty);
            check(activator.Component!, view.ToHtmlString, archive, requests);
        });
    }

    /// <summary>
    /// Creates valid minimal recipe data without accessing personal saves.
    /// </summary>
    /// <param name="runId">The synthetic completed-run identifier.</param>
    /// <returns>A recipe accepted by the production archive.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId) => new()
    {
        RunId = runId,
        Result = _runId,
        GenerationProfileId = _profileId,
        GameVersion = _version,
        IronmonVersion = _version,
        Configuration = new() { SchemaVersion = 1, WildPolicy = _policy, TrainerPolicy = _policy, UnfusionSetting = _policy },
        SpeciesGenerator = new() { Version = 1, PoolFingerprint = _profileId },
        AbilityGenerator = new() { Version = 1, PoolSize = 1, PoolFingerprint = _profileId },
        PlayerFusionGenerator = new() { Version = 1, PoolSize = 1, PoolFingerprint = _profileId }
    };

    /// <summary>
    /// Captures the archive component while rendering its production children normally.
    /// </summary>
    private sealed class ArchiveActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the rendered archive component.
        /// </summary>
        public ArchiveView? Component { get; private set; }

        /// <summary>
        /// Creates the requested production component.
        /// </summary>
        /// <param name="componentType">The component type requested by the renderer.</param>
        /// <returns>The newly created component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is ArchiveView archive)
                Component = archive;

            return component;
        }
    }

    /// <summary>
    /// Supplies stable labels for archive markup assertions.
    /// </summary>
    private sealed class ArchiveLocalizer : IStringLocalizer<TrackerResources>
    {
        /// <summary>
        /// Gets a stable label for a resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, name);

        /// <summary>
        /// Gets a stable label for a formatted resource key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        /// <summary>
        /// Supplies the unused resource enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent culture resources are requested.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
