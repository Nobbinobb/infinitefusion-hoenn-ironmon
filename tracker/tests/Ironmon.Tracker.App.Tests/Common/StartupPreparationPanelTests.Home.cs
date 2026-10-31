using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Navigation;
using Ironmon.Tracker.App.Components.Pages;
using Ironmon.Tracker.App.Controls;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.RunState;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Live;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Common;

/// <summary>
/// Verifies startup takes over the tracker without unmounting or moving its underlying page.
/// </summary>
public sealed partial class StartupPreparationPanelTests
{
    private const string _homeAddress = "http://localhost/";
    private const string _version = "0.10.0";
    private const string _selectView = "SelectView";
    private const string _toggleSettings = "ToggleSettings";
    private const string _toggleSeeds = "ToggleSeeds";
    private const string _toggleAccess = "ToggleAccess";
    private const string _togglePlay = "TogglePlay";
    private const string _toggleAchievements = "ToggleAchievements";
    private const string _handleKeyDown = "HandleKeyDown";
    private const string _handleGlobalView = "HandleGlobalViewRequested";
    private const string _handleGlobalEnemyCycle = "HandleGlobalEnemyCycleRequested";
    private const string _dismissFailure = "DismissFailure";
    private const string _archiveKey = "4";
    private const string _archiveCode = "Digit4";
    private const string _selectedView = "_selectedView";
    private const string _selectedEnemyId = "_selectedEnemyId";
    private const string _firstEnemyId = "takeover-enemy-one";
    private const string _secondEnemyId = "takeover-enemy-two";
    private const string _enemySpeciesId = "PIKACHU:0";
    private const string _settingsOpen = "_settingsOpen";
    private const string _render = "StateHasChanged";
    private const string _mainStart = "<main";
    private const string _mainEnd = "</main>";
    private const string _tagEnd = ">";
    private const string _inertPattern = @"\sinert(?:\s|=|>)";
    private const string _hiddenAttribute = "aria-hidden=\"true\"";
    private const string _headerClass = "tracker-header";
    private const string _tablistRole = "role=\"tablist\"";
    private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Keeps the existing page mounted and inert while both pointer and keyboard navigation are blocked by takeover.
    /// </summary>
    /// <param name="failed">Whether the takeover is displaying a preparation failure.</param>
    /// <returns>Completion after rendering production Home navigation without native app or game control.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TakeoverKeepsUnderlayMountedAndGuardsNavigation(bool failed)
    {
        await WithHomeAsync((state, activator, markup) =>
        {
            SuppressedChild underlay = activator.Underlay!;
            StartupPreparationPanel original = activator.Panel!;
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            if (failed)
                Observe(state, _firstPreparation, StartupPreparationProgressPayload.FailedStatus, _loadingPhase);

            AssertTakeover(markup());
            string frozenUnderlay = MainMarkup(markup());
            foreach (TrackerView view in Enum.GetValues<TrackerView>())
            {
                Navigate(activator.Home!, _selectView, view);
                Assert.Equal(frozenUnderlay, MainMarkup(markup()));
            }

            foreach (string tool in new[] { _toggleSettings, _toggleSeeds, _toggleAccess, _togglePlay, _toggleAchievements })
            {
                Navigate(activator.Home!, tool);
                Assert.Equal(frozenUnderlay, MainMarkup(markup()));
            }

            Navigate(activator.Home!, _handleKeyDown, new KeyboardEventArgs { Key = _archiveKey, Code = _archiveCode });
            Navigate(activator.Home!, _handleGlobalView, TrackerView.Archive);
            Navigate(activator.Home!, _handleGlobalEnemyCycle);
            Assert.Equal(frozenUnderlay, MainMarkup(markup()));
            Assert.Equal(_firstEnemyId, typeof(Home).GetField(_selectedEnemyId, _members)!.GetValue(activator.Home));
            Assert.Same(underlay, activator.Underlay);
            Assert.False(underlay.IsDisposed);
            Assert.Same(original, activator.Panel);
        }, withEnemies: true);
    }

    /// <summary>
    /// Restores the original mounted page and normal tab/tool navigation when preparation completes or disconnects.
    /// </summary>
    /// <param name="disconnect">Whether the connection ends rather than completing preparation.</param>
    /// <returns>Completion after checking takeover dismissal and restored interaction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionAndDisconnectRestoreMountedUnderlay(bool disconnect)
    {
        await WithHomeAsync((state, activator, markup) =>
        {
            SuppressedChild underlay = activator.Underlay!;
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            AssertTakeover(markup());
            if (disconnect)
                state.Publish(TrackerConnectionStatus.Waiting);
            else
                Observe(state, _firstPreparation, StartupPreparationProgressPayload.CompleteStatus, _loadingPhase);

            AssertRestoredUnderlay(markup());
            Assert.Same(underlay, activator.Underlay);
            Assert.False(underlay.IsDisposed);
            Navigate(activator.Home!, _selectView, TrackerView.Lookup);
            Assert.Equal(TrackerView.Lookup, (TrackerView)typeof(Home).GetField(_selectedView, _members)!.GetValue(activator.Home)!);
            Navigate(activator.Home!, _toggleSettings);
            Assert.True((bool)typeof(Home).GetField(_settingsOpen, _members)!.GetValue(activator.Home)!);
        });
    }

