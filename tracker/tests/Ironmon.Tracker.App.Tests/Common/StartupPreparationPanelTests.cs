using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Resources;
using System.Text.RegularExpressions;

namespace Ironmon.Tracker.App.Tests.Common;

/// <summary>
/// Verifies the startup waiting indicator follows game stages without inventing numeric progress or keeping a polling clock.
/// </summary>
public sealed partial class StartupPreparationPanelTests
{
    private const string _firstPreparation = "startup-panel-one";
    private const string _secondPreparation = "startup-panel-two";
    private const string _loadingPhase = "loading_save";
    private const string _worldPhase = "world_data";
    private const string _fusionPhase = "strength_pairing";
    private const string _unknownPhase = "future_internal_phase";
    private const string _motionClass = "startup-preparation-motion";
    private const string _panelClass = "startup-preparation";
    private const string _progressRole = "role=\"progressbar\"";
    private const string _numericProgressAttribute = "aria-valuenow";
    private const string _percentageSymbol = "%";
    private const string _alertRole = "role=\"alert\"";
    private const string _dialogRole = "role=\"dialog\"";
    private const string _modalAttribute = "aria-modal=\"true\"";
    private const string _buttonStart = "<button";
    private const string _dialogStart = "<dialog";
    private const string _titleElementId = "id=\"startup-preparation-title\"";
    private const string _headingPattern = @"<h[1-6]\b[^>]*>.*?</h[1-6]>";
    private const string _oldKickerClass = "startup-preparation-kicker";
    private const string _oldHintClass = "startup-preparation-hint";
    private const string _oldIconClass = "startup-preparation-icon";
    private const string _svgStart = "<svg";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";

