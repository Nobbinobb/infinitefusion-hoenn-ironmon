using System.Net;
using System.Reflection;
using System.Resources;
using Ironmon.Tracker.App.Components.Settings;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Settings;
using Ironmon.Tracker.Connection.Sprites;
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
    private const string ManifestContents = "1.2.png\n1.2a.png\n";
    private const string ResourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string Version = "0.8.2";
    private const string ReviewAction = "ReviewSpriteInstallAsync";
    private const string RecheckAction = "ReviewUnavailableSpritesAsync";
    private const string StartAction = "StartSpriteInstallAsync";
    private const string CancelReviewAction = "CancelSpriteReview";
    private const string RenderAction = "StateHasChanged";

    /// <summary>
    /// Verifies missing-server resources are never described as installed and the recheck action can recover them.
    /// </summary>
    [Fact]
    public async Task SettingsDistinguishUnavailableSheetsAndAllowAnExplicitRecheck()
    {
        string root = Path.Combine(Path.GetTempPath(), TestRootName, Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, ManifestPath))!);
        File.WriteAllText(Path.Combine(root, ExecutableName), string.Empty);
        File.WriteAllText(Path.Combine(root, ManifestPath), ManifestContents);
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
            services.AddSingleton(state);
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
                Assert.Contains("Recheck unavailable files", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, ReviewAction);
                Assert.Contains("Download 2 missing sheets? 0 of 2 sheets are already installed.", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, StartAction);
                Assert.Contains("2 files returned 404", view.ToHtmlString());

                await InvokeActionAsync(activator.Page!, ReviewAction);
                Assert.Contains("0 sheets are installed. The remaining 2 files returned 404", view.ToHtmlString());
                Assert.DoesNotContain("All 2 custom sprite sheets", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, RecheckAction);
                Assert.Contains("Download 2 missing sheets? 0 of 2 sheets are already installed.", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, CancelReviewAction);
                Assert.Equal(2, installer.CreatePlan(root).UnavailableSheetCount);

                handler.Available = true;
                await InvokeActionAsync(activator.Page!, RecheckAction);
                await InvokeActionAsync(activator.Page!, StartAction);
                Assert.Contains("Downloaded 2 custom sprite sheets", view.ToHtmlString());
                await InvokeActionAsync(activator.Page!, ReviewAction);
                Assert.Contains("All 2 custom sprite sheets from the installed manifest are already available.", view.ToHtmlString());
            });
        }
        finally
        {
            preferenceImplementation.SetValue(null, originalPreferences);
            Directory.Delete(root, true);
        }
    }

    /// <summary>Invokes a production button handler and flushes its resulting component render.</summary>
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

    /// <summary>Captures the production component so its existing handlers can be exercised.</summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>Gets the rendered settings component.</summary>
        internal TrackerSettingsPage? Page { get; private set; }

        /// <summary>Creates a component and retains the settings instance.</summary>
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

    /// <summary>Returns 404s until an explicit test recovery makes PNG content available.</summary>
    private sealed class RecoverableResponseHandler : HttpMessageHandler
    {
        /// <summary>Gets or sets whether the remote resources have recovered.</summary>
        internal bool Available { get; set; }

        /// <summary>Returns a controlled server result for each request.</summary>
        /// <param name="request">The outgoing resource request.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>The controlled response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Available
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>Loads the real settings text without desktop startup services.</summary>
    private sealed class SettingsLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(ResourceName, typeof(TrackerResources).Assembly);

        /// <summary>Gets a production text resource.</summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>Gets a formatted production text resource.</summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>Provides the unused enumeration contract.</summary>
        /// <param name="includeParentCultures">Whether parent resources should be included.</param>
        /// <returns>An empty sequence because tests resolve individual keys.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>Supplies unrelated graph defaults without reading or changing desktop user preferences.</summary>
    private sealed class DefaultPreferences : IPreferences
    {
        /// <summary>Reports that no user preference is stored in this isolated test.</summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The preference group.</param>
        /// <returns>False for every key.</returns>
        public bool ContainsKey(string key, string? sharedName = null) => false;

        /// <summary>Leaves the empty isolated preferences unchanged.</summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The preference group.</param>
        public void Remove(string key, string? sharedName = null) { }

        /// <summary>Leaves the empty isolated preferences unchanged.</summary>
        /// <param name="sharedName">The preference group.</param>
        public void Clear(string? sharedName = null) { }

        /// <summary>Ignores unrelated preference writes.</summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="value">The unused preference value.</param>
        /// <param name="sharedName">The preference group.</param>
        public void Set<T>(string key, T value, string? sharedName = null) { }

        /// <summary>Returns the default without accessing desktop storage.</summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="defaultValue">The normal application default.</param>
        /// <param name="sharedName">The preference group.</param>
        /// <returns>The supplied default.</returns>
        public T Get<T>(string key, T defaultValue, string? sharedName = null) => defaultValue;
    }
}

/// <summary>Isolates tests that replace the platform's process-wide preferences dependency.</summary>
[CollectionDefinition(nameof(SpriteSettingsCollection), DisableParallelization = true)]
public sealed class SpriteSettingsCollection { }
