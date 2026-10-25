using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Resources;
using Ironmon.Tracker.App.Components.Settings;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Settings;
using Ironmon.SpriteLibrary;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Exercises production sprite settings handlers and markup through review, suppression, cancellation, and recovery.
/// </summary>
[Collection(nameof(SpriteSettingsCollection))]
public sealed class SpriteDownloadSettingsTests
{
    private const string TestRootName = "IronmonSpriteSettingsTests";
    private const string ExecutableName = "InfiniteFusion2.exe";
    private const string ManifestPath = "Data/sprites/CUSTOM_SPRITES";
    private const string BaseManifestPath = "Data/sprites/BASE_SPRITES";
    private const string ManifestContents = "1.2.png\n1.2a.png\n";
    private const string ResourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string Version = "0.8.5";
    private const string ReviewAction = "ReviewSpriteInstallAsync";
    private const string RecheckAction = "ReviewUnavailableSpritesAsync";
    private const string StartAction = "StartSpriteInstallAsync";
    private const string CancelReviewAction = "CancelSpriteReview";
    private const string RenderAction = "StateHasChanged";
    private const string SettingsPreview = "settings";
    private const string SettingsTabField = "_settingsTab";
    private const string SpeedTab = "Speed";
    private const string GeneralTab = "General";
    private const string SpeedPreview = "speed-tab";
    private const string FeaturesTab = "Features";
    private const string FeaturesPreview = "features-tab";
    private const string FeaturesSavedPreview = "features-saved";
    private const string SavedStatusElement = "class=\"obsidian-settings-saved\" role=\"status\" aria-live=\"polite\"";
    private const string FeatureHandler = "HandleFeaturesChanged";
    private const string BstHandler = "HandleMaximumBstChanged";
    private const string ZeroBstValue = "0";

