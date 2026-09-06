using System.Reflection;
using System.Resources;
using Ironmon.Tracker.App.Components.Seeds;
using Ironmon.Tracker.App.Seeds;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Seeds;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Tests.Seeds;

/// <summary>
/// Verifies real token export, review, cancellation, and failure feedback in both presentation modes.
/// </summary>
public sealed class SeededRunViewTests
{
    private const string _create = "CreateAsync";
    private const string _copy = "CopyAsync";
    private const string _update = "UpdateImportToken";
    private const string _validate = "ValidateImportAsync";
    private const string _close = "CloseImportDialog";
    private const string _confirm = "ConfirmImportAsync";
    private const string _render = "StateHasChanged";
    private const string _dialog = "role=\"dialog\"";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _storageName = "IronmonSharingViewTests";
    private const string _runId = "sharing-test-run";
    private const string _tokenId = "sharing-test-token";
    private const string _version = "0.8.5";
    private const string _gameVersion = "6.8.2";
    private const string _profileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string _species = "species";
    private const string _abilities = "abilities";
    private const string _fusions = "fusions";
    private const string _clipboard = "navigator.clipboard.writeText";
    private const string _active = "active";
    private const string _mixed = "mixed";
    private const string _playerChoice = "player_choice";

    /// <summary>
    /// Keeps export explicit and shows the same token dialog for compact and full export.
    /// </summary>
    /// <param name="compact">Whether export uses the compact archive action.</param>
    /// <returns>A task representing export and clipboard verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportWaitsForRecipeAndCopiesAValidToken(bool compact)
    {
        TaskCompletionSource<RunReproductionRecipePayload> recipe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        Dictionary<string, object?> parameters = new()
        {
            [nameof(SeedTokenExportPanel.Compact)] = compact,
            [nameof(SeedTokenExportPanel.Title)] = "Export current run",
            [nameof(SeedTokenExportPanel.Description)] = "Share the current world.",

            [nameof(SeedTokenExportPanel.RecipeProvider)] = (Func<Task<RunReproductionRecipePayload>>)(() => { requests++; return recipe.Task; })
        };

        await RenderAsync<SeedTokenExportPanel>(parameters, async (panel, html, state, js, codec) =>
        {
            Assert.Equal(0, requests);
            Assert.DoesNotContain(_dialog, html());
            Task creating = InvokeAsync(panel, _create);
            Assert.Equal(1, requests);
            Assert.DoesNotContain(_dialog, html());
            recipe.SetResult(CreateRecipe());
            await creating;
            Assert.Contains(_dialog, html());
            await InvokeAsync(panel, _copy);
            SeedTokenValidationResult result = await codec.ValidateAsync(Assert.IsType<string>(js.Copied));
            Assert.True(result.IsValid);
            Assert.Equal(581855396, result.Data?.Seed);
            Assert.Contains("Seed token copied.", html());
        });
    }

