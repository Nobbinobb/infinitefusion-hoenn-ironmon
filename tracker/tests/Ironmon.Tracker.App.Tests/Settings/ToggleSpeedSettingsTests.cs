using System.Reflection;
using System.Resources;
using System.Text.Json;
using Ironmon.Tracker.App.Components.Settings;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Exercises the real speed settings, explicit persistence, rejected drafts, and detailed help.
/// </summary>
[Collection(nameof(SpriteSettingsCollection))]
public sealed class ToggleSpeedSettingsTests
{
    private const string ResourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string Version = "test";
    private const string HelpField = "_helpOpen";
    private const string Preview = "toggle-speed";
    private const string HelpPreview = "toggle-speed-help";
    private const string PreferenceKey = "toggle_speed_profile";

    /// <summary>
    /// Saves through the production callback while disconnected, retains invalid drafts, restores settings, and renders help.
    /// </summary>
    /// <returns>The completed real component and persistence checks.</returns>
    [Trait(TrackerTestCategories.Category, TrackerTestCategories.GameCatalog)]
    [Fact]
    public async Task ExplicitSavePersistsOnlyCompleteProfilesAndHelpUsesActualDialog()
    {
        FieldInfo preferencesField = typeof(Preferences).GetFields(BindingFlags.Static | BindingFlags.NonPublic).Single(field => typeof(IPreferences).IsAssignableFrom(field.FieldType));
        object? original = preferencesField.GetValue(null);
        DefaultPreferences preferences = new();
        preferencesField.SetValue(null, preferences);
        try
        {
            using TestWorkspace workspace = new();
            TrackerConnectionOptions options = new(0, Version, false, TimeSpan.FromSeconds(2));
            TrackerConnectionState state = new();
            using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
            TrackerRequestClient requests = new(session, options, state, new AreaDiscoveryStore(new TrackerKnowledgeOptions(workspace.Root)));
            CapturingActivator activator = new();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
            services.AddSingleton(options);
            services.AddSingleton(state);
            services.AddSingleton(requests);
            services.AddSingleton<IComponentActivator>(activator);
            services.AddSingleton<IStringLocalizer<TrackerResources>>(new SettingsLocalizer());
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<ToggleSpeedSettings>(ParameterView.Empty);
                Assert.False(preferences.ContainsKey(PreferenceKey));
                Assert.Contains("Multiplier palette", view.ToHtmlString());
                Assert.Contains("toggle-speed-settings", view.ToHtmlString());
                Assert.DoesNotContain("role=\"dialog\"", view.ToHtmlString());
                RunManagementViewTests.ExportPreview(Preview, view.ToHtmlString());
                ToggleSpeedConfiguration profile = options.ToggleSpeed;
                Assert.Null(await activator.Page!.SaveProfileAsync(TrackerJson.SerializePayload(profile)));
                string saved = preferences.Get(PreferenceKey, string.Empty);
                Assert.NotEmpty(saved);
                Assert.Equal(JsonSerializer.Serialize(profile, TrackerJson.Options), JsonSerializer.Serialize(TrackerToggleSpeedSettings.Load(), TrackerJson.Options));
                ToggleSpeedConfiguration committed = options.ToggleSpeed;
                ToggleSpeedConfiguration invalid = new() { Lanes = profile.Lanes };
                Assert.NotNull(await activator.Page.SaveProfileAsync(TrackerJson.SerializePayload(invalid)));
                Assert.Equal(saved, preferences.Get(PreferenceKey, string.Empty));
                Assert.Same(committed, options.ToggleSpeed);
                typeof(ToggleSpeedSettings).GetField(HelpField, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(activator.Page, true);
                await ((IHandleEvent)activator.Page).HandleEventAsync(new EventCallbackWorkItem((Action)(() => { })), null);
                string help = System.Net.WebUtility.HtmlDecode(view.ToHtmlString());
                Assert.Contains("Toggle speed help", help);
                Assert.Contains("Create, split, and move groups", help);
                Assert.Contains("first visit", help, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Hold mode", help);
                RunManagementViewTests.ExportPreview(HelpPreview, view.ToHtmlString());
                preferences.Set(PreferenceKey, Version);
                ToggleSpeedValidation.Validate(TrackerToggleSpeedSettings.Load());
                Assert.Equal(3, TrackerToggleSpeedSettings.Load().Lanes.Overworld.Count);
            });
        }
        finally
        {
            preferencesField.SetValue(null, original);
        }
    }

    /// <summary>
    /// Captures the real dialog without replacing nested production components.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the mounted speed settings.
        /// </summary>
        internal ToggleSpeedSettings? Page { get; private set; }

        /// <summary>
        /// Constructs the requested component and retains the speed editor.
        /// </summary>
        /// <param name="componentType">The component requested by the renderer.</param>
        /// <returns>The constructed component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is ToggleSpeedSettings page)
                Page = page;
            return component;
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
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }

    /// <summary>
    /// Stores test preferences without reading or changing desktop user preferences.
    /// </summary>
    private sealed class DefaultPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        /// <summary>
        /// Reports whether a test preference exists.
        /// </summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The preference group.</param>
        /// <returns>Whether the key exists.</returns>
        public bool ContainsKey(string key, string? sharedName = null)
            => _values.ContainsKey(key);

        /// <summary>
        /// Removes the requested test preference.
        /// </summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The preference group.</param>
        public void Remove(string key, string? sharedName = null)
            => _values.Remove(key);

        /// <summary>
        /// Removes the requested test preference.
        /// </summary>
        /// <param name="sharedName">The preference group.</param>
        public void Clear(string? sharedName = null)
            => _values.Clear();

        /// <summary>
        /// Stores a preference in memory.
        /// </summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="value">The value to retain.</param>
        /// <param name="sharedName">The preference group.</param>
        public void Set<T>(string key, T value, string? sharedName = null)
            => _values[key] = value!;

        /// <summary>
        /// Reads the retained value without accessing desktop storage.
        /// </summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="defaultValue">The normal application default.</param>
        /// <param name="sharedName">The preference group.</param>
        /// <returns>The retained value or supplied default.</returns>
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
            => _values.TryGetValue(key, out object? value) ? (T)value : defaultValue;
    }
}
