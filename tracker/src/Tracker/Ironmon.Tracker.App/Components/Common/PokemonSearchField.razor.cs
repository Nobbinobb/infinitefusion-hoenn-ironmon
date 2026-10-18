using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Provides bounded asynchronous Pokémon suggestions with mouse and keyboard selection.
/// </summary>
public partial class PokemonSearchField : IDisposable
{
    private const string _idPrefix = "pokemon-suggestions-";
    private const string _optionSeparator = "-option-";
    private const string _initializeFunction = "ironmonSearch.initialize";
    private const string _scrollFunction = "ironmonSearch.scrollActive";
    private const string _arrowDown = "ArrowDown";
    private const string _arrowUp = "ArrowUp";
    private const string _enter = "Enter";
    private const string _escape = "Escape";
    private const int _suggestionLimit = 10;
    private readonly string _listId = _idPrefix + Guid.NewGuid().ToString();
    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private ElementReference _input;
    private string _query = string.Empty;
    private string? _status;
    private int _activeIndex = -1;
    private bool _suggestionsOpen;
    private bool _disposed;
    private bool _scrollActive;
    private bool _hasFocus;

    /// <summary>
    /// Gets the browser runtime for native key handling and option visibility.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or sets the accessible search label and placeholder.
    /// </summary>
    [Parameter]
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the asynchronous source of matching Pokémon.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public Func<string, CancellationToken, Task<IReadOnlyList<PokemonSearchMatch>>> SearchAsync { get; set; } = null!;

    /// <summary>
    /// Gets or sets an optional provider for a search failure message using the source's current state.
    /// </summary>
    [Parameter]
    public Func<string>? GetSearchErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the predicate identifying entries that are already selected.
    /// </summary>
    [Parameter]
    public Func<string, bool>? IsSelected { get; set; }

    /// <summary>
    /// Gets or sets the callback for adding a suggested Pokémon.
    /// </summary>
    [Parameter]
    public EventCallback<PokemonSearchMatch> Selected { get; set; }

    /// <summary>
    /// Gets the currently highlighted suggestion's accessible identifier.
    /// </summary>
    private string? ActiveOptionId => _suggestionsOpen && _activeIndex >= 0 ? GetOptionId(_activeIndex) : null;

    /// <summary>
    /// Initializes native key behavior and keeps the highlighted suggestion visible.
    /// </summary>
    /// <param name="firstRender">Whether the input was just created.</param>
    /// <returns>A task representing browser interaction.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await JavaScript.InvokeVoidAsync(_initializeFunction, _input);

        if (_scrollActive)
        {
            _scrollActive = false;
            await JavaScript.InvokeVoidAsync(_scrollFunction, _input);
        }
    }

    /// <summary>
    /// Debounces partial-name searches and discards stale responses.
    /// </summary>
    /// <param name="args">The updated search text.</param>
    /// <returns>A task representing the latest suggestion request.</returns>
    private async Task HandleInputAsync(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;
        _hasFocus = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        CancellationToken token = _searchCancellation.Token;
        _matches = [];
        _activeIndex = -1;
        _suggestionsOpen = false;
        _status = null;
        if (string.IsNullOrWhiteSpace(_query))
            return;

        _status = Text["Settings.Favorites.Searching"];
        try
        {
            await Task.Delay(250, token);
            IReadOnlyList<PokemonSearchMatch> matches = await SearchAsync(_query.Trim(), token);
            if (token.IsCancellationRequested || _disposed)
                return;

            _matches = [.. matches.Take(_suggestionLimit)];
            _suggestionsOpen = _hasFocus && _matches.Count > 0;
            _status = Text[_matches.Count == _suggestionLimit ? "Redesign.Settings.FirstMatches" : "Redesign.Settings.MatchCount", _matches.Count];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException)
        {
            if (!token.IsCancellationRequested && !_disposed)
                _status = GetSearchErrorMessage?.Invoke() ?? Text["Settings.Favorites.SearchFailed"];
        }
    }

    /// <summary>
    /// Reopens existing suggestions when the search field regains focus.
    /// </summary>
    private void ShowSuggestions()
    {
        _hasFocus = true;
        _suggestionsOpen = _matches.Count > 0;
    }

    /// <summary>
    /// Closes the popup when focus moves away from the input.
    /// </summary>
    private void HideSuggestions()
    {
        _hasFocus = false;
        _suggestionsOpen = false;
    }

    /// <summary>
    /// Moves the active option, accepts a selection, or dismisses suggestions.
    /// </summary>
    /// <param name="args">The key pressed in the search input.</param>
    /// <returns>A task representing optional selection.</returns>
    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == _escape)
        {
            _suggestionsOpen = false;
            return;
        }

        if (args.Key == _enter && _suggestionsOpen && _activeIndex >= 0)
        {
            await SelectAsync(_matches[_activeIndex]);
            return;
        }

        if (args.Key is not (_arrowDown or _arrowUp) || _matches.Count == 0)
            return;

        _suggestionsOpen = true;
        int direction = args.Key == _arrowDown ? 1 : -1;
        int next = _activeIndex < 0 ? (direction > 0 ? 0 : _matches.Count - 1) : _activeIndex + direction;
        while (next >= 0 && next < _matches.Count && IsAlreadySelected(_matches[next]))
            next += direction;

        if (next >= 0 && next < _matches.Count)
        {
            _activeIndex = next;
            _scrollActive = true;
        }
    }

    /// <summary>
    /// Adds one suggestion and resets the input for another search.
    /// </summary>
    /// <param name="match">The chosen Pokémon.</param>
    /// <returns>A task representing selection and focus restoration.</returns>
    private async Task SelectAsync(PokemonSearchMatch match)
    {
        if (IsAlreadySelected(match))
            return;

        _searchCancellation?.Cancel();
        _suggestionsOpen = false;
        _query = string.Empty;
        _matches = [];
        _activeIndex = -1;
        await Selected.InvokeAsync(match);
        if (_disposed)
            return;

        _status = Text["Redesign.Settings.FavoriteAdded", match.SpeciesName];
        await _input.FocusAsync(preventScroll: true);
    }

    /// <summary>
    /// Tests whether a suggestion has already been selected.
    /// </summary>
    /// <param name="match">The suggested Pokémon.</param>
    /// <returns>Whether selection should be disabled.</returns>
    private bool IsAlreadySelected(PokemonSearchMatch match) => IsSelected?.Invoke(match.SpeciesId) == true;

    /// <summary>
    /// Creates an identifier scoped to this input's suggestion list.
    /// </summary>
    /// <param name="index">The suggestion index.</param>
    /// <returns>The stable option identifier.</returns>
    private string GetOptionId(int index) => _listId + _optionSeparator + index;

    /// <summary>
    /// Cancels outstanding work when the field is removed.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }
}
