using Ironmon.Tracker.App.Components.Settings;
using Ironmon.Tracker.App.History;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.History;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Renders the production maintenance controls and checks automatic protection, connection and cancellation behavior.
/// </summary>
public sealed class HistoryMaintenanceSettingsTests
{
    private const string _testRoot = "IronmonHistorySettingsTests";
    private const string _profile = "profile";
    private const string _passwordInput = "type=\"password\"";
    private const string _disabledAttribute = "disabled";
    private const string _reviewMethod = "ReviewAsync";
    private const string _reviewField = "_review";
    private const string _executeMethod = "ExecuteAsync";
    private const string _importMethod = "PrepareImport";
    private const string _preview = "history-maintenance";
    private const string _renderMethod = "StateHasChanged";
    private const string _measurementFormat = "0.000";
    private const string _exportStatusMethod = "ExportStatus";

    /// <summary>
    /// Reports net disk savings rounded down and identifies exports that consume additional disk space.
    /// </summary>
    /// <param name="removedBytes">The signed physical reduction in tracker storage.</param>
    /// <param name="archiveBytes">The encrypted archive size.</param>
    /// <param name="expectedMegabytes">The rounded-down magnitude shown to the user.</param>
    /// <param name="expectedChange">The expected disk-space direction.</param>
    [Theory]
    [InlineData(2000999L, 1000000L, "1.000", "disk space freed")]
    [InlineData(0L, 1000000L, "1.000", "additional disk space used")]
    [InlineData(-4096L, 1000000L, "1.004", "additional disk space used")]
    public void ExportCompletionIncludesArchiveCostAndDatabaseGrowth(long removedBytes, long archiveBytes, string expectedMegabytes, string expectedChange)
    {
        HistoryMaintenanceResult result = new(0, archiveBytes, removedBytes, 0);
        string text = (string)typeof(HistoryMaintenanceSettings).GetMethod(_exportStatusMethod, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [result])!;
        decimal expected = decimal.Parse(expectedMegabytes, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"{expected.ToString(_measurementFormat, System.Globalization.CultureInfo.CurrentCulture)} MB {expectedChange}", text);
        Assert.Contains("same drive", text);
    }

