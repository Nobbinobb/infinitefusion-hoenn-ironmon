using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Coordinates completed-run selection, name search, and deterministic Pokemon lookup.
/// </summary>
public partial class PostRunLookup : IDisposable
{
    private const int SearchPageSize = 20;
    private IReadOnlyList<CompletedRunRecipePayload> _recipes = [];
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private readonly List<string> _backHistory = [];
    private readonly List<string> _forwardHistory = [];
    private PokemonLookupSnapshot? _lookup;
    private string? _currentSpeciesId;
    private string? _selectedRunId;
    private string _query = string.Empty;
    private string? _error;
    private int _searchOffset;
    private int _matchTotal;
    private bool _loading;
    private bool _searched;

    /// <summary>
    /// Gets or initializes the completed-run recipe archive.
    /// </summary>
    [Inject]
    private CompletedRunArchive CompletedRuns { get; set; } = null!;

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
    /// Loads available recipes and subscribes to archive changes.
    /// </summary>
    protected override void OnInitialized()
    {
        RefreshRecipes();
        CompletedRuns.Changed += HandleCompletedRunsChanged;
    }

    /// <summary>
    /// Selects a completed run and clears results from the previous recipe.
    /// </summary>
    /// <param name="args">The select element change.</param>
    private void SelectRun(ChangeEventArgs args)
    {
        _selectedRunId = args.Value?.ToString();
        _matches = [];
        _lookup = null;
        _error = null;
        _searchOffset = 0;
        _matchTotal = 0;
        _searched = false;
        _currentSpeciesId = null;
        _backHistory.Clear();
        _forwardHistory.Clear();
    }

    /// <summary>
    /// Starts a search when Enter is pressed in the query field.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    private async Task HandleSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
            await SearchAsync();
    }

    /// <summary>
    /// Requests matching Pokemon identifiers from the connected game.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task SearchAsync() 
        => SearchPageAsync(0);

    /// <summary>
    /// Requests one page of matching Pokemon identifiers from the connected game.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the search.</returns>
    private async Task SearchPageAsync(int offset)
    {
        if (_loading)
            return;

        CompletedRunRecipePayload? recipe = GetSelectedRecipe();
        if (recipe is null)
            return;

        if (string.IsNullOrWhiteSpace(_query))
        {
            _error = "Enter a Pokémon name to search.";
            return;
        }

        _loading = true;
        _searched = false;
        _error = null;
        _lookup = null;
        try
        {
            PokemonSearchResponsePayload response = await Connection.SearchPokemonAsync(recipe, _query.Trim(), offset, SearchPageSize);
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
    /// Requests complete deterministic information for one stable Pokemon identifier.
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
    /// Navigates to the previous Pokemon in this lookup session.
    /// </summary>
    /// <returns>A task representing the navigation.</returns>
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
    /// Navigates to the next Pokemon in this lookup session.
    /// </summary>
    /// <returns>A task representing the navigation.</returns>
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
    /// Requests complete deterministic information without changing history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokemon identifier.</param>
    /// <returns>True when the lookup succeeded.</returns>
    private async Task<bool> LoadPokemonAsync(string speciesId)
    {
        if (_loading)
            return false;

        CompletedRunRecipePayload? recipe = GetSelectedRecipe();
        if (recipe is null)
            return false;

        _loading = true;
        _error = null;
        try
        {
            _lookup = await Connection.LookupPokemonAsync(recipe, speciesId);
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
    /// Gets whether a previous search page is available.
    /// </summary>
    /// <returns>True when the current page does not begin at the first match.</returns>
    private bool HasPreviousSearchPage() 
        => _searchOffset > 0;

    /// <summary>
    /// Gets whether a subsequent search page is available.
    /// </summary>
    /// <returns>True when matches remain after the current page.</returns>
    private bool HasNextSearchPage() 
        => _searchOffset + _matches.Count < _matchTotal;

    /// <summary>
    /// Loads the previous page of search matches.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task PreviousSearchPageAsync() 
        => SearchPageAsync(Math.Max(0, _searchOffset - SearchPageSize));

    /// <summary>
    /// Loads the next page of search matches.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task NextSearchPageAsync() 
        => SearchPageAsync(_searchOffset + SearchPageSize);

    /// <summary>
    /// Formats the visible inclusive search-result range.
    /// </summary>
    /// <returns>The result range and total.</returns>
    private string GetSearchRangeText()
    {
        int first = _matches.Count == 0 ? 0 : _searchOffset + 1;
        int last = _searchOffset + _matches.Count;
        return $"{first}–{last} of {_matchTotal}";
    }

    /// <summary>
    /// Gets the currently selected completed-run recipe.
    /// </summary>
    /// <returns>The selected recipe or null.</returns>
    private CompletedRunRecipePayload? GetSelectedRecipe() =>
        _recipes.FirstOrDefault(recipe => recipe.RunId == _selectedRunId);

    /// <summary>
    /// Formats one completed-run selection label.
    /// </summary>
    /// <param name="recipe">The completed-run recipe.</param>
    /// <returns>The concise run label.</returns>
    private static string FormatRun(CompletedRunRecipePayload recipe) =>
        $"{recipe.Result} · Seed {recipe.Seed}";

    /// <summary>
    /// Reloads recipes while retaining a still-valid selection.
    /// </summary>
    private void RefreshRecipes()
    {
        _recipes = CompletedRuns.Recipes;
        if (_selectedRunId is null || _recipes.All(recipe => recipe.RunId != _selectedRunId))
            _selectedRunId = _recipes.Count > 0 ? _recipes[0].RunId : null;
    }

    /// <summary>
    /// Refreshes the component after a completed-run recipe is stored.
    /// </summary>
    /// <param name="sender">The archive raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleCompletedRunsChanged(object? sender, EventArgs args)
    {
        RefreshRecipes();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes the completed-run archive subscription.
    /// </summary>
    public void Dispose() 
        => CompletedRuns.Changed -= HandleCompletedRunsChanged;
}