    /// <summary>
    /// Requires explicit confirmation after validation and keeps connection failures visible in the review dialog.
    /// </summary>
    /// <returns>A task representing review, cancellation, and a failed submission.</returns>
    [Fact]
    public async Task ImportReviewCanBeCancelledAndDoesNotHideSubmissionErrors()
    {
        await RenderAsync<SeededRunPage>([], async (page, html, state, js, codec) =>
        {
            string token = codec.Create(CreateRecipe(), _tokenId, DateTimeOffset.UtcNow);
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = token });
            await InvokeAsync(page, _validate);
            Assert.Contains(_dialog, html());
            Assert.Contains("581855396", html());
            Assert.Contains(_gameVersion, html());
            Assert.Contains("This action cannot be undone.", html());
            await InvokeAsync(page, _close);
            Assert.DoesNotContain(_dialog, html());
            Assert.Contains(token, html());
            await InvokeAsync(page, _validate);
            await InvokeAsync(page, _confirm);
            Assert.Contains(_dialog, html());
            Assert.Contains("The game did not receive the import request.", html());
            Assert.Contains("The current attempt was not changed.", html());
        });
    }

    /// <summary>
    /// Rejects invalid tokens inline and blocks confirmation after the game disconnects.
    /// </summary>
    /// <returns>A task representing validation and connection transitions.</returns>
    [Fact]
    public async Task InvalidAndDisconnectedImportsCannotAdvance()
    {
        await RenderAsync<SeededRunPage>([], async (page, html, state, js, codec) =>
        {
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = "invalid" });
            await InvokeAsync(page, _validate);
            Assert.DoesNotContain(_dialog, html());
            Assert.Contains("malformed", html());
            string token = codec.Create(CreateRecipe(), _tokenId, DateTimeOffset.UtcNow);
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = token });
            await InvokeAsync(page, _validate);
            state.Publish(TrackerConnectionStatus.Waiting);
            await InvokeAsync(page, _confirm);
            Assert.Contains("Connect an active Ironmon run", html());
            Assert.DoesNotContain("The game did not receive", html());
            await InvokeAsync(page, _close);
            await InvokeAsync(page, _validate);
            Assert.DoesNotContain(_dialog, html());
        });
    }

    /// <summary>
    /// Creates compatible deterministic inputs without accessing a personal save.
    /// </summary>
    /// <returns>A recipe accepted by the production token codec.</returns>
    private static CompletedRunRecipePayload CreateRecipe() => new()
    {
        RunId = _runId,
        Result = _active,
        Seed = 581855396,
        GenerationProfileId = _profileId,
        GameVersion = _gameVersion,
        IronmonVersion = _version,
        Configuration = new() { SchemaVersion = SeedTokenConstants.ConfigurationSchemaVersion, WildPolicy = _mixed, TrainerPolicy = _mixed, UnfusionSetting = _playerChoice },
        SpeciesGenerator = new() { Version = 1, PoolFingerprint = _species },
        AbilityGenerator = new() { Version = 1, PoolSize = 310, PoolFingerprint = _abilities },
        PlayerFusionGenerator = new() { Version = 1, PoolSize = 174348, PoolFingerprint = _fusions }
    };

    /// <summary>
    /// Renders a production component with a real codec and an unattached request session.
    /// </summary>
    /// <typeparam name="T">The component under test.</typeparam>
    /// <param name="parameters">The component parameters.</param>
    /// <param name="check">The checks executed on the rendering dispatcher.</param>
    /// <returns>A task representing rendering and verification.</returns>
    private static async Task RenderAsync<T>(Dictionary<string, object?> parameters, Func<T, Func<string>, TrackerConnectionState, BrowserRuntime, SeedTokenCodec, Task> check) where T : IComponent
    {
        TrackerConnectionState state = new();
        state.Publish(TrackerConnectionStatus.Connected, currentState: new GameCurrentStatePayload(true, _runId, null, 0));
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _storageName, Guid.NewGuid().ToString()));
        TrackerRequestClient requests = new(session, new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), state, new AreaDiscoveryStore(storage));
        CapturingActivator<T> activator = new();
        BrowserRuntime js = new();
        SeedTokenCodec codec = new(SeedTokenSharedKey.Material);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime>(js);
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new Localizer());
        services.AddSingleton(state);
        services.AddSingleton(requests);
        services.AddSingleton(codec);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SeedTokenFileSaver>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            await check(activator.Component!, view.ToHtmlString, state, js, codec);
        });
    }

    /// <summary>
    /// Invokes a production event handler and refreshes its markup.
    /// </summary>
    /// <param name="component">The rendered component.</param>
    /// <param name="method">The handler name.</param>
    /// <param name="arguments">The handler arguments.</param>
    /// <returns>A task representing the event and render.</returns>
    private static async Task InvokeAsync(IComponent component, string method, params object[] arguments)
    {
        if (component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(_render, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
    }

    /// <summary>
    /// Captures the requested component while allowing shared components to render normally.
    /// </summary>
    /// <typeparam name="T">The component to capture.</typeparam>
    private sealed class CapturingActivator<T> : IComponentActivator where T : IComponent
    {
        /// <summary>
        /// Gets the component created by the renderer.
        /// </summary>
        public T? Component { get; private set; }

        /// <summary>
        /// Creates and optionally captures a component.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The newly created component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is T target)
                Component = target;

            return component;
        }
    }

    /// <summary>
    /// Captures clipboard writes without accessing the user's clipboard.
    /// </summary>
    private sealed class BrowserRuntime : IJSRuntime
    {
        /// <summary>
        /// Gets the last copied token.
        /// </summary>
        public string? Copied { get; private set; }

        /// <summary>
        /// Records clipboard arguments and supplies harmless browser results.
        /// </summary>
        /// <typeparam name="TValue">The requested result type.</typeparam>
        /// <param name="identifier">The browser operation.</param>
        /// <param name="args">The supplied arguments.</param>
        /// <returns>The default browser result.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == _clipboard)
                Copied = (string?)args?[0];

            return ValueTask.FromResult(default(TValue)!);
        }

        /// <summary>
        /// Delegates the cancellation-aware browser contract.
        /// </summary>
        /// <typeparam name="TValue">The requested result type.</typeparam>
        /// <param name="identifier">The browser operation.</param>
        /// <param name="cancellationToken">The operation cancellation token.</param>
        /// <param name="args">The supplied arguments.</param>
        /// <returns>The default browser result.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    /// <summary>
    /// Resolves production text without desktop startup.
    /// </summary>
    private sealed class Localizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets one production string.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets one formatted production string.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Supplies the unused enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether to include parent cultures.</param>
        /// <returns>An empty sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
