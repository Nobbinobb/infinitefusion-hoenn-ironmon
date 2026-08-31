using Ironmon.Tracker.Access;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Connection.Access;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.RunState;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Lookup;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Renders the production lookup gate across idle, progress, error, completion, and new-run transitions.
/// </summary>
public sealed class LiveLookupPreparationTests
{
    private const string _runId = "lookup-run";
    private const string _secondRunId = "next-lookup-run";
    private const string _version = "0.8.2";
    private const string _rootName = "IronmonLookupUiTests";
    private const string _tabRole = "role=\"tablist\"";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";

    /// <summary>
    /// Verifies only full active preparation opens the actual four-tab markup, without recreating the view.
    /// </summary>
    [Fact]
    public async Task WorldTabsFollowFullActivePreparationAndResetForNewRuns()
    {
        string root = Path.Combine(Path.GetTempPath(), _rootName, Guid.NewGuid().ToString());
        TrackerKnowledgeOptions storage = new(root);
        TrackerConnectionState state = new();
        AreaDiscoveryStore discoveries = new(storage);
        using DiagnosticAccessService access = new(storage, new DiagnosticAccessTokenValidator(new DiagnosticAccessKeyring([])));
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        TrackerRequestClient requests = new(session, new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), state, discoveries);
        GameHandshakePayload game = new(_version, _version, true, false, root, _runId, null);
        state.Publish(TrackerConnectionStatus.Connected, game, new GameCurrentStatePayload(true, _runId, null, 0));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddSingleton(requests);
        services.AddSingleton(discoveries);
        services.AddSingleton(access);
        services.AddSingleton(new TrackerRunState());
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new LookupLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent view = await renderer.RenderComponentAsync<LiveLookup>(ParameterView.Empty);
            Assert.Contains("Calculate lookup data", view.ToHtmlString());
            Assert.DoesNotContain(_tabRole, view.ToHtmlString());

            requests.ObtainabilityProgress.Begin(_runId, TrackerObtainabilityProgressScope.ActiveRun);
            requests.ObtainabilityProgress.Report(_runId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload { Complete = true });
            Assert.DoesNotContain(_tabRole, view.ToHtmlString());
            Assert.Contains("preparing in the background", view.ToHtmlString());

            requests.ObtainabilityProgress.Fail(_runId, TrackerObtainabilityProgressScope.ActiveRun, "Calculation failed.");
            Assert.Contains("Retry calculation", view.ToHtmlString());
            Assert.DoesNotContain(_tabRole, view.ToHtmlString());

            requests.ObtainabilityProgress.Begin(_runId, TrackerObtainabilityProgressScope.ArchivedRun);
            requests.ObtainabilityProgress.Report(_runId, TrackerObtainabilityProgressScope.ArchivedRun, new PokemonObtainabilityResponsePayload { BackgroundComplete = true });
            Assert.DoesNotContain(_tabRole, view.ToHtmlString());

            requests.ObtainabilityProgress.Report(_runId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload { BackgroundComplete = true });
            string prepared = view.ToHtmlString();
            Assert.Contains(_tabRole, prepared);
            Assert.Contains("Trainers", prepared);
            Assert.Contains("Encounters", prepared);
            Assert.Contains("Items", prepared);
            Assert.Contains("Type Coverage", prepared);
            Assert.DoesNotContain("Calculate lookup data", prepared);

            state.Publish(TrackerConnectionStatus.Connected, game, new GameCurrentStatePayload(true, _secondRunId, null, 1));
            Assert.Contains("Calculate lookup data", view.ToHtmlString());
            Assert.DoesNotContain(_tabRole, view.ToHtmlString());
        });
    }

    /// <summary>
    /// Reads the production resource set without depending on the desktop assembly's startup services.
    /// </summary>
    private sealed class LookupLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets production text for one resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets formatted production text for one resource key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>Provides the unused resource enumeration contract.</summary>
        /// <param name="includeParentCultures">Whether parent cultures should be included.</param>
        /// <returns>An empty sequence because rendering requests keys directly.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
