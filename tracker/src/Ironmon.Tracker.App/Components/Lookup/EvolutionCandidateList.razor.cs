using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Loads and renders one server-paged generated evolution candidate list.
/// </summary>
public partial class EvolutionCandidateList
{
    private readonly PaginationState _pagination = new(TrackerProtocol.EvolutionCandidatePageSize);
    private IReadOnlyList<EvolutionCandidateSnapshot> _matches = [];
    private string _query = string.Empty;
    private string _appliedQuery = string.Empty;
    private string? _error;
    private string? _observedSource;
    private int _total;
    private int _unfilteredTotal;
    private bool _loading;

    /// <summary>
    /// Gets whether the name filter should be visible for the current list.
    /// </summary>
    private bool ShowFilter => AlwaysShowFilter || _unfilteredTotal >= TrackerProtocol.EvolutionCandidatePageSize;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the source species identifier.
    /// </summary>
    [Parameter]
    public required string SpeciesId { get; set; }

    /// <summary>
    /// Gets or sets the normal or component-specific list being displayed.
    /// </summary>
    [Parameter]
    public EvolutionCandidateSide Side { get; set; }

    /// <summary>
    /// Gets or sets whether the filter remains visible regardless of candidate count.
    /// </summary>
    [Parameter]
    public bool AlwaysShowFilter { get; set; }

    /// <summary>
    /// Gets or sets the visible list heading.
    /// </summary>
    [Parameter]
    public required string Heading { get; set; }

    /// <summary>
    /// Gets or sets the accessible list label.
    /// </summary>
    [Parameter]
    public required string AriaLabel { get; set; }

    /// <summary>
    /// Gets or sets the completed-run recipe when lookup is not using Debug.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether requests use the authorized active Debug run.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the optional live source that securely supplies the represented species.
    /// </summary>
    [Parameter]
    public DebugPokemonTarget? DebugTarget { get; set; }

    /// <summary>
    /// Gets or sets the enemy battler position when the live source is an enemy.
    /// </summary>
    [Parameter]
    public int? DebugEnemyPosition { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets whether the compact graph action is available.
    /// </summary>
    [Parameter]
    public bool ShowGraphAction { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the evolution graph is opened.
    /// </summary>
    [Parameter]
    public EventCallback GraphOpened { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a candidate is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Loads the first candidate page when the source changes.
    /// </summary>
    /// <returns>A task representing the page request.</returns>
    protected override async Task OnParametersSetAsync()
    {
        string source = $"{SpeciesId}|{Side}|{DebugMode}|{DebugTarget}|{DebugEnemyPosition}|{Recipe?.RunId}";
        if (source == _observedSource)
            return;

        _observedSource = source;
        _query = string.Empty;
        _appliedQuery = string.Empty;
        _pagination.Reset();
        _unfilteredTotal = 0;
        await LoadPageAsync(0);
    }

    /// <summary>
    /// Applies the entered candidate name filter.
    /// </summary>
    /// <returns>A task representing the filtered request.</returns>
    private Task ApplyFilterAsync()
    {
        _appliedQuery = _query.Trim();
        return LoadPageAsync(0);
    }

    /// <summary>
    /// Applies the candidate filter when Enter is pressed.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <returns>A task representing any filtered request.</returns>
    private Task HandleKeyDown(KeyboardEventArgs args)
        => args.Key == TrackerKeyboardKeys.Enter ? ApplyFilterAsync() : Task.CompletedTask;

    /// <summary>
    /// Loads the preceding candidate page.
    /// </summary>
    /// <returns>A task representing the page request.</returns>
    private Task PreviousPageAsync()
        => LoadPageAsync(_pagination.PageIndex - 1);

    /// <summary>
    /// Loads the following candidate page.
    /// </summary>
    /// <returns>A task representing the page request.</returns>
    private Task NextPageAsync()
        => LoadPageAsync(_pagination.PageIndex + 1);

    /// <summary>
    /// Requests one candidate page through the selected authorization channel.
    /// </summary>
    /// <param name="pageIndex">The zero-based result page.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadPageAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (_loading || !DebugMode && Recipe is null)
            return;

        _loading = true;
        _error = null;
        try
        {
            int offset = checked(pageIndex * _pagination.PageSize);
            EvolutionCandidateSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugEvolutionCandidatesAsync(SpeciesId, Side, _appliedQuery, offset, DebugTarget, DebugEnemyPosition)
                : await Connection.SearchEvolutionCandidatesAsync(Recipe!, SpeciesId, Side, _appliedQuery, offset);
            _matches = response.Matches;
            _pagination.Select(pageIndex);
            _total = response.Total;
            if (_appliedQuery.Length == 0)
                _unfilteredTotal = response.Total;
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
    /// Formats the inclusive candidate range shown on the current page.
    /// </summary>
    /// <returns>The visible range and filtered total.</returns>
    private string GetRangeText()
    {
        (int first, int last) = _pagination.GetRange(_total, _matches.Count);
        return Text["Lookup.Search.ResultRange", first, last, _total];
    }
}