    /// <summary>
    /// Recovers maintenance after disconnection and keeps the profile unchanged when the native picker is cancelled.
    /// </summary>
    /// <param name="disconnectedStatus">The connection state after the game is no longer connected.</param>
    /// <returns>The asynchronous production Razor rendering scenario.</returns>
    [Theory]
    [InlineData(TrackerConnectionStatus.Stopped)]
    [InlineData(TrackerConnectionStatus.Waiting)]
    [InlineData(TrackerConnectionStatus.Error)]
    public async Task MaintenanceRecoversAfterDisconnectAndUsesPickersWithoutPasswords(TrackerConnectionStatus disconnectedStatus)
    {
        string root = Path.Combine(Path.GetTempPath(), _testRoot, Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        GameplayCollectionStore? store = null;
        try
        {
            TrackerConnectionState connection = new();
            connection.Publish(TrackerConnectionStatus.Connected);
            CapturingActivator activator = new();
            CancellingPicker picker = new();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton(connection);
            store = new(Path.Combine(root, _profile));
            services.AddSingleton(store);
            services.AddSingleton(new CompletedRunArchive(new TrackerKnowledgeOptions(root)));
            services.AddSingleton<IHistoryArchivePicker>(picker);
            services.AddSingleton<IComponentActivator>(activator);
            services.AddSingleton<IStringLocalizer<TrackerResources>>(new TestLocalizer());
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<HistoryMaintenanceSettings>(ParameterView.Empty);
                Assert.Contains("Review export", view.ToHtmlString());
                Assert.Contains("original tracker profile", view.ToHtmlString());
                Assert.Contains("Close the connected game", view.ToHtmlString());
                await InvokeAsync(activator.Page!, _reviewMethod);
                Assert.DoesNotContain("Estimated disk space freed", view.ToHtmlString());
                connection.Publish(TrackerConnectionStatus.Handshaking);
                await InvokeAsync(activator.Page!, _reviewMethod);
                Assert.Contains("Close the connected game", view.ToHtmlString());
                Assert.DoesNotContain("Estimated disk space freed", view.ToHtmlString());
                connection.Publish(disconnectedStatus);
                Assert.DoesNotContain("Close the connected game", view.ToHtmlString());
                Assert.DoesNotContain(_disabledAttribute, view.ToHtmlString());
                await InvokeAsync(activator.Page!, _executeMethod);
                Assert.Equal(0, picker.Calls);
                await InvokeAsync(activator.Page!, _reviewMethod);
                HistoryMaintenanceResult measurement = new(8500000, 1000000, 0, 10800000, EstimatedFreedBytes: 1234000);
                typeof(HistoryMaintenanceSettings).GetField(_reviewField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(activator.Page, measurement);
                typeof(ComponentBase).GetMethod(_renderMethod, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activator.Page, null);
                string html = view.ToHtmlString();
                Assert.Contains("Estimated disk space freed", html);
                Assert.Contains(1.234m.ToString(_measurementFormat, System.Globalization.CultureInfo.CurrentCulture), html);
                Assert.DoesNotContain(10.8m.ToString(_measurementFormat, System.Globalization.CultureInfo.CurrentCulture), html);
                Assert.Contains("same drive", html);
                Assert.Contains("MB", html);
                Assert.DoesNotContain(_passwordInput, html);
                Assert.Contains("Choose location, export and remove", html);
                Assert.DoesNotContain(_disabledAttribute, html);
                RunManagementViewTests.ExportPreview(_preview, html);
                await InvokeAsync(activator.Page!, _executeMethod);
                Assert.Equal(1, picker.Calls);
                Assert.Contains("Estimated disk space freed", view.ToHtmlString());
                connection.Publish(TrackerConnectionStatus.Connected);
                Assert.DoesNotContain("Estimated disk space freed", view.ToHtmlString());
                await InvokeAsync(activator.Page!, _executeMethod);
                Assert.Equal(1, picker.Calls);
                await InvokeAsync(activator.Page!, _importMethod);
                Assert.DoesNotContain("Choose archive and import", view.ToHtmlString());
                connection.Publish(disconnectedStatus);
                await InvokeAsync(activator.Page!, _importMethod);
                html = view.ToHtmlString();
                Assert.Contains("Choose archive and import", html);
                Assert.DoesNotContain(_passwordInput, html);
                Assert.DoesNotContain(_disabledAttribute, html);
                await InvokeAsync(activator.Page!, _executeMethod);
                Assert.Equal(2, picker.Calls);
                Assert.Equal(1, picker.ImportCalls);
            });
        }
        finally
        {
            if (store is not null)
                await store.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Runs an actual production component callback.
    /// </summary>
    /// <param name="page">The mounted production component.</param>
    /// <param name="method">The callback identifier.</param>
    /// <returns>The asynchronous production callback.</returns>
    private static async Task InvokeAsync(HistoryMaintenanceSettings page, string method)
    {
        object? result = typeof(HistoryMaintenanceSettings).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        if (result is Task pending)
            await pending;

        typeof(ComponentBase).GetMethod(_renderMethod, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
    }

    /// <summary>
    /// Captures the real maintenance component while constructing its child components normally.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the mounted maintenance component.
        /// </summary>
        internal HistoryMaintenanceSettings? Page { get; private set; }

        /// <summary>
        /// Constructs one renderer-owned component.
        /// </summary>
        /// <param name="componentType">The requested production component type.</param>
        /// <returns>The production component instance.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is HistoryMaintenanceSettings page)
                Page = page;
            return component;
        }
    }

    /// <summary>
    /// Simulates cancelling native file selection without accessing desktop files.
    /// </summary>
    private sealed class CancellingPicker : IHistoryArchivePicker
    {
        /// <summary>
        /// Gets the number of native picker requests made by production callbacks.
        /// </summary>
        internal int Calls { get; private set; }
        /// <summary>
        /// Gets the number of existing-archive picker requests made by production callbacks.
        /// </summary>
        internal int ImportCalls { get; private set; }
        /// <summary>
        /// Cancels a destination selection.
        /// </summary>
        /// <returns>A cancelled destination result.</returns>
        public Task<string?> ChooseExportAsync()
        {
            Calls++;
            return Task.FromResult<string?>(null);
        }
        /// <summary>
        /// Cancels an existing-archive selection.
        /// </summary>
        /// <returns>A cancelled archive result.</returns>
        public Task<string?> ChooseImportAsync()
        {
            Calls++;
            ImportCalls++;
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Supplies deterministic nested labels without reading desktop language preferences.
    /// </summary>
    private sealed class TestLocalizer : IStringLocalizer<TrackerResources>
    {
        /// <summary>
        /// Gets one stable label.
        /// </summary>
        /// <param name="name">The resource key.</param>
        /// <returns>The stable label.</returns>
        public LocalizedString this[string name] => new(name, name);
        /// <summary>
        /// Gets one stable formatted label.
        /// </summary>
        /// <param name="name">The resource key.</param>
        /// <param name="arguments">The unused fixture arguments.</param>
        /// <returns>The stable label.</returns>
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        /// <summary>
        /// Enumerates no predefined labels.
        /// </summary>
        /// <param name="includeParentCultures">The unused culture option.</param>
        /// <returns>An empty sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
