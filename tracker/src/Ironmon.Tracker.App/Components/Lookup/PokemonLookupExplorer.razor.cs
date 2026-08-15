using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates generated Pokémon search, paging, lookup, and navigation history.
/// </summary>
public partial class PokemonLookupExplorer : IDisposable
{
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
    private string? _observedRequestedSpeciesId;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned diagnostic access.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether requests use the active debug run.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets a species requested by a containing navigation surface.
    /// </summary>
    [Parameter]
    public string? RequestedSpeciesId { get; set; }

    /// <summary>
    /// Subscribes active-run lookup state to diagnostic-access changes.
    /// </summary>
    protected override void OnInitialized()
        => AccessService.Changed += HandleDiagnosticAccessChanged;

    /// <summary>
    /// Opens a newly requested species without adding an artificial history entry.
    /// </summary>
    /// <returns>A task representing the requested lookup.</returns>
    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrWhiteSpace(RequestedSpeciesId) || RequestedSpeciesId == _observedRequestedSpeciesId)
            return;

        _observedRequestedSpeciesId = RequestedSpeciesId;
        await LoadPokemonAsync(RequestedSpeciesId);
    }

    /// <summary>
    /// Gets the message shown while generated data is loading.
    /// </summary>
    private string LoadingMessage => DebugMode ? Text["Lookup.Search.ReadingActiveGeneratedData"] : Text["Lookup.Search.ReconstructingGeneratedData"];

    /// <summary>
    /// Starts a search when Enter is pressed in the query field.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <returns>A task representing the search.</returns>
    private async Task HandleSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == TrackerKeyboardKeys.Enter)
            await SearchAsync();
    }

    /// <summary>
    /// Starts a search at the first result page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task SearchAsync()
        => SearchPageAsync(0);

    /// <summary>
    /// Requests one page of generated Pokémon matches.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the search.</returns>
    private async Task SearchPageAsync(int offset)
    {
        if (_loading)
            return;

        string query = _query.Trim();
        if (query.Length == 0)
        {
            _error = Text["Lookup.Search.EnterPokemonName"];
            return;
        }

        if (!DebugMode && Recipe is null)
            return;

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
            PokemonSearchResponsePayload response = await SearchPokemonAsync(query, offset);
            _matches = response.Matches;
            _searchOffset = offset;
            _matchTotal = Math.Max(response.Total, offset + response.Matches.Count);
            _searched = true;
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
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
    /// Sends a search request using the configured lookup source.
    /// </summary>
    /// <param name="query">The trimmed name query.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>The matching generated Pokémon.</returns>
    private Task<PokemonSearchResponsePayload> SearchPokemonAsync(string query, int offset)
    {
        return DebugMode
            ? Connection.SearchDebugPokemonAsync(query, offset, TrackerProtocol.DefaultSearchPageSize)
            : Connection.SearchPokemonAsync(Recipe!, query, offset, TrackerProtocol.DefaultSearchPageSize);
    }

    /// <summary>
    /// Navigates to one generated Pokémon and records history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokémon identifier.</param>
    /// <returns>A task representing the lookup.</returns>
    private async Task NavigateToPokemonAsync(string speciesId)
    {
        if (_loading || speciesId == _currentSpeciesId)
            return;

        string? previousSpeciesId = _currentSpeciesId;
        PokemonSearchMatch? match = _matches.FirstOrDefault(candidate => candidate.SpeciesId == speciesId);
        if (!await LoadPokemonAsync(speciesId, match))
            return;

        if (previousSpeciesId is not null)
            _backHistory.Add(previousSpeciesId);
        _forwardHistory.Clear();
    }

    /// <summary>
    /// Navigates to one selected search match and records history.
    /// </summary>
    /// <param name="match">The selected Pokemon search match.</param>
    /// <returns>A task representing the lookup.</returns>
    private Task NavigateToPokemonAsync(PokemonSearchMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return NavigateToPokemonAsync(match.SpeciesId);
    }

    /// <summary>
    /// Navigates backward through lookup history.
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
    /// Navigates forward through lookup history.
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
    /// Loads one generated Pokémon without changing navigation history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokémon identifier.</param>
    /// <param name="match">The originating search match when one is available.</param>
    /// <returns>Whether the lookup succeeded.</returns>
    private async Task<bool> LoadPokemonAsync(string speciesId, PokemonSearchMatch? match = null)
    {
        if (_loading || !DebugMode && Recipe is null)
            return false;

        _loading = true;
        _error = null;
        try
        {
            PokemonLookupSection? section = GetFirstAuthorizedLookupSection();
            if (!DebugMode || section is not null)
            {
                _lookup = await LookupPokemonAsync(speciesId, section ?? PokemonLookupSection.Overview);
            }
            else if (match is not null)
            {
                PokemonInformationPage page = TrackerDiagnosticCapabilityRules.HasAnyOverviewSurface(Connection)
                    ? PokemonInformationPage.Overview
                    : PokemonInformationPage.Evolutions;
                _lookup = new PokemonLookupSnapshot
                {
                    Identity = new PokemonLookupIdentitySnapshot
                    {
                        SpeciesId = match.SpeciesId,
                        SpeciesName = match.SpeciesName,
                        Fusion = match.Fusion
                    },
                    Section = (PokemonLookupSection)(int)page
                };
            }
            else
            {
                throw new InvalidOperationException(Text["Lookup.Search.SelectPokemonFromResults"]);
            }

            _currentSpeciesId = _lookup.Identity.SpeciesId;
            _matches = [];
            return true;
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
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
    /// Sends a lookup request using the configured lookup source.
    /// </summary>
    /// <param name="speciesId">The stable Pokémon identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <returns>The generated Pokémon snapshot.</returns>
    private Task<PokemonLookupSnapshot> LookupPokemonAsync(string speciesId, PokemonLookupSection section)
        => DebugMode ? Connection.LookupDebugPokemonAsync(speciesId, section) : Connection.LookupPokemonAsync(Recipe!, speciesId, section);

    /// <summary>
    /// Gets the first active-run information section that may be requested directly.
    /// </summary>
    /// <returns>The first authorized section, or null when only independent tools are granted.</returns>
    private PokemonLookupSection? GetFirstAuthorizedLookupSection()
    {
        if (!DebugMode)
            return PokemonLookupSection.Overview;

        foreach (PokemonInformationPage page in Enum.GetValues<PokemonInformationPage>())
        {
            string capability = TrackerDiagnosticCapabilityRules.GetPokemonInformationCapability(page);
            if (Connection.HasDiagnosticCapability(capability))
                return (PokemonLookupSection)(int)page;
        }

        return null;
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
        => SearchPageAsync(Math.Max(0, _searchOffset - TrackerProtocol.DefaultSearchPageSize));

    /// <summary>
    /// Loads the next search page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task NextSearchPageAsync()
        => SearchPageAsync(_searchOffset + TrackerProtocol.DefaultSearchPageSize);

    /// <summary>
    /// Formats the inclusive visible result range.
    /// </summary>
    /// <returns>The visible range and total count.</returns>
    private string GetSearchRangeText()
    {
        int first = _matches.Count == 0 ? 0 : _searchOffset + 1;
        int last = _searchOffset + _matches.Count;
        return Text["Lookup.Search.ResultRange", first, last, _matchTotal];
    }

    /// <summary>
    /// Gets whether an exception represents an expected request failure.
    /// </summary>
    /// <param name="exception">The request exception.</param>
    /// <returns>Whether the exception can be displayed as a lookup error.</returns>
    private static bool IsExpectedRequestException(Exception exception)
        => exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException;

    /// <summary>
    /// Clears protected active-run lookup data immediately after access changes.
    /// </summary>
    /// <param name="sender">The diagnostic-access service.</param>
    /// <param name="args">The empty change arguments.</param>
    private void HandleDiagnosticAccessChanged(object? sender, EventArgs args)
    {
        if (!DebugMode)
            return;

        _matches = [];
        _lookup = null;
        _currentSpeciesId = null;
        _backHistory.Clear();
        _forwardHistory.Clear();
        _error = null;
        _searched = false;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes the diagnostic-access subscription.
    /// </summary>
    public void Dispose()
        => AccessService.Changed -= HandleDiagnosticAccessChanged;
}
