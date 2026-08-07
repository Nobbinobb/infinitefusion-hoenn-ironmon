using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Coordinates authorized search and generated Pokemon lookup for the active run.
/// </summary>
public partial class DebugActiveRunLookup
{
    private const int SearchPageSize = 20;
    private readonly List<string> _backHistory = [];
    private readonly List<string> _forwardHistory = [];
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private PokemonLookupSnapshot? _lookup;
    private string? _currentSpeciesId;
    private string _query = string.Empty;
    private string? _error;
    private int _searchOffset;
    private int _matchTotal;
    private bool _loading;
    private bool _searched;

    /// <summary>
    /// Gets or initializes the active game connection.
    /// </summary>
    [Inject]
    private TrackerConnectionService Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Starts a search when Enter is pressed in the query field.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <returns>A task representing the search.</returns>
    private async Task HandleSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
            await SearchAsync();
    }

    /// <summary>
    /// Starts a search at the first result page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task SearchAsync()
        => SearchPageAsync(0);

    /// <summary>
    /// Requests one page of active-run matches.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the search.</returns>
    private async Task SearchPageAsync(int offset)
    {
        if (_loading)
            return;

        if (string.IsNullOrWhiteSpace(_query))
        {
            _error = "Enter a Pokemon name to search.";
            return;
        }

        _loading = true;
        _searched = false;
        _error = null;
        _lookup = null;
        if (offset == 0)
        {
            _currentSpeciesId = null;
            _backHistory.Clear();
            _forwardHistory.Clear();
        }

        try
        {
            PokemonSearchResponsePayload response = await Connection.SearchDebugPokemonAsync(_query, offset, SearchPageSize);
            _matches = response.Matches;
            _searchOffset = offset;
            _matchTotal = Math.Max(response.Total, offset + response.Matches.Count);
            _searched = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _matches = [];
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Navigates to one generated Pokemon and records history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokemon identifier.</param>
    /// <returns>A task representing the lookup.</returns>
    private async Task NavigateToPokemonAsync(string speciesId)
    {
        if (_loading || speciesId == _currentSpeciesId)
            return;

        string? previousSpeciesId = _currentSpeciesId;
        if (!await LoadPokemonAsync(speciesId))
            return;

        if (previousSpeciesId is not null)
            _backHistory.Add(previousSpeciesId);
        _forwardHistory.Clear();
    }

    /// <summary>
    /// Navigates backward through active-run lookup history.
    /// </summary>
    /// <returns>A task representing the lookup.</returns>
    private async Task GoBackAsync()
    {
        if (_backHistory.Count == 0)
            return;

        int lastIndex = _backHistory.Count - 1;
        string targetSpeciesId = _backHistory[lastIndex];
        string? currentSpeciesId = _currentSpeciesId;
        if (!await LoadPokemonAsync(targetSpeciesId))
            return;

        _backHistory.RemoveAt(lastIndex);
        if (currentSpeciesId is not null)
            _forwardHistory.Add(currentSpeciesId);
    }

    /// <summary>
    /// Navigates forward through active-run lookup history.
    /// </summary>
    /// <returns>A task representing the lookup.</returns>
    private async Task GoForwardAsync()
    {
        if (_forwardHistory.Count == 0)
            return;

        int lastIndex = _forwardHistory.Count - 1;
        string targetSpeciesId = _forwardHistory[lastIndex];
        string? currentSpeciesId = _currentSpeciesId;
        if (!await LoadPokemonAsync(targetSpeciesId))
            return;

        _forwardHistory.RemoveAt(lastIndex);
        if (currentSpeciesId is not null)
            _backHistory.Add(currentSpeciesId);
    }

    /// <summary>
    /// Loads one active-run Pokemon without changing navigation history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokemon identifier.</param>
    /// <returns>Whether the lookup succeeded.</returns>
    private async Task<bool> LoadPokemonAsync(string speciesId)
    {
        _loading = true;
        _error = null;
        try
        {
            _lookup = await Connection.LookupDebugPokemonAsync(speciesId);
            _currentSpeciesId = _lookup.SpeciesId;
            _matches = [];
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _error = exception.Message;
            return false;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Gets whether an earlier search page exists.
    /// </summary>
    /// <returns>Whether the current page starts after the first match.</returns>
    private bool HasPreviousSearchPage()
        => _searchOffset > 0;

    /// <summary>
    /// Gets whether a later search page exists.
    /// </summary>
    /// <returns>Whether matches remain after the current page.</returns>
    private bool HasNextSearchPage()
        => _searchOffset + _matches.Count < _matchTotal;

    /// <summary>
    /// Loads the previous search page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task PreviousSearchPageAsync()
        => SearchPageAsync(Math.Max(0, _searchOffset - SearchPageSize));

    /// <summary>
    /// Loads the next search page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task NextSearchPageAsync()
        => SearchPageAsync(_searchOffset + SearchPageSize);

    /// <summary>
    /// Formats the inclusive visible result range.
    /// </summary>
    /// <returns>The visible range and total count.</returns>
    private string GetSearchRangeText()
    {
        int first = _matches.Count == 0 ? 0 : _searchOffset + 1;
        int last = _searchOffset + _matches.Count;
        return $"{first}–{last} of {_matchTotal}";
    }
}