    /// <summary>
    /// Keeps failure modal until disconnect without removing the page that will be restored.
    /// </summary>
    /// <returns>Completion after a running, failed and disconnected lifecycle.</returns>
    [Fact]
    public async Task FailureTakeoverRestoresUnderlayOnDisconnect()
    {
        await WithHomeAsync((state, activator, markup) =>
        {
            SuppressedChild underlay = activator.Underlay!;
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.FailedStatus, _loadingPhase);
            AssertTakeover(markup());
            Assert.Contains("Preparation could not finish", markup());
            state.Publish(TrackerConnectionStatus.Waiting);
            AssertRestoredUnderlay(markup());
            Assert.Same(underlay, activator.Underlay);
            Assert.False(underlay.IsDisposed);
        });
    }

    /// <summary>
    /// Exercises the actual failure action and restores the same page without allowing late packets to reopen takeover.
    /// </summary>
    /// <returns>Completion after clicking the production failure handler and checking restored interaction.</returns>
    [Fact]
    public async Task FailureBackActionRestoresUnderlayAndRetiresTakeover()
    {
        await WithHomeAsync((state, activator, markup) =>
        {
            SuppressedChild underlay = activator.Underlay!;
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.FailedStatus, _loadingPhase);
            AssertTakeover(markup());
            Assert.Contains("Back to tracker", markup());
            Navigate(activator.Panel!, _dismissFailure);
            AssertRestoredUnderlay(markup());
            Assert.Same(underlay, activator.Underlay);
            Assert.False(underlay.IsDisposed);
            Assert.False(state.StartupPreparation.Observe(new() { PreparationId = _firstPreparation, Status = StartupPreparationProgressPayload.RunningStatus, Phase = _worldPhase }));
            AssertRestoredUnderlay(markup());
        });
    }

    /// <summary>
    /// Checks that the waiting dialog takes over outside the retained shell rather than adding a flow banner.
    /// </summary>
    /// <param name="markup">The complete production shell markup.</param>
    private static void AssertTakeover(string markup)
    {
        string main = MainMarkup(markup);
        string opening = main[..(main.IndexOf(_tagEnd, StringComparison.Ordinal) + 1)];
        Assert.Matches(_inertPattern, opening);
        Assert.Contains(_hiddenAttribute, opening);
        Assert.Contains(_headerClass, main);
        Assert.Contains(_tablistRole, main);
        Assert.DoesNotContain(_panelClass, main);
        Assert.Contains(_dialogStart, markup);
        Assert.Contains(_dialogRole, markup);
        Assert.Contains(_modalAttribute, markup);
    }

    /// <summary>
    /// Checks that modal and interaction-blocking attributes are removed together.
    /// </summary>
    /// <param name="markup">The complete production shell markup.</param>
    private static void AssertRestoredUnderlay(string markup)
    {
        string main = MainMarkup(markup);
        string opening = main[..(main.IndexOf(_tagEnd, StringComparison.Ordinal) + 1)];
        Assert.DoesNotMatch(_inertPattern, opening);
        Assert.DoesNotContain(_hiddenAttribute, opening);
        AssertWaitingHidden(markup);
        Assert.Contains(_headerClass, main);
        Assert.Contains(_tablistRole, main);
    }

    /// <summary>
    /// Extracts only the retained tracker shell, excluding its separate loading takeover.
    /// </summary>
    /// <param name="markup">The complete rendered output.</param>
    /// <returns>The tracker main element and its original child content.</returns>
    private static string MainMarkup(string markup)
    {
        int start = markup.IndexOf(_mainStart, StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = markup.IndexOf(_mainEnd, start, StringComparison.Ordinal) + _mainEnd.Length;
        Assert.True(end > start);
        return markup[start..end];
    }

    /// <summary>
    /// Renders production Home and its loading takeover while keeping unrelated game data loaders inactive.
    /// </summary>
    /// <param name="check">The state transitions and shell assertions to run on the renderer dispatcher.</param>
    /// <param name="withEnemies">Whether two opposing battlers should expose a real global enemy-cycle action.</param>
    /// <returns>Completion after rendering and subscription cleanup.</returns>
    private static async Task WithHomeAsync(Action<TrackerConnectionState, HomeActivator, Func<string>> check, bool withEnemies = false)
    {
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _firstPreparation, Guid.NewGuid().ToString()));
        TrackerConnectionState state = new();
        TrackerRunState runs = new();
        if (withEnemies)
        {
            runs.UpdateEnemy(new EnemyPokemonSnapshot { EnemyId = _firstEnemyId, Position = 1, SpeciesId = _enemySpeciesId, SpeciesName = "First opponent" }, true);
            runs.UpdateEnemy(new EnemyPokemonSnapshot { EnemyId = _secondEnemyId, Position = 3, SpeciesId = _enemySpeciesId, SpeciesName = "Second opponent" }, true);
        }

        CompletedRunArchive archive = new(storage);
        TrackerConnectionOptions options = new(0, _version, false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService connection = new(options, new TrackerDiagnosticsStore(), state, runs, new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), archive);
        HomeActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<NavigationManager, HomeNavigation>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, StartupLocalizer>();
        services.AddSingleton(state);
        services.AddSingleton(runs);
        services.AddSingleton(archive);
        services.AddSingleton(options);
        services.AddSingleton(connection);
        services.AddSingleton(new TrackerGlobalShortcutService(state, connection, new ControlProfiles(() => null, _ => { })));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var shell = await renderer.RenderComponentAsync<Home>(ParameterView.Empty);
            check(state, activator, shell.ToHtmlString);
        });
    }

    /// <summary>
    /// Invokes the normal shell navigation handler and performs the renderer's usual event rerender.
    /// </summary>
    /// <param name="component">The production shell or failure dialog owning the interaction.</param>
    /// <param name="method">The actual navigation method.</param>
    /// <param name="arguments">The navigation arguments.</param>
    private static void Navigate(ComponentBase component, string method, params object[] arguments)
    {
        component.GetType().GetMethod(method, _members)!.Invoke(component, arguments);
        typeof(ComponentBase).GetMethod(_render, _members)!.Invoke(component, null);
    }

    /// <summary>
    /// Retains real shell and loading presentation while suppressing unrelated leaf data and desktop services.
    /// </summary>
    private sealed class HomeActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the production shell receiving navigation events.
        /// </summary>
        internal Home? Home { get; private set; }

        /// <summary>
        /// Gets the actual panel, allowing its identity to be checked across navigation.
        /// </summary>
        internal StartupPreparationPanel? Panel { get; private set; }

        /// <summary>
        /// Gets the initial active-page instance to verify it remains mounted during takeover.
        /// </summary>
        internal SuppressedChild? Underlay { get; private set; }

        /// <summary>
        /// Creates the real shell, loading panel and icons while suppressing unrelated child views.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The requested production coordinator or an inert leaf view.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            if (componentType == typeof(Home))
            {
                Home = new Home();
                return Home;
            }

            if (componentType == typeof(StartupPreparationPanel))
            {
                Panel = new StartupPreparationPanel();
                return Panel;
            }

            if (componentType == typeof(ObsidianIcon))
                return new ObsidianIcon();

            SuppressedChild child = new();
            if ((componentType == typeof(Components.Player.PlayerCard)
                || componentType == typeof(Components.Enemy.EnemyCard)) && Underlay is null)
                Underlay = child;

            return child;
        }
    }

    /// <summary>
    /// Keeps unrelated page data loading outside this shell presentation regression.
    /// </summary>
    private sealed class SuppressedChild : IComponent, IDisposable
    {
        /// <summary>
        /// Gets whether the original page was removed from the renderer.
        /// </summary>
        internal bool IsDisposed { get; private set; }

        /// <summary>
        /// Accepts the renderer handle without producing unrelated child UI.
        /// </summary>
        /// <param name="renderHandle">The renderer-owned component handle.</param>
        public void Attach(RenderHandle renderHandle)
        {
        }

        /// <summary>
        /// Accepts leaf inputs without accessing desktop services or the game.
        /// </summary>
        /// <param name="parameters">The original view inputs.</param>
        /// <returns>A completed presentation task.</returns>
        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;

        /// <summary>
        /// Records an actual page unmount without accessing any external resource.
        /// </summary>
        public void Dispose()
            => IsDisposed = true;
    }

    /// <summary>
    /// Supplies the local address used by the actual shell during initialization.
    /// </summary>
    private sealed class HomeNavigation : NavigationManager
    {
        /// <summary>
        /// Initializes a local-only tracker root address.
        /// </summary>
        public HomeNavigation()
            => Initialize(_homeAddress, _homeAddress);

        /// <summary>
        /// Rejects navigation outside the rendered tracker fixture.
        /// </summary>
        /// <param name="uri">The unused target address.</param>
        /// <param name="forceLoad">Whether a browser navigation was requested.</param>
        protected override void NavigateToCore(string uri, bool forceLoad)
            => throw new NotSupportedException("This fixture does not navigate outside its rendered shell.");
    }
}
