using System.Reflection;
using System.Runtime.CompilerServices;
using Ironmon.Tracker.App.Components.Pages;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Renders the production run manager through save selection, rules and blocked gameplay.
/// </summary>
[Collection(nameof(SpriteSettingsCollection))]
public sealed class RunManagementViewTests
{
    private const string TestRootName = "IronmonRunManagementViews";
    private const string SnapshotField = "_snapshot";
    private const string CreatingField = "_creating";
    private const string StepField = "_step";
    private const string SelectedField = "_selected";
    private const string ConfirmField = "_confirm";
    private const string RenderMethod = "StateHasChanged";
    private const string BeginNewMethod = "BeginNew";
    private const string RequestStartMethod = "RequestStart";
    private const string ContinuePreview = "continue";
    private const string SavePreview = "save";
    private const string OccupiedPreview = "occupied";
    private const string RulesPreview = "rules";
    private const string ReviewPreview = "review";
    private const string OverwritePreview = "overwrite";
    private const string GameplayPreview = "gameplay";
    private const string ManualPrefix = "File ";
    private const string AutoSlot = "Auto 1";
    private const string ProjectRelativeRoot = "../../../..";
    private const string PreviewRelativePath = "data/run-management-{0}-preview.html";
    private const string StylesRelativePath = "tracker/src/Tracker/Ironmon.Tracker.App/wwwroot";
    private const string IndexFileName = "index.html";
    private const string StylePattern = "<link rel=\"stylesheet\" href=\"([^\"]+)\"";
    private const string ScopedStyleFileName = "Ironmon Tracker.styles.css";
    private const string ScopedStylesRelativePath = "tracker/src/Tracker/Ironmon.Tracker.App/obj/Release/net10.0-windows10.0.19041.0/win-x64/scopedcss/projectbundle/Ironmon Tracker.bundle.scp.css";

