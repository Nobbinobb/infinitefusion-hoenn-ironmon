using Ironmon.Tracker.App.Components.Navigation;
using Ironmon.Tracker.App.Components.Settings;
using Ironmon.Tracker.App.Controls;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.RunState;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Exercises the production controls editor, its two-slot dialog, and category and device selection.
/// </summary>
public sealed class ControlSettingsTests
{
    private const string _version = "test";
    private const string _deviceIdentity = "rendered-controller";
    private const string _keyboardProfile = "keyboard";
    private const string _dualSense = "DualSense";
    private const string _xbox = "Xbox Controller";
    private const string _categoryAction = "SelectCategory";
    private const string _profileAction = "SelectProfile";
    private const string _captureAction = "BeginCapture";
    private const string _useAction = "UseCapture";
    private const string _clearAction = "ClearCapture";
    private const string _restoreAction = "RestoreDefaults";
    private const string _renderAction = "StateHasChanged";
    private const string _controllerField = "_controllers";
    private const string _previewEnvironment = "IRONMON_CONTROLS_PREVIEW";
    private const string _stylesheetPath = "../../tracker/src/Tracker/Ironmon.Tracker.App/wwwroot";
    private const string _htmlExtension = ".html";
    private const string _eKey = "e";
    private const string _eCode = "KeyE";
    private const string _escape = "Escape";
    private const string _conflictPreviewName = "conflict-preview";
    private const string _conflictSavedName = "conflict-saved";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _directionClass = "tracker-control-direction";
    private const string _crossClass = "tracker-control-cross";
    private const string _rowAttribute = "scope=\"row\"";
    private const string _dialogAttribute = "role=\"dialog\"";
    private const string _selectedTabAttribute = "aria-selected=\"true\"";
    private const string _pressedInputAttribute = "aria-pressed=\"true\"";
    private const string _legacyListClass = "tracker-controls-list";
    private const string _stylesheetTemplate = "<link rel=\"stylesheet\" href=\"{0}\">";
    private const string _previewTemplate = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">{0}<style>body{{margin:0;background:#090d12;color:#e7edf7;font:12px 'Segoe UI',sans-serif}}main{{box-sizing:border-box;width:min(486px,100%);padding:16px}}</style></head><body><main class=\"obsidian\">{1}</main></body></html>";
    private const string _customPreviewName = "custom-combinations";
    private const string _disabledPreviewName = "disabled-features";
    private const string _disabledRowClass = "tracker-control-disabled";
    private const string _disabledAttribute = " disabled";
    private static readonly string[] _stylesheets = ["tracker.css", "obsidian-tools.css", "obsidian.css", "tracker-controls.css"];

