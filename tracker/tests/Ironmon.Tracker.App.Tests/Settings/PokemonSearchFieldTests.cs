using System.Reflection;
using System.Resources;
using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Covers suggestion limits, selection, and overlapping asynchronous searches.
/// </summary>
public sealed class PokemonSearchFieldTests
{
    private const string _inputAction = "HandleInputAsync";
    private const string _keyAction = "HandleKeyDownAsync";
    private const string _renderAction = "StateHasChanged";
    private const string _inputField = "_input";
    private const string _inputId = "search-test-input";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _arrowDown = "ArrowDown";
    private const string _enter = "Enter";
    private const string _oldQuery = "old";
    private const string _newQuery = "new";

    /// <summary>
    /// Caps suggestions at ten, skips an existing favorite, and clears an accepted search.
    /// </summary>
    /// <returns>A task representing the interactive component checks.</returns>
    [Fact]
    public async Task KeyboardSelectionSkipsExistingFavoritesAndClearsTheSearch()
    {
        IReadOnlyList<PokemonSearchMatch> matches = [.. Enumerable.Range(0, 12).Select(index => new PokemonSearchMatch { SpeciesId = index.ToString(), SpeciesName = $"Pokémon {index}" })];
        PokemonSearchMatch? selected = null;
        await RenderAsync((_, _) => Task.FromResult(matches), async (field, html) =>
        {
            await InvokeAsync(field, _inputAction, new ChangeEventArgs { Value = "P" });
            Assert.Equal(10, System.Text.RegularExpressions.Regex.Matches(html(), "role=\"option\"").Count);
            Assert.Contains("First 10 matches", html());
            Assert.Contains("aria-disabled=\"true\"", html());
            await InvokeAsync(field, _keyAction, new KeyboardEventArgs { Key = _arrowDown });
            await InvokeAsync(field, _keyAction, new KeyboardEventArgs { Key = _enter });
            Assert.Same(matches[1], selected);
            Assert.Contains("value=\"\"", html());
            Assert.DoesNotContain("role=\"option\"", html());
        }, match => selected = match, id => id == matches[0].SpeciesId);
    }

    /// <summary>
    /// Prevents an older response that ignores cancellation from replacing newer suggestions.
    /// </summary>
    /// <returns>A task representing deliberately reordered responses.</returns>
    [Fact]
    public async Task LateResponseCannotReplaceTheLatestQuery()
    {
        TaskCompletionSource<IReadOnlyList<PokemonSearchMatch>> lateResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, CancellationToken, Task<IReadOnlyList<PokemonSearchMatch>>> search = async (query, cancellationToken) =>
        {
            if (query == _oldQuery)
            {
                firstStarted.SetResult();
                return await lateResponse.Task;
            }

            return [new PokemonSearchMatch { SpeciesId = _newQuery, SpeciesName = "New result" }];
        };

        await RenderAsync(search, async (field, html) =>
        {
            Task first = InvokeAsync(field, _inputAction, new ChangeEventArgs { Value = _oldQuery });
            await firstStarted.Task;
            await InvokeAsync(field, _inputAction, new ChangeEventArgs { Value = _newQuery });
            lateResponse.SetResult([new PokemonSearchMatch { SpeciesId = _oldQuery, SpeciesName = "Old result" }]);
            await first;
            Assert.Contains("New result", html());
            Assert.DoesNotContain("Old result", html());
        });
    }

    /// <summary>
    /// Reports a rejected game lookup as a search failure rather than a missing connection.
    /// </summary>
    /// <returns>A task representing the failed request and rendered feedback.</returns>
    [Fact]
    public async Task RejectedLookupDoesNotClaimTheGameIsDisconnected()
    {
        await RenderAsync((_, _) => throw new TrackerProtocolException("Lookup failed."), async (field, html) =>
        {
            await InvokeAsync(field, _inputAction, new ChangeEventArgs { Value = "Lat" });
            Assert.Contains("search failed", html());
            Assert.DoesNotContain("Connect the game", html());
        });
    }

