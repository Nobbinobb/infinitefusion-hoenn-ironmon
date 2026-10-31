using Ironmon.Tracker.App.Components.Lookup;
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
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Exercises real shell navigation and Archive payload priority across automatic run-reset event ordering.
/// </summary>
public sealed partial class LookupPreparationPriorityTests
{
    private const string _nextActiveRunId = "next-priority-run";
    private const string _serviceSession = "_requests";
    private const string _selectView = "SelectView";
    private const string _selectedView = "_selectedView";
    private const string _homeRender = "StateHasChanged";
    private const string _homeAddress = "http://localhost/";

    /// <summary>
    /// Verifies completion, a new run's connection snapshot and starter events never promote automatic Archive preparation.
    /// </summary>
    /// <param name="starterBeforeConnection">Whether a starter event arrives before the new run's connection snapshot as well.</param>
    /// <returns>Completion after checking real serialized background polls and an explicit Archive click's foreground poll.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HomeResetSnapshotsKeepAutomaticArchiveInBackground(bool starterBeforeConnection)
    {
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _runId, Guid.NewGuid().ToString()));
        TrackerConnectionState state = new();
        TrackerRunState runs = new();
        CompletedRunArchive archive = new(storage);
        TrackerConnectionOptions options = new(0, _version, false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService connection = new(options, new TrackerDiagnosticsStore(), state, runs, new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), archive);
        TrackerRequestSession session = (TrackerRequestSession)typeof(TrackerConnectionService).GetField(_serviceSession, _members)!.GetValue(connection)!;
        using PriorityStream stream = new();
        session.Connect(new TrackerMessageWriter(stream));
        state.Publish(TrackerConnectionStatus.Connected, currentState: new GameCurrentStatePayload(true, _runId, null, 0));
        HomeActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<NavigationManager, HomeNavigation>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, PriorityLocalizer>();
        services.AddSingleton(state);
        services.AddSingleton(runs);
        services.AddSingleton(archive);
        services.AddSingleton(options);
        services.AddSingleton(connection);
        services.AddSingleton(connection.Requests);
        services.AddSingleton(new TrackerGlobalShortcutService(state, connection, new ControlProfiles(() => null, _ => { })));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<Home>(ParameterView.Empty);
            archive.Store(CreateRecipe());
            TrackerMessage completedRun = await stream.ReadRequestAsync();
            Assert.False(Payload(completedRun).Foreground);

            if (starterBeforeConnection)
                runs.UpdateStarterSelection(new StarterSelectionSnapshot { Active = true });

            state.Publish(TrackerConnectionStatus.Connected, currentState: new GameCurrentStatePayload(true, _nextActiveRunId, null, 1));
            Assert.True(session.TryComplete(TrackerMessageFactory.CreateResponse(completedRun.RequestId!, new Ironmon.Tracker.Protocol.Lookup.PokemonObtainabilityResponsePayload { Phase = _phase }, _runId)));
            TrackerMessage resetPoll = await stream.ReadRequestAsync();
            Assert.False(Payload(resetPoll).Foreground);
            Assert.Equal(_runId, Payload(resetPoll).Recipe.RunId);

            runs.UpdateStarterSelection(new StarterSelectionSnapshot { Active = true });
            TrackerView selected = (TrackerView)typeof(Home).GetField(_selectedView, _members)!.GetValue(activator.Home)!;
            Assert.Equal(starterBeforeConnection ? TrackerView.Archive : TrackerView.Player, selected);

            Invoke(activator.Home!, _selectView, TrackerView.Archive);
            typeof(ComponentBase).GetMethod(_homeRender, _members)!.Invoke(activator.Home, null);
            session.TryComplete(TrackerMessageFactory.CreateResponse(resetPoll.RequestId!, new Ironmon.Tracker.Protocol.Lookup.PokemonObtainabilityResponsePayload { Phase = _phase }, _runId));
            Assert.True(Payload(await stream.ReadRequestAsync()).Foreground);
        });
    }

    /// <summary>
    /// Renders the actual shell and Archive coordinator while suppressing unrelated leaf data and desktop services.
    /// </summary>
    private sealed class HomeActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the actual production shell receiving archive, connection and starter events.
        /// </summary>
        internal Home? Home { get; private set; }

        /// <summary>
        /// Creates the production coordinators and suppresses unrelated child presentation.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The real coordinator or an inert presentation child.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            if (componentType == typeof(Home))
            {
                Home = new Home();
                return Home;
            }

            return componentType == typeof(ArchiveView) ? new ArchiveView() : new SuppressedHomeChild();
        }
    }

    /// <summary>
    /// Keeps unrelated leaf UI outside this navigation/protocol regression without running native services.
    /// </summary>
    private sealed class SuppressedHomeChild : IComponent
    {
        /// <summary>
        /// Accepts the renderer handle without producing leaf UI.
        /// </summary>
        /// <param name="renderHandle">The renderer-owned component handle.</param>
        public void Attach(RenderHandle renderHandle)
        {
        }

        /// <summary>
        /// Accepts presentation inputs without loading unrelated game data.
        /// </summary>
        /// <param name="parameters">The production leaf component's parameters.</param>
        /// <returns>A completed presentation task.</returns>
        public Task SetParametersAsync(ParameterView parameters)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Supplies only the local root address needed by the real shell's initialization.
    /// </summary>
    private sealed class HomeNavigation : NavigationManager
    {
        /// <summary>
        /// Initializes an isolated tracker root address.
        /// </summary>
        public HomeNavigation()
            => Initialize(_homeAddress, _homeAddress);

        /// <summary>
        /// Rejects external navigation because this fixture only renders the local shell.
        /// </summary>
        /// <param name="uri">The unused requested address.</param>
        /// <param name="forceLoad">Whether a browser navigation was requested.</param>
        protected override void NavigateToCore(string uri, bool forceLoad)
            => throw new NotSupportedException("This fixture does not navigate outside its rendered shell.");
    }
}
