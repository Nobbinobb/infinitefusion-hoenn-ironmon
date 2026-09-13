using Ironmon.Tracker.App.Components.Updates;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Updater.Infrastructure;
using Ironmon.Updater.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Updates;

/// <summary>
/// Exercises the production updater renderer and signed session without changing any real installed tracker.
/// </summary>
public sealed class TrackerUpdatePanelTests
{
    private const string ResourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string ScriptNotes = "<script>alert('release notes')</script>\nA quieter tracker update.";
    private const string KeyHandler = "HandleKeyDown";
    private const string EscapeKey = "Escape";
    private const string LocalModification = "my edited script";
    private const string HomeKey = "home";
    private const string AfterRender = "OnAfterRenderAsync";
    private const string SessionProperty = "Session";
    private const string JavaScriptProperty = "JavaScript";
    private const string NavigationProperty = "Navigation";

    /// <summary>
    /// Prevents overlapping render notifications from attaching two focus traps and stranding the tracker as inert.
    /// </summary>
    [Fact]
    public async Task OverlappingRenderCallbacksAttachAndDetachOnlyOneDialog()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.Session.OpenAsync();
        var component = new TrackerUpdatePanel();
        var javascript = new DeferredDialogRuntime();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TrackerUpdatePanel).GetProperty(SessionProperty, flags)!.SetValue(component, fixture.Session);
        typeof(TrackerUpdatePanel).GetProperty(JavaScriptProperty, flags)!.SetValue(component, javascript);
        typeof(TrackerUpdatePanel).GetProperty(NavigationProperty, flags)!.SetValue(component, new TrackerUpdateNavigation());
        var render = typeof(TrackerUpdatePanel).GetMethod(AfterRender, flags)!;
        var first = (Task)render.Invoke(component, [false])!;
        var second = (Task)render.Invoke(component, [false])!;
        await second;
        fixture.Session.Close();
        javascript.Acknowledged.SetResult(1);
        await first;
        Assert.Equal(1, javascript.Attachments);
        Assert.Equal(1, javascript.Detachments);
        await component.DisposeAsync();
        Assert.Equal(1, javascript.Detachments);
    }

    /// <summary>
    /// Renders the invitation on startup and returns with Escape while retaining the real review session.
    /// </summary>
    [Fact]
    public async Task InvitationOpensEscapedReleaseNotesAndEscapeReturnsToTheOrigin()
    {
        using var fixture = new TrackerUpdateTestFixture();
        fixture.Release.UseNotes(ScriptNotes);
        await fixture.Session.StartAsync();
        await RenderAsync(fixture, async (component, html) =>
        {
            Assert.Contains("Update available", html());
            Assert.Contains("role=\"dialog\"", html());
            Assert.Contains("Later", html());
            Assert.DoesNotContain("tracker-update-footer", html());
            await fixture.Session.OpenAsync();
            Assert.Contains("role=\"dialog\"", html());
            Assert.Contains("&lt;script&gt;", html());
            Assert.DoesNotContain("<script>", html());
            Assert.Contains("Save your game", html());
            Assert.DoesNotContain("Continue", html());
            typeof(TrackerUpdatePanel).GetMethod(KeyHandler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, [new KeyboardEventArgs { Key = EscapeKey }]);
            Assert.False(fixture.Session.IsOpen);
            Assert.DoesNotContain("role=\"dialog\"", html());
        });
    }

    /// <summary>
    /// Displays real exact file consent and switches to cancellable progress after the single Update action.
    /// </summary>
    [Fact]
    public async Task ReviewedConflictAndDownloadUseRealSessionTransitions()
    {
        using var fixture = new TrackerUpdateTestFixture { ArchiveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await File.WriteAllTextAsync(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script), LocalModification);
        await fixture.Session.OpenAsync();
        await RenderAsync(fixture, async (_, html) =>
        {
            Assert.Contains(SignedReleaseFixture.Script, html());
            Assert.Contains("type=\"checkbox\"", html());
            Assert.False(fixture.Session.CanUpdate);
            fixture.Session.Approve(SignedReleaseFixture.Script, true);
            Assert.True(fixture.Session.CanUpdate);
            var update = fixture.Session.UpdateAsync();
            await fixture.ArchiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("<progress", html());
            Assert.Contains("Downloading the Ironmon package", html());
            Assert.Contains("MiB", html());
            Assert.Contains("Cancel", html());
            Assert.DoesNotContain("Continue", html());
            fixture.Session.Cancel();
            await update.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("Cancelled.", html());
            Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        });
    }

    /// <summary>
    /// Keeps offline errors visible and offers checking only inside the dedicated panel.
    /// </summary>
    [Fact]
    public async Task FailedStartupShowsNoDialogUntilSettingsIsSelected()
    {
        using var fixture = new TrackerUpdateTestFixture { Offline = true };
        await fixture.Session.StartAsync();
        await RenderAsync(fixture, async (_, html) =>
        {
            Assert.DoesNotContain("role=\"dialog\"", html());
            await fixture.Session.OpenAsync();
            Assert.Contains("check for updates", html());
            Assert.Contains("Check again", html());
            Assert.DoesNotContain("latest available", html());
        });
    }

    /// <summary>
    /// Shows installed versions and a confirmed current result without a bottom action bar.
    /// </summary>
    [Fact]
    public async Task CurrentReleaseHasVersionRowsAndInlineRetry()
    {
        using var fixture = new TrackerUpdateTestFixture { ObservedVersion = SignedReleaseFixture.VersionB };
        await fixture.Session.OpenFromSettingsAsync();
        await RenderAsync(fixture, (_, html) =>
        {
            Assert.Contains("Back to Settings", html());
            Assert.Contains("tracker-update-result-current", html());
            Assert.Contains("Ironmon tracker", html());
            Assert.Contains("Infinite Fusion", html());
            Assert.Contains(fixture.Release.Manifest.Game.VersionLabel, html());
            Assert.Contains("Last checked", html());
            Assert.DoesNotContain("tracker-update-actions", html());
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Treats Escape on the startup invitation as Later and releases the navigation guard.
    /// </summary>
    [Fact]
    public async Task EscapeDismissesTheInvitationForThisLaunch()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.Session.StartAsync();
        await RenderAsync(fixture, async (component, html) =>
        {
            typeof(TrackerUpdatePanel).GetMethod(KeyHandler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, [new KeyboardEventArgs { Key = EscapeKey }]);
            await fixture.Session.StartAsync();
            Assert.False(fixture.Session.ShowStartupPrompt);
            Assert.DoesNotContain("role=\"dialog\"", html());
        });
    }

    /// <summary>
    /// Round-trips only registered selection data and rejects oversized or malformed relaunch context.
    /// </summary>
    [Fact]
    public void NavigationRoundTripIsBoundedAndConsumedOnce()
    {
        var before = new TrackerUpdateNavigation();
        before.Register(HomeKey, () => 3);
        var json = before.Export(JsonSerializer.SerializeToElement(new { y = 180, items = Array.Empty<int>() }));
        var after = new TrackerUpdateNavigation();
        after.Import(json);
        Assert.Equal(3, after.Take<int>(HomeKey));
        Assert.Equal(0, after.Take<int>(HomeKey));
        Assert.Equal(180, after.TakeScroll().GetProperty("y").GetInt32());
        Assert.Throws<InvalidDataException>(() => after.Import(new string('x', 4097)));
        Assert.Throws<JsonException>(() => after.Import("[1,2]"));
    }

    /// <summary>
    /// Confirms every new resource is resolved by the existing catalog, including a non-English fallback culture.
    /// </summary>
    /// <param name="culture">The display culture.</param>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public async Task UpdateResourcesResolveWithoutShowingKeys(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture;
            using var fixture = new TrackerUpdateTestFixture();
            await fixture.Session.OpenAsync();
            await RenderAsync(fixture, (_, html) =>
            {
                Assert.DoesNotContain("Updates.", html());
                Assert.Contains("Update available", html());
                return Task.CompletedTask;
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            CultureInfo.CurrentCulture = previousFormat;
        }
    }

    /// <summary>
    /// Renders the real component with the signed session and shared localization catalog.
    /// </summary>
    /// <param name="fixture">The isolated signed workflow.</param>
    /// <param name="check">The interactions and rendered-state assertions.</param>
    /// <returns>The complete renderer fixture.</returns>
    private static async Task RenderAsync(TrackerUpdateTestFixture fixture, Func<TrackerUpdatePanel, Func<string>, Task> check)
    {
        var services = new ServiceCollection();
        var activator = new PanelActivator();
        services.AddLogging();
        services.AddSingleton(fixture.Session);
        services.AddSingleton<TrackerUpdateNavigation>();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, UpdateLocalizer>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<TrackerUpdatePanel>();
            await check(activator.Component!, view.ToHtmlString);
        });
    }

    /// <summary>
    /// Captures the production component for keyboard interaction on the render dispatcher.
    /// </summary>
    private sealed class PanelActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the mounted updater component.
        /// </summary>
        internal TrackerUpdatePanel? Component { get; private set; }

        /// <summary>
        /// Creates the renderer-requested component and captures the update panel.
        /// </summary>
        /// <param name="componentType">The requested production component.</param>
        /// <returns>The component instance.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is TrackerUpdatePanel panel)
                Component = panel;

            return component;
        }
    }

    /// <summary>
    /// Holds the browser acknowledgement open while a second render notification reaches the production component.
    /// </summary>
    private sealed class DeferredDialogRuntime : IJSRuntime
    {
        private const string Attach = "ironmonDialog.attach";
        private const string Detach = "ironmonDialog.detach";

        /// <summary>
        /// Gets the controlled first browser acknowledgement.
        /// </summary>
        internal TaskCompletionSource<int> Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets the number of focus traps requested by the real component.
        /// </summary>
        internal int Attachments { get; private set; }

        /// <summary>
        /// Gets the number of focus traps released by the real component.
        /// </summary>
        internal int Detachments { get; private set; }

        /// <summary>
        /// Forwards a browser invocation without a cancellation token to the controlled runtime.
        /// </summary>
        /// <typeparam name="TValue">The browser return type.</typeparam>
        /// <param name="identifier">The invoked browser function.</param>
        /// <param name="args">The browser arguments.</param>
        /// <returns>The controlled browser response.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        /// <summary>
        /// Counts real attachment and cleanup calls while delaying the first acknowledgement.
        /// </summary>
        /// <remarks>
        /// The framework-defined interface fixes the cancellation parameter before its argument array.
        /// </remarks>
        /// <typeparam name="TValue">The browser return type.</typeparam>
        /// <param name="identifier">The invoked browser function.</param>
        /// <param name="cancellationToken">The framework cancellation token.</param>
        /// <param name="args">The framework argument array.</param>
        /// <returns>The controlled browser response.</returns>
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == Attach)
            {
                Attachments++;
                return (TValue)(object)await Acknowledged.Task.WaitAsync(cancellationToken);
            }

            if (identifier == Detach)
                Detachments++;

            return default!;
        }
    }

    /// <summary>
    /// Reads the production resource catalog without starting the native desktop application.
    /// </summary>
    private sealed class UpdateLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(ResourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets a localized value or exposes a missing key to the test assertion.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name, CultureInfo.CurrentUICulture) ?? name);

        /// <summary>
        /// Formats a localized value using the display culture.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(CultureInfo.CurrentCulture, this[name].Value, arguments));

        /// <summary>
        /// Supplies the unused enumeration contract for these explicit key assertions.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent cultures are requested.</param>
        /// <returns>An empty enumeration.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