    /// <summary>
    /// Verifies missing-server resources are never described as installed and the recheck action can recover them.
    /// </summary>
    /// <returns>A task representing sprite-settings rendering and recovery assertions.</returns>
    [Fact]
    public async Task SettingsDistinguishUnavailableSheetsAndAllowAnExplicitRecheck()
    {
        string root = Path.Combine(Path.GetTempPath(), TestRootName, Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, ManifestPath))!);
        File.WriteAllText(Path.Combine(root, ExecutableName), string.Empty);
        File.WriteAllText(Path.Combine(root, ManifestPath), ManifestContents);
        File.WriteAllText(Path.Combine(root, BaseManifestPath), string.Empty);
        FieldInfo preferenceImplementation = typeof(Preferences).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(field => typeof(IPreferences).IsAssignableFrom(field.FieldType));
        object? originalPreferences = preferenceImplementation.GetValue(null);
        preferenceImplementation.SetValue(null, new DefaultPreferences());
        try
        {
            TrackerKnowledgeOptions storage = new(root);
            TrackerConnectionState state = new();
            GameHandshakePayload game = new(Version, Version, true, false, root, null, null);
            state.Publish(TrackerConnectionStatus.Waiting, game, null);
            using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
            TrackerRequestClient requests = new(session, new TrackerConnectionOptions(0, Version, false, TimeSpan.FromSeconds(2)), state, new AreaDiscoveryStore(storage));
            RecoverableResponseHandler handler = new();
            using HttpClient client = new(handler);
            using CustomSpriteSheetInstaller installer = new(client, static _ => false);
            CapturingActivator activator = new();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<Microsoft.JSInterop.IJSRuntime, SettingsJsRuntime>();
            services.AddSingleton(state);
            services.AddSingleton(new TrackerConnectionOptions(0, Version, false, TimeSpan.FromSeconds(2)));
            services.AddSingleton(requests);
            services.AddSingleton(installer);
            services.AddSingleton(new FavoritePokemonStore(storage));
            services.AddSingleton(new EvolutionGraphSettings());
            services.AddSingleton<IComponentActivator>(activator);
            services.AddSingleton<IStringLocalizer<TrackerResources>>(new SettingsLocalizer());
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<TrackerSettingsPage>(ParameterView.Empty);
                string settingsHtml = view.ToHtmlString();
                Assert.DoesNotContain("Game session", settingsHtml);
                Assert.DoesNotContain("Start tracker minimized", settingsHtml);
                Assert.DoesNotContain("Automatic reset after a loss", settingsHtml);
                Assert.DoesNotContain("Autoselect starter", settingsHtml);
                Assert.DoesNotContain("aria-label=\"Startup\"", settingsHtml);
                Assert.DoesNotContain("aria-label=\"Ironmon\"", settingsHtml);
                Assert.DoesNotContain("aria-label=\"Favorite Clause\"", settingsHtml);
                Assert.True(settingsHtml.IndexOf("Starter selection", StringComparison.Ordinal) < settingsHtml.IndexOf("Favorite Clause", StringComparison.Ordinal));
                Assert.True(settingsHtml.IndexOf("Favorite Clause", StringComparison.Ordinal) < settingsHtml.IndexOf("Evolution graph", StringComparison.Ordinal));
                RunManagementViewTests.ExportPreview(SettingsPreview, settingsHtml);
                Assert.DoesNotContain("toggle-speed-setting\"", settingsHtml);
                FieldInfo tab = typeof(TrackerSettingsPage).GetField(SettingsTabField, BindingFlags.NonPublic | BindingFlags.Instance)!;
                tab.SetValue(activator.Page, Enum.Parse(tab.FieldType, FeaturesTab));
                await ((IHandleEvent)activator.Page!).HandleEventAsync(new EventCallbackWorkItem((Action)(() => { })), null);
                string featuresHtml = view.ToHtmlString();
                Assert.Contains(SavedStatusElement, featuresHtml);
                Assert.Contains("Achievement pop-ups", featuresHtml);
                Assert.Contains("Repel step counter", featuresHtml);
                Assert.Contains("Speed indicator", featuresHtml);
                Assert.Contains("Item quick select", featuresHtml);
                Assert.Contains("Quick run hotkey", featuresHtml);
                Assert.Contains("Favorite Clause", featuresHtml);
                Assert.Contains("Enforce BST limit", featuresHtml);
                Assert.Contains("Autoselect starter", featuresHtml);
                Assert.Contains("Automatic reset after a loss", featuresHtml);
                Assert.Contains("Start tracker minimized", featuresHtml);
                Assert.False(TrackerStartupSettings.StartMinimized);
                Assert.True(TrackerStartupSettings.AutomaticReset);
                Assert.Equal(new TrackerFeatureSettings(), TrackerFeaturePreferences.Load());
                RunManagementViewTests.ExportPreview(FeaturesPreview, featuresHtml);
                TrackerFeatureSettings disabled = new() { AchievementPopups = false, RepelCounter = false, SpeedIndicator = false, BattleQuickItems = false, QuickRun = false, FavoriteClause = false, EnforceBstLimit = false };
                Task change = (Task)typeof(TrackerSettingsPage).GetMethod(FeatureHandler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, [disabled])!;
                await change;
                typeof(ComponentBase).GetMethod(RenderAction, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, null);
                string savedHtml = view.ToHtmlString();
                Assert.Contains(SavedStatusElement, savedHtml);
                RunManagementViewTests.ExportPreview(FeaturesSavedPreview, savedHtml);
                Assert.Equal(disabled, TrackerFeaturePreferences.Load());
                Assert.Equal(disabled, provider.GetRequiredService<TrackerConnectionOptions>().Features);
                TrackerFeaturePreferences.Save(new());
                Assert.Equal(new TrackerFeatureSettings(), TrackerFeaturePreferences.Load());
                int? maximum = 400;
                await activator.Page!.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(TrackerSettingsPage.MaximumStarterBaseStatTotalChanged)] = EventCallback.Factory.Create<int?>(activator.Page, value => maximum = value) }));
                await (Task)typeof(TrackerSettingsPage).GetMethod(BstHandler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, [new ChangeEventArgs { Value = ZeroBstValue }])!;
                Assert.Null(maximum);
                tab.SetValue(activator.Page, Enum.Parse(tab.FieldType, SpeedTab));
                await ((IHandleEvent)activator.Page!).HandleEventAsync(new EventCallbackWorkItem((Action)(() => { })), null);
                string speedHtml = view.ToHtmlString();
                Assert.DoesNotContain("Game session", speedHtml);
                Assert.Contains("toggle-speed-settings", speedHtml);
                Assert.DoesNotContain("role=\"dialog\"", speedHtml);
                Assert.DoesNotContain("hidden class=\"toggle-speed-tab\"", speedHtml);
                RunManagementViewTests.ExportPreview(SpeedPreview, speedHtml);
                tab.SetValue(activator.Page, Enum.Parse(tab.FieldType, GeneralTab));
                await ((IHandleEvent)activator.Page!).HandleEventAsync(new EventCallbackWorkItem((Action)(() => { })), null);
                Assert.Contains("hidden class=\"toggle-speed-tab\"", view.ToHtmlString());

                Assert.Contains("Recheck unavailable files", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, ReviewAction);
                Assert.Contains("Check 2 sheets for updates? 0 of 2 sheets are already installed.", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, StartAction);
                Assert.Contains("2 files returned 404", view.ToHtmlString());

                await InvokeActionAsync(activator.Page!, ReviewAction);
                Assert.Contains("0 sheets are installed. The remaining 2 files returned 404", view.ToHtmlString());
                Assert.DoesNotContain("All 2 custom sprite sheets", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, RecheckAction);
                Assert.Contains("Check 2 sheets for updates? 0 of 2 sheets are already installed.", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, CancelReviewAction);
                Assert.Equal(2, installer.CreatePlan(root).UnavailableSheetCount);

                handler.Available = true;
                await InvokeActionAsync(activator.Page!, RecheckAction);
                await InvokeActionAsync(activator.Page!, StartAction);
                Assert.Contains("Updated 2 custom sprite sheets", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, ReviewAction);
                Assert.Contains("Check 2 sheets for updates? 2 of 2 sheets are already installed.", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, StartAction);
                Assert.Contains("All 2 checked custom sprite sheets are up to date.", view.ToHtmlString());
            });
        }
        finally
        {
            preferenceImplementation.SetValue(null, originalPreferences);
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Invokes a production button handler and flushes its resulting component render.
    /// </summary>
    /// <param name="page">The actual settings component created by the renderer.</param>
    /// <param name="methodName">The button handler to invoke.</param>
    /// <returns>A task representing the handler and updated render.</returns>
    private static async Task InvokeActionAsync(TrackerSettingsPage page, string methodName)
    {
        MethodInfo method = typeof(TrackerSettingsPage).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (method.Invoke(page, null) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(RenderAction, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
    }

    /// <summary>
    /// Captures the production component so its existing handlers can be exercised.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the rendered settings component.
        /// </summary>
        internal TrackerSettingsPage? Page { get; private set; }

        /// <summary>
        /// Creates a component and retains the settings instance.
        /// </summary>
        /// <param name="componentType">The renderer-requested component type.</param>
        /// <returns>The newly constructed component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is TrackerSettingsPage page)
                Page = page;

            return component;
        }
    }

    /// <summary>
    /// Returns 404s until an explicit test recovery makes PNG content available.
    /// </summary>
    private sealed class RecoverableResponseHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets or sets whether the remote resources have recovered.
        /// </summary>
        internal bool Available { get; set; }

        /// <summary>
        /// Returns a controlled server result for each request.
        /// </summary>
        /// <param name="request">The outgoing resource request.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>A completed task containing the controlled server response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Available)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            HttpResponseMessage response = request.Headers.IfNoneMatch.Count > 0
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]) };

            response.Headers.ETag = new EntityTagHeaderValue("\"settings-test\"");
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Loads the real settings text without desktop startup services.
    /// </summary>
    private sealed class SettingsLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(ResourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets a production text resource.
        /// </summary>
        /// <param name="name">The localization resource key.</param>
        /// <returns>The localized value, or the resource key when the value is missing.</returns>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets a formatted production text resource.
        /// </summary>
        /// <param name="name">The localization resource key.</param>
        /// <param name="arguments">The arguments substituted into the resource value.</param>
        /// <returns>The formatted resource value.</returns>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Provides the unused enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent resources should be included.</param>
        /// <returns>An empty sequence because tests resolve individual keys.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>
    /// Retains isolated preferences without reading or changing desktop user preferences.
    /// </summary>
    private sealed class DefaultPreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = [];

        /// <summary>
        /// Reports whether an isolated preference was stored.
        /// </summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The preference group.</param>
        /// <returns>Whether the test contains the key.</returns>
        public bool ContainsKey(string key, string? sharedName = null)
            => _values.ContainsKey(key);

        /// <summary>
        /// Removes isolated test preferences.
        /// </summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The preference group.</param>
        public void Remove(string key, string? sharedName = null)
            => _values.Remove(key);

        /// <summary>
        /// Removes isolated test preferences.
        /// </summary>
        /// <param name="sharedName">The preference group.</param>
        public void Clear(string? sharedName = null)
            => _values.Clear();

        /// <summary>
        /// Retains one isolated preference.
        /// </summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="value">The unused preference value.</param>
        /// <param name="sharedName">The preference group.</param>
        public void Set<T>(string key, T value, string? sharedName = null)
            => _values[key] = value;

        /// <summary>
        /// Reads the isolated value without accessing desktop storage.
        /// </summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="defaultValue">The normal application default.</param>
        /// <param name="sharedName">The preference group.</param>
        /// <returns>The saved value or supplied default.</returns>
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
            => _values.TryGetValue(key, out object? value) && value is T saved ? saved : defaultValue;
    }
}

/// <summary>
/// Isolates tests that replace the platform's process-wide preferences dependency.
/// </summary>
[CollectionDefinition(nameof(SpriteSettingsCollection), DisableParallelization = true)]
public sealed class SpriteSettingsCollection { }
