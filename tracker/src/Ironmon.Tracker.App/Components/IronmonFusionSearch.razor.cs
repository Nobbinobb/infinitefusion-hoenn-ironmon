using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Searches normal fusion materials and calculates their seeded Ironmon outcomes.
/// </summary>
public partial class IronmonFusionSearch
{
    private const int SearchPageSize = 20;
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private IReadOnlyList<FusionOutcomeSnapshot> _outcomes = [];
    private string _query = string.Empty;
    private string? _error;
    private string? _pokemonKey;
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
    /// Gets or sets the normal Pokemon whose fusion results are being explored.
    /// </summary>
    [Parameter]
    public PokemonLookupSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload Recipe { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a related Pokemon is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> PokemonSelected { get; set; }

    /// <summary>
    /// Clears search state when the primary material changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_pokemonKey == Pokemon.SpeciesId)
            return;

        _pokemonKey = Pokemon.SpeciesId;
        _matches = [];
        _outcomes = [];
        _query = string.Empty;
        _error = null;
        _searchOffset = 0;
        _matchTotal = 0;
        _searched = false;
    }

    /// <summary>
    /// Starts a material search when Enter is pressed.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    private async Task HandleSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
            await SearchAsync();
    }

    /// <summary>
    /// Starts a normal-species material search at the first page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task SearchAsync() 
        => SearchPageAsync(0);

    /// <summary>
    /// Searches one page of normal-species fusion materials.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the search.</returns>
    private async Task SearchPageAsync(int offset)
    {
        if (_loading)
            return;

        if (string.IsNullOrWhiteSpace(_query))
        {
            _error = "Enter a Pokémon name to search.";
            return;
        }

        _loading = true;
        _searched = false;
        _error = null;
        _outcomes = [];
        try
        {
            PokemonSearchResponsePayload response = await Connection.SearchPokemonAsync(Recipe, _query.Trim(), offset, SearchPageSize, true);
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
    /// Calculates both Ironmon fusion orientations for the selected material.
    /// </summary>
    /// <param name="match">The selected normal Pokemon.</param>
    /// <returns>A task representing the calculation.</returns>
    private async Task SelectMaterialAsync(PokemonSearchMatch match)
    {
        if (_loading)
            return;

        _loading = true;
        _error = null;
        try
        {
            FusionPreviewResponsePayload response = await Connection.PreviewFusionAsync(Recipe, Pokemon.SpeciesId, match.SpeciesId);
            _outcomes = response.Outcomes;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _outcomes = [];
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Gets whether a previous material page is available.
    /// </summary>
    /// <returns>True when the current page does not begin at the first match.</returns>
    private bool HasPreviousPage() 
        => _searchOffset > 0;

    /// <summary>
    /// Gets whether a subsequent material page is available.
    /// </summary>
    /// <returns>True when matches remain after the current page.</returns>
    private bool HasNextPage() 
        => _searchOffset + _matches.Count < _matchTotal;

    /// <summary>
    /// Loads the previous material page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task PreviousPageAsync() 
        => SearchPageAsync(Math.Max(0, _searchOffset - SearchPageSize));

    /// <summary>
    /// Loads the next material page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task NextPageAsync() 
        => SearchPageAsync(_searchOffset + SearchPageSize);

    /// <summary>
    /// Formats the visible inclusive material-search range.
    /// </summary>
    /// <returns>The result range and total.</returns>
    private string GetRangeText()
    {
        int first = _matches.Count == 0 ? 0 : _searchOffset + 1;
        int last = _searchOffset + _matches.Count;
        return $"{first}–{last} of {_matchTotal}";
    }
}