    /// <summary>
    /// Resolves connection-aware failure feedback when the request finishes.
    /// </summary>
    /// <returns>A task representing a disconnection during a pending search.</returns>
    [Fact]
    public async Task FailureFeedbackUsesTheCurrentSourceState()
    {
        const string disconnectedMessage = "Connect the game to search.";
        const string connectedMessage = "Search failed.";
        bool connected = true;
        await RenderAsync((_, _) =>
        {
            connected = false;
            throw new IOException("Connection closed.");
        }, async (field, html) =>
        {
            await InvokeAsync(field, _inputAction, new ChangeEventArgs { Value = "Lat" });
            Assert.Contains(disconnectedMessage, html());
            Assert.DoesNotContain(connectedMessage, html());
        }, getSearchErrorMessage: () => connected ? connectedMessage : disconnectedMessage);
    }

    /// <summary>
    /// Renders the production search field with isolated request and browser dependencies.
    /// </summary>
    /// <param name="search">The controlled asynchronous search source.</param>
    /// <param name="check">The component checks performed on the render dispatcher.</param>
    /// <param name="selected">An optional selection observer.</param>
    /// <param name="isSelected">An optional predicate for existing selections.</param>
    /// <param name="getSearchErrorMessage">An optional provider for current failure feedback.</param>
    /// <returns>A task representing rendering and verification.</returns>
    private static async Task RenderAsync(Func<string, CancellationToken, Task<IReadOnlyList<PokemonSearchMatch>>> search, Func<PokemonSearchField, Func<string>, Task> check, Action<PokemonSearchMatch>? selected = null, Func<string, bool>? isSelected = null, Func<string>? getSearchErrorMessage = null)
    {
        SearchActivator activator = new();
        SettingsJsRuntime javaScript = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime>(javaScript);
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new SearchLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Dictionary<string, object?> parameters = new()
            {
                [nameof(PokemonSearchField.SearchAsync)] = search,
                [nameof(PokemonSearchField.IsSelected)] = isSelected,
                [nameof(PokemonSearchField.GetSearchErrorMessage)] = getSearchErrorMessage,
                [nameof(PokemonSearchField.Selected)] = EventCallback.Factory.Create<PokemonSearchMatch>(activator, match => selected?.Invoke(match))
            };
            var view = await renderer.RenderComponentAsync<PokemonSearchField>(ParameterView.FromDictionary(parameters));
            typeof(PokemonSearchField).GetField(_inputField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(activator.Field, new ElementReference(_inputId, new WebElementReferenceContext(javaScript)));
            await check(activator.Field!, view.ToHtmlString);
        });
    }

    /// <summary>
    /// Dispatches an input handler and refreshes its rendered output.
    /// </summary>
    /// <param name="field">The rendered production component.</param>
    /// <param name="action">The input handler name.</param>
    /// <param name="args">The corresponding input event.</param>
    /// <returns>A task representing the handler and render.</returns>
    private static async Task InvokeAsync(PokemonSearchField field, string action, object args)
    {
        if (typeof(PokemonSearchField).GetMethod(action, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, [args]) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(_renderAction, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, null);
    }

    /// <summary>
    /// Captures the real field created by the component renderer.
    /// </summary>
    private sealed class SearchActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the rendered search field.
        /// </summary>
        internal PokemonSearchField? Field { get; private set; }

        /// <summary>
        /// Creates a component and captures the search field when encountered.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The newly created component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is PokemonSearchField field)
                Field = field;

            return component;
        }
    }

    /// <summary>
    /// Resolves production search strings without desktop startup.
    /// </summary>
    private sealed class SearchLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets one localized production string.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets one formatted production string.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Provides the unused enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent cultures should be included.</param>
        /// <returns>An empty sequence because tests resolve individual keys.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
