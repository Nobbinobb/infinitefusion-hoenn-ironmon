using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Pages;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Exercises production earned-icon actions, artwork loading, selection changes, and dialog dismissal.
/// </summary>
public sealed class AchievementArtworkViewerTests
{
    private const string _artworkButton = "achievement-artwork-button";
    private const string _smallWidth = "width=\"50\"";
    private const string _dialogRole = "role=\"dialog\"";
    private const string _largeWidth = "width=\"1024\"";
    private const string _largeImage = "achievement-artwork-image";
    private const string _openMethod = "OpenArtwork";
    private const string _keyboardMethod = "HandleKeyDown";
    private const string _escape = "Escape";
    private const string _unavailable = "Artwork is unavailable";
    private const string _batch = """
        {"schema_version":1,"profile_id":"11111111111111111111111111111111","epoch_id":"22222222222222222222222222222222","stream_id":"33333333333333333333333333333333","events":[{"sequence":"1","type":"item.disposition_resolved","attempt_id":"55555555555555555555555555555555","context":{},"payload":{"item_id":"POTION","disposition":"consumed","quantity":100,"battle_entry_owner":"player","current_controller":"player"}}]}
        """;

    /// <summary>
    /// Opens an earned image through the real page callback and closes it through the shared Escape handler.
    /// </summary>
    /// <returns>A task completing after the list, viewer, keyboard dismissal, and ledger are checked.</returns>
    [Fact]
    public async Task EarnedIconOpensArtworkAndEscapeReturnsToTheList()
    {
        using TestWorkspace directory = new();
        await using GameplayCollectionStore store = new(directory.Root);
        await store.AcceptBatchAsync(JsonSerializer.Deserialize<JsonElement>(_batch));
        var earned = await store.GetAchievementsAsync();
        UnlockedAchievement selected = earned.Single(item => item.Number == 206);
        await RenderAsync<AchievementsPage>(store, [], async (activator, html) =>
        {
            Assert.Contains(_artworkButton, html());
            Assert.Contains(_smallWidth, html());
            Assert.DoesNotContain(_dialogRole, html());
            MethodInfo open = typeof(AchievementsPage).GetMethod(_openMethod, BindingFlags.NonPublic | BindingFlags.Instance)!;
            await ((IHandleEvent)activator.Page!).HandleEventAsync(new EventCallbackWorkItem((Action)(() => open.Invoke(activator.Page, [selected]))), null);
            await WaitForImageAsync(html);
            Assert.Contains(_dialogRole, html());
            Assert.Contains(_largeWidth, html());
            Assert.Contains(selected.Name, html());
            MethodInfo keyboard = typeof(ObsidianDialogScope).GetMethod(_keyboardMethod, BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)keyboard.Invoke(activator.Scope, [new KeyboardEventArgs { Key = _escape }])!;
            Assert.DoesNotContain(_dialogRole, html());
            Assert.DoesNotContain(_largeImage, html());
            Assert.Contains(_artworkButton, html());
        });
        Assert.Equal(earned, await store.GetAchievementsAsync());
    }

