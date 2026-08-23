using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates generated Pokémon search, paging, lookup, and navigation history.
/// </summary>
public partial class PokemonLookupExplorer : IDisposable
{
    private const string _lookupRequestKey = "lookup";
    private readonly NavigationHistory<string> _history = new();
    private readonly PaginationState _searchPagination = new(TrackerProtocol.DefaultSearchPageSize);
    private readonly LatestRequestCoordinator<string> _requests = new();
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private PokemonLookupSnapshot? _lookup;
    private string? _currentSpeciesId;
    private string _query = string.Empty;
    private string? _error;
    private int _matchTotal;
    private bool _loading;
    private bool _searched;
    private bool _observedDebugMode;
    private CompletedRunRecipePayload? _observedRecipe;
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
        bool sourceChanged = _observedDebugMode != DebugMode || !ReferenceEquals(_observedRecipe, Recipe);
        if (sourceChanged)
        {
            _observedDebugMode = DebugMode;
            _observedRecipe = Recipe;
            _observedRequestedSpeciesId = null;
            CancelRequest();
            _matches = [];
            _lookup = null;
            _currentSpeciesId = null;
            _history.Clear();
            _searchPagination.Reset();
            _matchTotal = 0;
            _error = null;
            _searched = false;
        }

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
    /// <param name="pageIndex">The zero-based result page.</param>
    /// <returns>A task representing the search.</returns>
    private async Task SearchPageAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        string query = _query.Trim();
        if (query.Length == 0)
        {
            _error = Text["Lookup.Search.EnterPokemonName"];
            return;
        }

        if (!DebugMode && Recipe is null)
            return;

        LatestRequestLease<string> request = BeginRequest();
        _searched = false;
        _error = null;
        _lookup = null;
        if (pageIndex == 0)
        {
            _currentSpeciesId = null;
            _history.Clear();
        }

