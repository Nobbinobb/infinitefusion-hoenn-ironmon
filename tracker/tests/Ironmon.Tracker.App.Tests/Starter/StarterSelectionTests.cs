using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Starter;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Live;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Starter;

/// <summary>
/// Verifies starter disclosure, eligibility, and confirmation lifetime in the production components.
/// </summary>
public sealed class StarterSelectionTests
{
    private const string _speciesName = "Cyndaquil";
    private const string _storageName = "IronmonStarterTests";
    private const string _version = "test";
    private const string _publish = "Publish";
    private const string _render = "StateHasChanged";
    private const string _request = "RequestChoice";
    private const string _scene = "run:1";
    private const string _nextScene = "run:2";
    private const string _action = "starter-select-action";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const BindingFlags _instanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Hides stale identity fields and withholds actions unless every starter rule permits selection.
    /// </summary>
    /// <param name="revealed">Whether the game has revealed the slot.</param>
    /// <param name="automatic">Whether automatic-selection restrictions apply.</param>
    /// <param name="favorite">Whether the candidate qualifies for the Favorite Clause.</param>
    /// <param name="random">Whether this slot is the random pick.</param>
    /// <param name="eligible">Whether its BST satisfies the ceiling.</param>
    /// <param name="accepting">Whether the game currently accepts choices.</param>
    /// <param name="expected">Whether the selection action should exist.</param>
    /// <returns>A task representing rendering and confirmation checks.</returns>
    [Theory]
    [InlineData(false, false, true, true, true, true, false)]
    [InlineData(true, false, false, false, true, true, true)]
    [InlineData(true, false, true, true, false, true, false)]
    [InlineData(true, true, false, false, true, true, false)]
    [InlineData(true, true, true, false, true, true, true)]
    [InlineData(true, true, false, true, true, true, true)]
    [InlineData(true, true, true, false, false, true, false)]
    [InlineData(true, true, false, true, true, false, false)]
    public async Task SelectionRespectsDisclosureAndGameRules(bool revealed, bool automatic, bool favorite, bool random, bool eligible, bool accepting, bool expected)
    {
        StarterChoiceSnapshot choice = new() { Index = 0, Revealed = revealed, SpeciesName = _speciesName, Favorite = favorite, BstEligible = eligible, CanSelect = accepting, BaseStatTotal = 309 };
        StarterSelectionSnapshot selection = new() { Active = true, SelectionId = _scene, AutoSelect = automatic, RandomPickIndex = random ? 0 : 1, MaximumBaseStatTotal = 350, Choices = [choice] };
        await RenderAsync<StarterSelection>(new() { [nameof(StarterSelection.Selection)] = selection }, false, async (component, html) =>
        {
            Assert.Equal(expected, html().Contains(_action, StringComparison.Ordinal));
            Assert.Equal(revealed, html().Contains(_speciesName, StringComparison.Ordinal));
            Assert.Equal(revealed, html().Contains("pokemon-sprite", StringComparison.Ordinal));
            await InvokeAsync(component, _request, choice);
            Assert.Equal(expected, html().Contains("role=\"dialog\"", StringComparison.Ordinal));
            if (!expected)
                return;

            await component.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(StarterSelection.Selection)] = new StarterSelectionSnapshot { Active = true, SelectionId = _nextScene, Choices = [choice] } }));
            Assert.DoesNotContain("role=\"dialog\"", html());
        });
    }

    /// <summary>
    /// Renders real components with an isolated request client that cannot contact a game.
    /// </summary>
    /// <typeparam name="T">The component under test.</typeparam>
    /// <param name="parameters">The component inputs.</param>
    /// <param name="authorized">Whether the detached connection grants development access.</param>
    /// <param name="check">The checks performed on the render dispatcher.</param>
    /// <returns>A task representing rendering and checks.</returns>
    private static async Task RenderAsync<T>(Dictionary<string, object?> parameters, bool authorized, Func<T, Func<string>, Task> check) where T : IComponent
    {
        string directory = Path.Combine(Path.GetTempPath(), _storageName, Guid.NewGuid().ToString());
        TrackerKnowledgeOptions storage = new(directory);
        TrackerConnectionState state = new();
        GameHandshakePayload game = new(_version, _version, true, authorized, directory, null, null);
        typeof(TrackerConnectionState).GetMethod(_publish, _instanceMembers)!.Invoke(state, [TrackerConnectionStatus.Connected, game, null, null]);
        await using TrackerConnectionService connection = new(new(0, _version, authorized, TimeSpan.FromSeconds(1)), new(), state, new(), new(storage), new(storage), new(storage));
        StarterActivator<T> activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(connection.Requests);
        services.AddSingleton(state);
        services.AddSingleton(new PokemonSpriteDialogService());
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, StarterLocalizer>();
        try
        {
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
                await check(activator.Component!, view.ToHtmlString);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Invokes a component interaction and updates its rendered state on the dispatcher.
    /// </summary>
    /// <param name="component">The production component.</param>
    /// <param name="method">The interaction method.</param>
    /// <param name="arguments">The interaction arguments.</param>
    /// <returns>A task representing the interaction and render.</returns>
    private static async Task InvokeAsync(IComponent component, string method, params object[] arguments)
    {
        if (component.GetType().GetMethod(method, _instanceMembers)!.Invoke(component, arguments) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(_render, _instanceMembers)!.Invoke(component, null);
    }

    /// <summary>
    /// Captures the real component instance created by the renderer.
    /// </summary>
    /// <typeparam name="T">The component to capture.</typeparam>
    private sealed class StarterActivator<T> : IComponentActivator where T : IComponent
    {
        /// <summary>
        /// Gets the captured production component.
        /// </summary>
        internal T? Component { get; private set; }

        /// <summary>
        /// Creates the requested component and captures the matching instance.
        /// </summary>
        /// <param name="componentType">The component type requested by the renderer.</param>
        /// <returns>The new component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is T match)
                Component = match;

            return component;
        }
    }

    /// <summary>
    /// Resolves production research strings without desktop startup.
    /// </summary>
    private sealed class StarterLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the localized value for one key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets the formatted localized value for one key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns the unused enumeration contract for these key-based tests.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent resources should be included.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