    /// <summary>
    /// Verifies the real page exposes each action once and renders keyboard and controller layouts and dialogs.
    /// </summary>
    /// <returns>A task that completes after all renderer and connection resources are disposed.</returns>
    [Trait(TrackerTestCategories.Category, TrackerTestCategories.GameCatalog)]
    [Fact]
    public async Task CategoriesAndDeviceProfilesRenderTheProductionBindingEditor()
    {
        using TestWorkspace workspace = new();
        TrackerKnowledgeOptions storage = new(workspace.Root);
        TrackerConnectionState state = new();
        await using TrackerConnectionService connection = new(new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), new TrackerDiagnosticsStore(), state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage));
        ControlProfiles profiles = new(() => null, _ => { });
        TrackerGlobalShortcutService shortcuts = new(state, connection, profiles);
        CapturingActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, ControlsLocalizer>();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton(profiles);
        services.AddSingleton(shortcuts);
        services.AddSingleton(state);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        Assert.Equal(ControlCatalog.Actions.Count, ControlCatalog.Groups.SelectMany(group => group.Actions).Distinct().Count());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<TrackerControlsPage>(ParameterView.Empty);
            TrackerControlsPage page = activator.Page!;
            foreach (string name in new[] { _keyboardProfile, _dualSense, _xbox })
            {
                if (name != _keyboardProfile)
                {
                    profiles.Remember(_deviceIdentity, name, true);
                    typeof(TrackerGlobalShortcutService).GetField(_controllerField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shortcuts, (IReadOnlyList<ControllerReading>)[new(_deviceIdentity, name, true, new HashSet<string>())]);
                }

                Invoke(page, _profileAction, new ChangeEventArgs { Value = name == _keyboardProfile ? _keyboardProfile : _deviceIdentity });
                for (int category = 0; category < ControlCatalog.Groups.Count; category++)
                {
                    Invoke(page, _categoryAction, category);
                    string html = view.ToHtmlString();
                    Assert.Contains("Binding 1", html);
                    Assert.Contains("Binding 2", html);
                    Assert.Contains(_rowAttribute, html);
                    Assert.DoesNotContain(_legacyListClass, html);
                    Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, _selectedTabAttribute));
                    if (name != _keyboardProfile && category == 0)
                        Assert.Contains(_directionClass, html);

                    if (name == _dualSense && category == 1)
                        Assert.Contains(_crossClass, html);

                    foreach (string id in ControlCatalog.Groups[category].Actions)
                        Assert.Contains(ControlCatalog.Actions.Single(action => action.Id == id).Label, System.Net.WebUtility.HtmlDecode(html));

                    Export(name + category, html);
                }

                Invoke(page, _captureAction, ControlCatalog.Player, 1);
                Assert.Contains(_dialogAttribute, view.ToHtmlString());
                Assert.Contains("Binding 2", view.ToHtmlString());
                if (name != _keyboardProfile)
                {
                    shortcuts.PreviewController([ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.RightYNegative]);
                    Assert.Contains("Controller inputs", view.ToHtmlString());
                    Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(view.ToHtmlString(), _pressedInputAttribute).Count);
                }

                Export(name + _captureAction, view.ToHtmlString());
                shortcuts.CancelCapture();
            }

            profiles.Remember(_deviceIdentity, _dualSense, true);
            profiles.SetControllerSlot(_deviceIdentity, ControlCatalog.Player, 1, [ControlInput.South, ControlInput.LeftTrigger]);
            profiles.SetControllerSlot(_deviceIdentity, ControlCatalog.Enemy, 1, [ControlInput.RightStick, ControlInput.RightXPositive]);
            profiles.SetControllerSlot(_deviceIdentity, ControlCatalog.Lookup, 1, [ControlInput.DpadUp, ControlInput.LeftTrigger]);
            profiles.SetControllerSlot(_deviceIdentity, ControlCatalog.Archive, 1, [ControlInput.LeftStick, ControlInput.LeftYPositive]);
            Invoke(page, _profileAction, new ChangeEventArgs { Value = _deviceIdentity });
            Invoke(page, _categoryAction, 4);
            Export(_customPreviewName, view.ToHtmlString());

            Invoke(page, _profileAction, new ChangeEventArgs { Value = _keyboardProfile });
            Invoke(page, _categoryAction, 3);
            ControlAction confirm = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Confirm);
            Invoke(page, _captureAction, confirm.Id, 1);
            KeyboardEventArgs key = new() { Key = _eKey, Code = _eCode };
            shortcuts.CaptureKeyDown(key);
            shortcuts.CaptureKeyUp(key);
            Assert.Contains("Use binding", view.ToHtmlString());
            Assert.Equal([0x0D], profiles.Keyboard(confirm)[1]);
            Invoke(page, _useAction);
            Assert.DoesNotContain(_dialogAttribute, view.ToHtmlString());
            Assert.Equal([0x43], profiles.Keyboard(confirm)[0]);
            Assert.Equal([0x45], profiles.Keyboard(confirm)[1]);
            Invoke(page, _captureAction, confirm.Id, 0);
            Invoke(page, _clearAction);
            Assert.Empty(profiles.Keyboard(confirm)[0]);
            Assert.Equal([0x45], profiles.Keyboard(confirm)[1]);
            Assert.Contains("Unbound", view.ToHtmlString());
            Invoke(page, _restoreAction);
            Assert.Equal(confirm.Keyboard, profiles.Keyboard(confirm));

            await page.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(TrackerControlsPage.Features)] = new TrackerFeatureSettings { BattleQuickItems = false, QuickRun = false } }));
            int[][] savedRun = profiles.Keyboard(ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.BattleRun));
            Invoke(page, _categoryAction, 3);
            Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(view.ToHtmlString(), _disabledRowClass).Count);
            Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(view.ToHtmlString(), _disabledAttribute).Count);
            foreach (string action in new[] { ControlCatalog.BattleRun, ControlCatalog.QuickItemLeft, ControlCatalog.QuickItemRight, ControlCatalog.QuickItemUp, ControlCatalog.QuickItemDown, ControlCatalog.QuickItemUse })
            {
                Invoke(page, _captureAction, action, 0);
                Assert.Null(shortcuts.CaptureAction);
            }

            Export(_disabledPreviewName, view.ToHtmlString());
            await page.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(TrackerControlsPage.Features)] = new TrackerFeatureSettings() }));
            Invoke(page, _categoryAction, 3);
            Assert.DoesNotContain(_disabledRowClass, view.ToHtmlString());
            Assert.Equal(savedRun, profiles.Keyboard(ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.BattleRun)));
            Invoke(page, _categoryAction, 4);
            Invoke(page, _captureAction, ControlCatalog.Reset, 1);
            KeyboardEventArgs escape = new() { Key = _escape, Code = _escape };
            shortcuts.CaptureKeyDown(escape);
            shortcuts.CaptureKeyUp(escape);
            Assert.Contains("Using this binding will unbind:", view.ToHtmlString());
            Assert.Contains("Open pause menu (Binding 2)", view.ToHtmlString());
            Export(_conflictPreviewName, view.ToHtmlString());
            shortcuts.CancelCapture();
            ControlAction menu = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Menu);
            Assert.Equal(menu.Keyboard, profiles.Keyboard(menu));
            Invoke(page, _captureAction, ControlCatalog.Reset, 1);
            shortcuts.CaptureKeyDown(escape);
            shortcuts.CaptureKeyUp(escape);
            Invoke(page, _useAction);
            Assert.DoesNotContain(_dialogAttribute, view.ToHtmlString());
            Assert.Contains("Unbound: Open pause menu (Binding 2)", view.ToHtmlString());
            Assert.Empty(profiles.Keyboard(menu)[1]);
            Export(_conflictSavedName, view.ToHtmlString());
            profiles.SetKeyboard(ControlCatalog.Menu, menu.Keyboard);
            Invoke(page, _categoryAction, 1);
            Assert.Contains("Conflicting bindings are inactive", view.ToHtmlString());
            Invoke(page, _profileAction, new ChangeEventArgs { Value = _deviceIdentity });
            Assert.DoesNotContain("Unbound:", view.ToHtmlString());
            Assert.DoesNotContain("Conflicting bindings are inactive", view.ToHtmlString());
        });
    }

    /// <summary>
    /// Invokes an actual editor handler and flushes its component render.
    /// </summary>
    /// <param name="page">The rendered controls page.</param>
    /// <param name="action">The private handler name.</param>
    /// <param name="arguments">The handler arguments.</param>
    private static void Invoke(TrackerControlsPage page, string action, params object[] arguments)
    {
        typeof(TrackerControlsPage).GetMethod(action, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, arguments);
        typeof(ComponentBase).GetMethod(_renderAction, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
    }

    /// <summary>
    /// Exports real Razor output with production styles when a local visual review directory is requested.
    /// </summary>
    /// <param name="name">The profile and category identifying the preview.</param>
    /// <param name="html">The rendered production component.</param>
    private static void Export(string name, string html)
    {
        string? directory = Environment.GetEnvironmentVariable(_previewEnvironment);
        if (directory is null)
            return;

        Directory.CreateDirectory(directory);
        string styles = string.Concat(_stylesheets.Select(file => string.Format(_stylesheetTemplate, new Uri(Path.GetFullPath(Path.Combine(directory, _stylesheetPath, file))).AbsoluteUri)));
        File.WriteAllText(Path.Combine(directory, name + _htmlExtension), string.Format(_previewTemplate, styles, html));
    }

    /// <summary>
    /// Captures the rendered page while leaving all other production components intact.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the actual rendered controls page.
        /// </summary>
        public TrackerControlsPage? Page { get; private set; }

        /// <summary>
        /// Constructs a component and retains the controls editor.
        /// </summary>
        /// <param name="componentType">The requested production component type.</param>
        /// <returns>The constructed component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is TrackerControlsPage page)
                Page = page;

            return component;
        }
    }

    /// <summary>
    /// Resolves the production text used by common Obsidian components.
    /// </summary>
    private sealed class ControlsLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets one production localization resource.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets a formatted production localization resource.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Provides the unused enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether to include parent cultures.</param>
        /// <returns>An empty collection because this test resolves resources individually.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