    /// <summary>
    /// Verifies one large phase heading and the independent waiting bar update without generic headings or filler.
    /// </summary>
    /// <returns>Completion after rendering real state changes without a native game.</returns>
    [Fact]
    public async Task RunningStagesShowOnePhaseHeadingWithIndependentWaitingBar()
    {
        await WithPanelAsync((state, markup) =>
        {
            AssertWaitingHidden(markup());
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            AssertPhaseOnlyWaiting(markup(), "Loading your save");
            Assert.Contains(_dialogRole, markup());
            Assert.Contains(_modalAttribute, markup());
            Assert.Contains(_dialogStart, markup());
            Assert.DoesNotContain(_buttonStart, markup());
            Assert.DoesNotContain(_numericProgressAttribute, markup());
            Assert.DoesNotContain(_percentageSymbol, markup());
            Assert.DoesNotContain(_firstPreparation, markup());

            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _worldPhase);
            AssertPhaseOnlyWaiting(markup(), "Preparing the world");
            Assert.DoesNotContain("Loading your save", markup());

            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _fusionPhase);
            AssertPhaseOnlyWaiting(markup(), "Preparing fusion choices");
        });
    }

    /// <summary>
    /// Verifies unfamiliar game phases retain a useful waiting message without exposing an internal identifier.
    /// </summary>
    /// <returns>Completion after checking fallback presentation.</returns>
    [Fact]
    public async Task UnknownStageKeepsFriendlyWaitingText()
    {
        await WithPanelAsync((state, markup) =>
        {
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _unknownPhase);
            AssertPhaseOnlyWaiting(markup(), "Preparing your game", allowFallbackTitle: true);
            Assert.DoesNotContain(_unknownPhase, markup());
        });
    }

    /// <summary>
    /// Verifies readiness hides waiting UI, while a later failure remains visible and safely encodes game error text.
    /// </summary>
    /// <returns>Completion after checking completion, failure and reset transitions.</returns>
    [Fact]
    public async Task ReadyHidesPanelAndFailureRemainsVisibleUntilReset()
    {
        await WithPanelAsync((state, markup) =>
        {
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.CompleteStatus, _loadingPhase);
            AssertWaitingHidden(markup());

            Observe(state, _secondPreparation, StartupPreparationProgressPayload.RunningStatus, _worldPhase);
            Observe(state, _secondPreparation, StartupPreparationProgressPayload.FailedStatus, _worldPhase, "Could not read <save>.");
            Assert.Contains("Preparation could not finish", markup());
            Assert.Contains(_alertRole, markup());
            Assert.Contains("Could not read &lt;save&gt;.", markup());
            Assert.DoesNotContain(_motionClass, markup());

            state.StartupPreparation.Reset();
            AssertWaitingHidden(markup());
        });
    }

    /// <summary>
    /// Verifies disconnect clears both an active waiting indicator and a retained loading error.
    /// </summary>
    /// <param name="failed">Whether startup has failed before the connection ends.</param>
    /// <returns>Completion after checking the real connection reset notification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectClearsWaitingAndFailure(bool failed)
    {
        await WithPanelAsync((state, markup) =>
        {
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            if (failed)
                Observe(state, _firstPreparation, StartupPreparationProgressPayload.FailedStatus, _loadingPhase);

            Assert.Contains(_panelClass, markup());
            if (failed)
                Assert.Contains("Return to the game for more information.", markup());

            state.Publish(TrackerConnectionStatus.Waiting);
            AssertWaitingHidden(markup());
        });
    }

    /// <summary>
    /// Checks that only the closed native dialog placeholder remains after loading ends.
    /// </summary>
    /// <param name="markup">The rendered loading component or complete tracker shell.</param>
    private static void AssertWaitingHidden(string markup)
    {
        Assert.Contains(_dialogStart, markup);
        Assert.DoesNotContain(_motionClass, markup);
        Assert.DoesNotContain(_progressRole, markup);
        Assert.DoesNotContain(_titleElementId, markup);
    }

    /// <summary>
    /// Checks that running takeover contains only its meaningful phase heading and an independently animated waiting bar.
    /// </summary>
    /// <param name="markup">The rendered production dialog.</param>
    /// <param name="phaseText">The friendly phase label expected in its single heading.</param>
    /// <param name="allowFallbackTitle">Whether the generic message is the one legitimate unknown-phase fallback.</param>
    private static void AssertPhaseOnlyWaiting(string markup, string phaseText, bool allowFallbackTitle = false)
    {
        Match heading = Assert.Single(Regex.Matches(markup, _headingPattern, RegexOptions.Singleline).Cast<Match>());
        Assert.Contains(phaseText, heading.Value);
        Assert.Contains(_motionClass, markup);
        Assert.Contains(_progressRole, markup);
        Assert.DoesNotContain(_oldKickerClass, markup);
        Assert.DoesNotContain(_oldHintClass, markup);
        Assert.DoesNotContain(_oldIconClass, markup);
        Assert.DoesNotContain(_svgStart, markup);
        Assert.DoesNotContain("Getting ready", markup);
        Assert.DoesNotContain("The game will continue when preparation finishes.", markup);
        if (!allowFallbackTitle)
            Assert.DoesNotContain("Preparing your game", markup);
    }

    /// <summary>
    /// Publishes a real game preparation observation through the shared connection state.
    /// </summary>
    /// <param name="state">The connection-scoped observation owner.</param>
    /// <param name="preparationId">The unique loading attempt.</param>
    /// <param name="status">The game lifecycle status.</param>
    /// <param name="phase">The game stage identifier.</param>
    /// <param name="error">The optional terminal error.</param>
    private static void Observe(TrackerConnectionState state, string preparationId, string status, string phase, string? error = null)
        => Assert.True(state.StartupPreparation.Observe(new() { PreparationId = preparationId, Status = status, Phase = phase, Error = error }));

    /// <summary>
    /// Renders the production panel against its actual shared state and production localization.
    /// </summary>
    /// <param name="check">The state transitions and markup assertions to run on the renderer dispatcher.</param>
    /// <returns>Completion after the panel and state subscriptions are disposed.</returns>
    private static async Task WithPanelAsync(Action<TrackerConnectionState, Func<string>> check)
    {
        TrackerConnectionState state = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddSingleton<IStringLocalizer<TrackerResources>, StartupLocalizer>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var panel = await renderer.RenderComponentAsync<StartupPreparationPanel>(ParameterView.Empty);
            check(state, panel.ToHtmlString);
        });
    }

    /// <summary>
    /// Reads production resource values without starting desktop UI services.
    /// </summary>
    private sealed class StartupLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the localized value for one resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets the localized value for a formatted resource key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Supplies the unused resource enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent culture resources should be included.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