        try
        {
            int offset = checked(pageIndex * _searchPagination.PageSize);
            PokemonSearchResponsePayload response = await SearchPokemonAsync(query, offset, request.CancellationToken);
            if (!request.IsCurrent)
                return;

            _matches = response.Matches;
            _searchPagination.Select(pageIndex);
            _matchTotal = Math.Max(response.Total, offset + response.Matches.Count);
            _searched = true;
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            if (request.IsCurrent)
            {
                _matches = [];
                _error = exception.Message;
            }
        }
        finally
        {
            CompleteRequest(request);
        }
    }

    /// <summary>
    /// Sends a search request using the configured lookup source.
    /// </summary>
    /// <param name="query">The trimmed name query.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels a replaced request.</param>
    /// <returns>The matching generated Pokémon.</returns>
    private Task<PokemonSearchResponsePayload> SearchPokemonAsync(string query, int offset, CancellationToken cancellationToken)
    {
        return DebugMode
            ? Connection.SearchDebugPokemonAsync(query, offset, TrackerProtocol.DefaultSearchPageSize, cancellationToken: cancellationToken)
            : Connection.SearchPokemonAsync(Recipe!, query, offset, TrackerProtocol.DefaultSearchPageSize, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Navigates to one generated Pokémon and records history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokémon identifier.</param>
    /// <returns>A task representing the lookup.</returns>
    private async Task NavigateToPokemonAsync(string speciesId)
    {
        if (speciesId == _currentSpeciesId)
            return;

        string? previousSpeciesId = _currentSpeciesId;
        PokemonSearchMatch? match = _matches.FirstOrDefault(candidate => candidate.SpeciesId == speciesId);
        if (!await LoadPokemonAsync(speciesId, match))
            return;

        if (previousSpeciesId is not null)
            _history.RecordNavigation(previousSpeciesId);
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
        if (!_history.TryPeekBack(out string? targetSpeciesId) || _currentSpeciesId is not string currentSpeciesId)
            return;

        if (!await LoadPokemonAsync(targetSpeciesId))
            return;

        _history.CommitBack(currentSpeciesId);
    }

    /// <summary>
    /// Navigates forward through lookup history.
    /// </summary>
    /// <returns>A task representing the lookup.</returns>
    private async Task GoForwardAsync()
    {
        if (!_history.TryPeekForward(out string? targetSpeciesId) || _currentSpeciesId is not string currentSpeciesId)
            return;

        if (!await LoadPokemonAsync(targetSpeciesId))
            return;

        _history.CommitForward(currentSpeciesId);
    }

    /// <summary>
    /// Loads one generated Pokémon without changing navigation history.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokémon identifier.</param>
    /// <param name="match">The originating search match when one is available.</param>
    /// <returns>Whether the lookup succeeded.</returns>
    private async Task<bool> LoadPokemonAsync(string speciesId, PokemonSearchMatch? match = null)
    {
        if (!DebugMode && Recipe is null)
            return false;

        LatestRequestLease<string> request = BeginRequest();
        _error = null;
        try
        {
            PokemonLookupSection? section = GetFirstAuthorizedLookupSection();
            PokemonLookupSnapshot lookup;
            if (!DebugMode || section is not null)
            {
                lookup = await LookupPokemonAsync(speciesId, section ?? PokemonLookupSection.Overview, request.CancellationToken);
            }
            else if (match is not null)
            {
                PokemonInformationPage page = TrackerDiagnosticCapabilityRules.HasAnyOverviewSurface(Connection)
                    ? PokemonInformationPage.Overview
                    : PokemonInformationPage.Evolutions;
                lookup = new PokemonLookupSnapshot
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

            if (!request.IsCurrent)
                return false;

            _lookup = lookup;
            _currentSpeciesId = lookup.Identity.SpeciesId;
            _matches = [];
            return true;
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            if (request.IsCurrent)
                _error = exception.Message;

            return false;
        }
        finally
        {
            CompleteRequest(request);
        }
    }

    /// <summary>
    /// Sends a lookup request using the configured lookup source.
    /// </summary>
    /// <param name="speciesId">The stable Pokémon identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <param name="cancellationToken">The token that cancels a replaced request.</param>
    /// <returns>The generated Pokémon snapshot.</returns>
    private Task<PokemonLookupSnapshot> LookupPokemonAsync(string speciesId, PokemonLookupSection section, CancellationToken cancellationToken)
    {
        return DebugMode
            ? Connection.LookupDebugPokemonAsync(speciesId, section, cancellationToken)
            : Connection.LookupPokemonAsync(Recipe!, speciesId, section, cancellationToken);
    }

    /// <summary>
    /// Cancels the previous explorer operation and begins the new latest request.
    /// </summary>
    /// <returns>The lease owned by the new request.</returns>
    private LatestRequestLease<string> BeginRequest()
    {
        LatestRequestLease<string> request = _requests.Begin(_lookupRequestKey);
        _loading = true;
        return request;
    }

    /// <summary>
    /// Releases one completed request without clearing a newer request's loading state.
    /// </summary>
    /// <param name="request">The completed request lease.</param>
    private void CompleteRequest(LatestRequestLease<string> request)
    {
        if (request.Complete())
            _loading = false;
    }

    /// <summary>
    /// Invalidates the active request and clears its loading state.
    /// </summary>
    private void CancelRequest()
    {
        _requests.CancelAll();
        _loading = false;
    }

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
        => _searchPagination.HasPrevious;

    /// <summary>
    /// Gets whether a later search page exists.
    /// </summary>
    /// <returns>Whether matches remain after the current page.</returns>
    private bool HasNextSearchPage()
        => _searchPagination.HasNext(_matchTotal, _matches.Count);

    /// <summary>
    /// Loads the previous search page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task PreviousSearchPageAsync()
        => SearchPageAsync(_searchPagination.PageIndex - 1);

    /// <summary>
    /// Loads the next search page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task NextSearchPageAsync()
        => SearchPageAsync(_searchPagination.PageIndex + 1);

    /// <summary>
    /// Formats the inclusive visible result range.
    /// </summary>
    /// <returns>The visible range and total count.</returns>
    private string GetSearchRangeText()
    {
        (int first, int last) = _searchPagination.GetRange(_matchTotal, _matches.Count);
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

        CancelRequest();
        _matches = [];
        _lookup = null;
        _currentSpeciesId = null;
        _history.Clear();
        _searchPagination.Reset();
        _matchTotal = 0;
        _error = null;
        _searched = false;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes the diagnostic-access subscription.
    /// </summary>
    public void Dispose()
    {
        AccessService.Changed -= HandleDiagnosticAccessChanged;
        _requests.Dispose();
    }
}
