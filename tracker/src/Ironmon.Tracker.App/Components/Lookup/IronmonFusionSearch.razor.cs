using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Searches normal fusion materials and calculates their seeded Ironmon outcomes.
/// </summary>
public partial class IronmonFusionSearch : IDisposable
{
    private static readonly TimeSpan _obtainabilityRefreshInterval = TimeSpan.FromMilliseconds(500);
    private readonly PaginationState _searchPagination = new(TrackerProtocol.DefaultSearchPageSize);
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private IReadOnlyList<FusionOutcomeSnapshot> _outcomes = [];
    private CancellationTokenSource? _obtainabilityRefreshCancellation;
    private string _query = string.Empty;
    private string? _error;
    private string? _pokemonKey;
    private int _matchTotal;
    private bool _loading;
    private bool _searched;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the normal Pokemon whose fusion results are being explored.
    /// </summary>
    [Parameter]
    public PokemonLookupSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether requests use the authorized active-run debug channel.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

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
        if (_pokemonKey == Pokemon.Identity.SpeciesId)
            return;

        _pokemonKey = Pokemon.Identity.SpeciesId;
        CancelObtainabilityRefresh();
        _matches = [];
        _outcomes = [];
        _query = string.Empty;
        _error = null;
        _searchPagination.Reset();
        _matchTotal = 0;
        _searched = false;
    }

    /// <summary>
    /// Starts a material search when Enter is pressed.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    private async Task HandleSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == TrackerKeyboardKeys.Enter)
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
    /// <param name="pageIndex">The zero-based result page.</param>
    /// <returns>A task representing the search.</returns>
    private async Task SearchPageAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (_loading)
            return;

        if (string.IsNullOrWhiteSpace(_query))
        {
            _error = Text["Lookup.Fusion.EnterPokemonName"];
            return;
        }

        _loading = true;
        _searched = false;
        _error = null;
        _outcomes = [];
        try
        {
            int offset = checked(pageIndex * _searchPagination.PageSize);
            PokemonSearchResponsePayload response = await SearchPokemonAsync(offset);
            _matches = response.Matches;
            _searchPagination.Select(pageIndex);
            _matchTotal = Math.Max(response.Total, offset + response.Matches.Count);
            _searched = true;
            StartObtainabilityRefresh();
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
            FusionPreviewResponsePayload response = await PreviewFusionAsync(match.SpeciesId);
            _outcomes = response.Outcomes;
            StartObtainabilityRefresh();
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
        => _searchPagination.HasPrevious;

    /// <summary>
    /// Gets whether a subsequent material page is available.
    /// </summary>
    /// <returns>True when matches remain after the current page.</returns>
    private bool HasNextPage()
        => _searchPagination.HasNext(_matchTotal, _matches.Count);

    /// <summary>
    /// Loads the previous material page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task PreviousPageAsync()
        => SearchPageAsync(_searchPagination.PageIndex - 1);

    /// <summary>
    /// Loads the next material page.
    /// </summary>
    /// <returns>A task representing the search.</returns>
    private Task NextPageAsync()
        => SearchPageAsync(_searchPagination.PageIndex + 1);

    /// <summary>
    /// Formats the visible inclusive material-search range.
    /// </summary>
    /// <returns>The result range and total.</returns>
    private string GetRangeText()
    {
        (int first, int last) = _searchPagination.GetRange(_matchTotal, _matches.Count);
        return Text["Lookup.Fusion.ResultRange", first, last, _matchTotal];
    }

    /// <summary>
    /// Searches normal fusion materials through the selected lookup channel.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>The matching normal Pokemon.</returns>
    private Task<PokemonSearchResponsePayload> SearchPokemonAsync(int offset)
    {
        if (DebugMode)
            return Connection.SearchDebugPokemonAsync(_query.Trim(), offset, TrackerProtocol.DefaultSearchPageSize, true);

        CompletedRunRecipePayload recipe = Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]);
        return Connection.SearchPokemonAsync(recipe, _query.Trim(), offset, TrackerProtocol.DefaultSearchPageSize, true);
    }

    /// <summary>
    /// Calculates fusion outcomes through the selected lookup channel.
    /// </summary>
    /// <param name="secondSpeciesId">The selected second material.</param>
    /// <returns>The generated fusion outcomes.</returns>
    private Task<FusionPreviewResponsePayload> PreviewFusionAsync(string secondSpeciesId)
    {
        if (DebugMode)
            return Connection.PreviewDebugFusionAsync(Pokemon.Identity.SpeciesId, secondSpeciesId);

        CompletedRunRecipePayload recipe = Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]);
        return Connection.PreviewFusionAsync(recipe, Pokemon.Identity.SpeciesId, secondSpeciesId);
    }

    /// <summary>
    /// Starts a passive refresh for calculating statuses on the currently displayed cards.
    /// </summary>
    private void StartObtainabilityRefresh()
    {
        CancelObtainabilityRefresh();
        IReadOnlyList<string> speciesIds = [.. _matches
            .Where(match => match.ObtainabilityStatus == PokemonObtainabilityStatus.Calculating)
            .Select(match => match.SpeciesId)
            .Concat(_outcomes.SelectMany(outcome => new[] { outcome.Body, outcome.Head, outcome.Result })
                .Where(relation => relation.ObtainabilityStatus == PokemonObtainabilityStatus.Calculating)
                .Select(relation => relation.SpeciesId))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (speciesIds.Count == 0)
            return;

        CancellationTokenSource cancellation = new();
        _obtainabilityRefreshCancellation = cancellation;
        _ = RefreshObtainabilityAsync(speciesIds, cancellation);
    }

    /// <summary>
    /// Refreshes visible material and outcome states without requesting global material work.
    /// </summary>
    /// <param name="speciesIds">The visible identifiers awaiting final states.</param>
    /// <param name="cancellation">The owner of the refresh lifetime.</param>
    /// <returns>A task representing passive refreshes.</returns>
    private async Task RefreshObtainabilityAsync(IReadOnlyList<string> speciesIds, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                PokemonObtainabilityResponsePayload response = DebugMode
                    ? await Connection.AdvanceDebugPokemonObtainabilityAsync(speciesIds: speciesIds, foreground: false, cancellationToken: cancellation.Token)
                    : await Connection.AdvancePokemonObtainabilityAsync(Recipe!, speciesIds: speciesIds, foreground: false, cancellationToken: cancellation.Token);

                if (!ReferenceEquals(_obtainabilityRefreshCancellation, cancellation))
                    return;

                HashSet<string> obtainable = new(response.ObtainableSpeciesIds, StringComparer.OrdinalIgnoreCase);
                bool complete = response.Complete || response.BackgroundComplete;
                _matches = [.. _matches.Select(match => new PokemonSearchMatch
                {
                    SpeciesId = match.SpeciesId,
                    SpeciesName = match.SpeciesName,
                    Fusion = match.Fusion,
                    ObtainabilityStatus = ResolveObtainabilityStatus(match.SpeciesId, match.ObtainabilityStatus, speciesIds, obtainable, complete)
                })];

                _outcomes = [.. _outcomes.Select(outcome => new FusionOutcomeSnapshot
                {
                    Body = RefreshRelation(outcome.Body, speciesIds, obtainable, complete),
                    Head = RefreshRelation(outcome.Head, speciesIds, obtainable, complete),
                    Result = RefreshRelation(outcome.Result, speciesIds, obtainable, complete)
                })];

                await InvokeAsync(StateHasChanged);
                if (complete)
                    return;

                await Task.Delay(_obtainabilityRefreshInterval, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            if (ReferenceEquals(_obtainabilityRefreshCancellation, cancellation))
                _error = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_obtainabilityRefreshCancellation, cancellation))
                _obtainabilityRefreshCancellation = null;

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Copies one relation with its newest request-scoped status.
    /// </summary>
    /// <param name="relation">The relation to copy.</param>
    /// <param name="speciesIds">The identifiers included in the refresh.</param>
    /// <param name="obtainable">The identifiers with proven paths.</param>
    /// <param name="complete">Whether every requested target is resolved.</param>
    /// <returns>The refreshed relation.</returns>
    private static PokemonRelationSnapshot RefreshRelation(PokemonRelationSnapshot relation, IReadOnlyList<string> speciesIds, IReadOnlySet<string> obtainable, bool complete)
    {
        return new PokemonRelationSnapshot
        {
            SpeciesId = relation.SpeciesId,
            SpeciesName = relation.SpeciesName,
            SpritePath = relation.SpritePath,
            Label = relation.Label,
            ObtainabilityStatus = ResolveObtainabilityStatus(relation.SpeciesId, relation.ObtainabilityStatus, speciesIds, obtainable, complete)
        };
    }

    /// <summary>
    /// Resolves one requested identifier from the bounded response membership.
    /// </summary>
    /// <param name="speciesId">The identifier to resolve.</param>
    /// <param name="current">The currently displayed state.</param>
    /// <param name="requested">The identifiers included in the refresh.</param>
    /// <param name="obtainable">The identifiers with proven paths.</param>
    /// <param name="complete">Whether every requested target is resolved.</param>
    /// <returns>The newest display state.</returns>
    private static PokemonObtainabilityStatus? ResolveObtainabilityStatus(string speciesId, PokemonObtainabilityStatus? current, IReadOnlyList<string> requested, IReadOnlySet<string> obtainable, bool complete)
    {
        if (obtainable.Contains(speciesId))
            return PokemonObtainabilityStatus.Obtainable;

        return complete && requested.Contains(speciesId, StringComparer.OrdinalIgnoreCase)
            ? PokemonObtainabilityStatus.Unobtainable
            : current;
    }

    /// <summary>
    /// Stops the current visible-card status refresh.
    /// </summary>
    private void CancelObtainabilityRefresh()
    {
        _obtainabilityRefreshCancellation?.Cancel();
        _obtainabilityRefreshCancellation = null;
    }

    /// <summary>
    /// Stops refresh work owned by the component.
    /// </summary>
    public void Dispose()
        => CancelObtainabilityRefresh();
}