    /// <summary>
    /// Ensures the most recent selection wins when an earlier asynchronous image request is superseded.
    /// </summary>
    /// <returns>A task completing after successive selections render only the final detail image.</returns>
    [Fact]
    public async Task ChangingArtworkSelectionDoesNotRestoreAnOlderImage()
    {
        using TestWorkspace directory = new();
        await using GameplayCollectionStore store = new(directory.Root);
        await store.AcceptBatchAsync(JsonSerializer.Deserialize<JsonElement>(_batch));
        var earned = await store.GetAchievementsAsync();
        UnlockedAchievement first = earned.Single(item => item.Number == 206);
        UnlockedAchievement second = earned.Single(item => item.Number == 207);
        UnlockedAchievement third = earned.Single(item => item.Number == 208);
        AchievementArtwork expected = (await store.GetAchievementArtworkAsync(third.Id))!;
        await RenderAsync<AchievementArtworkDialog>(store, new() { [nameof(AchievementArtworkDialog.Achievement)] = first }, async (activator, html) =>
        {
            Task pending = activator.Dialog!.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(AchievementArtworkDialog.Achievement)] = second }));
            Task latest = activator.Dialog.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(AchievementArtworkDialog.Achievement)] = third }));
            await Task.WhenAll(pending, latest);
            Assert.Contains(expected.ImageDataUri, System.Net.WebUtility.HtmlDecode(html()));
            Assert.Contains(third.Name, System.Net.WebUtility.HtmlDecode(html()));
            Assert.Contains(_largeWidth, html());
        });
    }

    /// <summary>
    /// Keeps unearned artwork out of the dialog even if its component is asked to show an unearned identity.
    /// </summary>
    /// <returns>A task completing after the detail service rejects the image and the dialog renders a recoverable state.</returns>
    [Fact]
    public async Task ViewerDoesNotRevealAnUnearnedImage()
    {
        using TestWorkspace earnedDirectory = new();
        await using GameplayCollectionStore earnedStore = new(earnedDirectory.Root);
        await earnedStore.AcceptBatchAsync(JsonSerializer.Deserialize<JsonElement>(_batch));
        UnlockedAchievement selected = (await earnedStore.GetAchievementsAsync()).Single(item => item.Number == 206);
        using TestWorkspace emptyDirectory = new();
        await using GameplayCollectionStore emptyStore = new(emptyDirectory.Root);
        await RenderAsync<AchievementArtworkDialog>(emptyStore, new() { [nameof(AchievementArtworkDialog.Achievement)] = selected }, (_, html) =>
        {
            Assert.True(html().Contains(_unavailable, StringComparison.Ordinal), html());
            Assert.DoesNotContain(_largeImage, html());
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Waits for an interactive child render without replacing its production asynchronous load path.
    /// </summary>
    /// <param name="html">Reads the root's latest rendered HTML.</param>
    /// <returns>A task completing once the real image has appeared.</returns>
    private static async Task WaitForImageAsync(Func<string> html)
    {
        for (int attempt = 0; attempt < 100 && !html().Contains(_largeImage, StringComparison.Ordinal); attempt++)
            await Task.Delay(20);

        Assert.Contains(_largeImage, html());
    }

    /// <summary>
    /// Renders real application components against test-owned collection storage and localization.
    /// </summary>
    /// <typeparam name="T">The root component to mount.</typeparam>
    /// <param name="store">The isolated collection service.</param>
    /// <param name="parameters">The root's initial parameters.</param>
    /// <param name="check">Checks component actions and the latest rendered HTML on the renderer dispatcher.</param>
    /// <returns>A task completing after rendering, interaction, and component disposal.</returns>
    private static async Task RenderAsync<T>(GameplayCollectionStore store, Dictionary<string, object?> parameters, Func<ArtworkActivator, Func<string>, Task> check) where T : IComponent
    {
        ServiceCollection services = new();
        ArtworkActivator activator = new();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = TrackerLocalizationConstants.ResourcesPath);
        services.AddSingleton(store);
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, DetachedRuntime>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            await check(activator, root.ToHtmlString);
        });
    }

    /// <summary>
    /// Captures mounted production components for their actual callbacks.
    /// </summary>
    private sealed class ArtworkActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the mounted achievement collection page.
        /// </summary>
        internal AchievementsPage? Page { get; private set; }

        /// <summary>
        /// Gets the active detail component.
        /// </summary>
        internal AchievementArtworkDialog? Dialog { get; private set; }

        /// <summary>
        /// Gets the active shared keyboard and focus boundary.
        /// </summary>
        internal ObsidianDialogScope? Scope { get; private set; }

        /// <summary>
        /// Creates the framework-requested component and remembers the relevant mounted instance.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The newly constructed component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is AchievementsPage page)
                Page = page;
            if (component is AchievementArtworkDialog dialog)
                Dialog = dialog;
            if (component is ObsidianDialogScope scope)
                Scope = scope;

            return component;
        }
    }

    /// <summary>
    /// Supplies inert browser interop while exercising production server-rendered component callbacks.
    /// </summary>
    private sealed class DetachedRuntime : IJSRuntime
    {
        /// <summary>
        /// Completes a browser invocation without opening a native window.
        /// </summary>
        /// <typeparam name="TValue">The requested result type.</typeparam>
        /// <param name="identifier">The requested browser function.</param>
        /// <param name="args">The function arguments.</param>
        /// <returns>A completed browser result.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        /// <summary>
        /// Completes a cancellable browser invocation without opening a native window.
        /// </summary>
        /// <typeparam name="TValue">The requested result type.</typeparam>
        /// <param name="identifier">The requested browser function.</param>
        /// <param name="cancellationToken">The caller's cancellation token.</param>
        /// <param name="args">The function arguments.</param>
        /// <returns>A completed browser result.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);
    }

}