    /// <summary>
    /// Verifies real component markup mirrors slots, uses existing help controls, and blocks gameplay actions.
    /// </summary>
    /// <returns>A task representing component rendering and assertions for run-management availability.</returns>
    [Fact]
    public async Task ProductionRunManagerUsesActualSlotsHelpAndTitleOrMenuAvailability()
    {
        string root = Path.Combine(Path.GetTempPath(), TestRootName, Guid.NewGuid().ToString());
        FieldInfo preferenceField = typeof(Preferences).GetFields(BindingFlags.Static | BindingFlags.NonPublic).Single(field => typeof(IPreferences).IsAssignableFrom(field.FieldType));
        object? originalPreferences = preferenceField.GetValue(null);
        preferenceField.SetValue(null, new TrackerGameLauncherTests.StartupPreferences());
        try
        {
            TrackerConnectionState state = new();
            TrackerConnectionOptions options = new(0, "0.8.9", false, TimeSpan.FromSeconds(2));
            using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
            TrackerRequestClient requests = new(session, options, state, new AreaDiscoveryStore(new TrackerKnowledgeOptions(root)));
            CapturingActivator activator = new();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<Microsoft.JSInterop.IJSRuntime, SettingsJsRuntime>();
            services.AddSingleton(state);
            services.AddSingleton(requests);
            services.AddSingleton<IComponentActivator>(activator);
            services.AddSingleton<IStringLocalizer<TrackerResources>>(new ViewLocalizer());
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<TrackerPlayPage>(ParameterView.Empty);
                Assert.Contains("Connect the game", view.ToHtmlString());
                List<GameSaveSlotPayload> slots = [.. Enumerable.Range(0, 8).Select(index => new GameSaveSlotPayload { Slot = ManualPrefix + (char)('A' + index), Manual = true, Ironmon = index == 0, Occupied = index <= 1, Loadable = true, Trainer = "Trainer", Location = "Littleroot Town", Badges = 2, AttemptNumber = 4, Rules = new(), Revision = "revision" })];
                slots.Add(new() { Slot = AutoSlot, Ironmon = true, Occupied = true, Loadable = true, Trainer = "Trainer", Location = "Route 101", Rules = new(), Revision = "revision" });
                Set(activator.Page!, SnapshotField, new GameManagementPayload { State = RunManagementProtocol.MainMenuState, MenuId = "menu", Slots = slots });
                state.Publish(TrackerConnectionStatus.Connected);
                Render(activator.Page!);
                string continueHtml = view.ToHtmlString();
                Assert.Contains(ManualPrefix + 'A', continueHtml);
                Assert.Contains(AutoSlot, continueHtml);
                Assert.DoesNotContain(ManualPrefix + 'B', continueHtml);
                ExportPreview(ContinuePreview, continueHtml);
                typeof(TrackerPlayPage).GetMethod(BeginNewMethod, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page!, null);
                Render(activator.Page!);
                string saveHtml = view.ToHtmlString();
                foreach (GameSaveSlotPayload slot in slots.Where(slot => slot.Manual))
                    Assert.Contains(slot.Slot, saveHtml);

                Assert.DoesNotContain(AutoSlot, saveHtml);
                ExportPreview(SavePreview, saveHtml);
                Set(activator.Page!, SelectedField, slots[1]);
                Render(activator.Page!);
                Assert.Contains("File B will be replaced", view.ToHtmlString());
                ExportPreview(OccupiedPreview, view.ToHtmlString());
                Set(activator.Page!, StepField, 2);
                Render(activator.Page!);
                string rulesHtml = view.ToHtmlString();
                Assert.Contains("obsidian-help-button", rulesHtml);
                Assert.Contains("popovertarget", rulesHtml);
                Assert.Contains("Trainer Pokémon", System.Net.WebUtility.HtmlDecode(rulesHtml));
                Assert.DoesNotContain("Automatic reset", rulesHtml);
                ExportPreview(RulesPreview, rulesHtml);
                Set(activator.Page!, StepField, 3);
                Render(activator.Page!);
                ExportPreview(ReviewPreview, view.ToHtmlString());
                Task confirmation = (Task)typeof(TrackerPlayPage).GetMethod(RequestStartMethod, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page!, null)!;
                await confirmation;
                Render(activator.Page!);
                Assert.Contains("Replace File B?", view.ToHtmlString());
                Assert.Contains("Keep save", view.ToHtmlString());
                ExportPreview(OverwritePreview, view.ToHtmlString());
                Set(activator.Page!, ConfirmField, false);
                Set(activator.Page!, CreatingField, false);
                Set(activator.Page!, SnapshotField, new GameManagementPayload { State = RunManagementProtocol.TitleScreenState, MenuId = RunManagementProtocol.TitleScreenState, Slots = slots });
                Render(activator.Page!);
                Assert.Contains("New run", view.ToHtmlString());
                Assert.Contains("Continue", view.ToHtmlString());
                Set(activator.Page!, SnapshotField, new GameManagementPayload { State = "gameplay" });
                Render(activator.Page!);
                Assert.Contains("Return to the game", view.ToHtmlString());
                Assert.DoesNotContain("Start Ironmon run", view.ToHtmlString());
                ExportPreview(GameplayPreview, view.ToHtmlString());
            });
        }
        finally
        {
            preferenceField.SetValue(null, originalPreferences);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Sets a private draft field on the mounted production page.
    /// </summary>
    /// <param name="page">The mounted page.</param>
    /// <param name="name">The field identifier.</param>
    /// <param name="value">The value to assign to the private field.</param>
    private static void Set(TrackerPlayPage page, string name, object value)
        => typeof(TrackerPlayPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    /// <summary>
    /// Requests a production component render on its renderer dispatcher.
    /// </summary>
    /// <param name="page">The mounted page.</param>
    private static void Render(TrackerPlayPage page)
        => typeof(ComponentBase).GetMethod(RenderMethod, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    /// <summary>
    /// Exports actual rendered markup and built styles for visual inspection.
    /// </summary>
    /// <param name="screen">The preview screen name.</param>
    /// <param name="html">The renderer-produced component markup.</param>
    /// <param name="sourcePath">The compiler-provided source location.</param>
    internal static void ExportPreview(string screen, string html, [CallerFilePath] string sourcePath = "")
    {
        string project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, ProjectRelativeRoot));
        string stylesRoot = Path.Combine(project, StylesRelativePath);
        string index = File.ReadAllText(Path.Combine(stylesRoot, IndexFileName));
        string css = string.Join(Environment.NewLine, System.Text.RegularExpressions.Regex.Matches(index, StylePattern).Select(match => File.ReadAllText(match.Groups[1].Value == ScopedStyleFileName ? Path.Combine(project, ScopedStylesRelativePath) : Path.Combine(stylesRoot, match.Groups[1].Value))));
        File.WriteAllText(Path.Combine(project, string.Format(PreviewRelativePath, screen)), $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><style>{css}</style></head><body><main class=\"tracker-shell obsidian\">{html}</main></body></html>");
    }

    /// <summary>
    /// Captures the mounted page while creating all other components normally.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the mounted production page.
        /// </summary>
        public TrackerPlayPage? Page { get; private set; }

        /// <summary>
        /// Creates a requested Blazor component.
        /// </summary>
        /// <param name="componentType">The component to activate.</param>
        /// <returns>A new component instance, with the run-management page retained for fixture updates.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is TrackerPlayPage page)
                Page = page;

            return component;
        }
    }

    /// <summary>
    /// Supplies readable labels without changing desktop language settings.
    /// </summary>
    private sealed class ViewLocalizer : IStringLocalizer<TrackerResources>
    {
        /// <summary>
        /// Gets one stable fixture label.
        /// </summary>
        /// <param name="name">The resource key.</param>
        /// <returns>The fixture label.</returns>
        public LocalizedString this[string name] => new(name, name);

        /// <summary>
        /// Gets one formatted fixture label.
        /// </summary>
        /// <param name="name">The resource key.</param>
        /// <param name="arguments">The formatting arguments, which this fixture ignores.</param>
        /// <returns>The fixture label.</returns>
        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        /// <summary>
        /// Returns the fixture's empty enumeration.
        /// </summary>
        /// <param name="includeParentCultures">Unused culture option.</param>
        /// <returns>No predefined labels.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
