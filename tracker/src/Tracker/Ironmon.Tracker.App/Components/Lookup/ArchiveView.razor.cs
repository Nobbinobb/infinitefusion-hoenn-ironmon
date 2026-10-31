using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates completed-run selection and archived information surfaces.
/// </summary>
public partial class ArchiveView : IDisposable
{
    private static readonly TimeSpan _obtainabilityPrecalculationInterval = TimeSpan.FromMilliseconds(250);
    private readonly ArchiveRunSelectionState _selection = new();
    private CancellationTokenSource? _obtainabilityPrecalculationCancellation;
    private string? _obtainabilityPrecalculationRunId;
    private IReadOnlyList<CompletedRunRecipePayload> _recipes = [];
    private ArchiveSection _selectedSection = ArchiveSection.Summary;
    private TrackerConnectionStatus _connectionStatus;
    private bool _showHistory;
    private string? _historySpeciesId;
    private long _historyLookupVersion;
    private bool _explicitPreparationRequested;
    private bool _observedAutomaticSelection;

    /// <summary>
    /// Gets or initializes the completed-run recipe archive.
    /// </summary>
    [Inject]
    private CompletedRunArchive CompletedRuns { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the connected game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared connection status service.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the current game's authoritative attempt statistics.
    /// </summary>
    [Parameter]
    public RunStatisticsPayload? CurrentStatistics { get; set; }

    /// <summary>
    /// Gets or sets whether run completion opened Archive automatically rather than through user navigation.
    /// </summary>
    [Parameter]
    public bool AutomaticSelection { get; set; }

    /// <summary>
    /// Gets whether the current Archive selection has been explicitly requested by the user.
    /// </summary>
    private bool ForegroundPreparationRequested => !AutomaticSelection || _explicitPreparationRequested;

    /// <summary>
    /// Loads available recipes and subscribes to archive changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _observedAutomaticSelection = AutomaticSelection;
        _connectionStatus = ConnectionState.Snapshot.Status;
        RefreshRecipes(CompletedRuns.RequestedRunId);
        _showHistory = _recipes.Count == 0;
        _selection.SetExpanded(!_showHistory);
        InitializeUpdateNavigation();
        RestartObtainabilityPrecalculation();
        CompletedRuns.Changed += HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
        ConnectionState.Changed += HandleConnectionChanged;
    }

    /// <summary>
    /// Promotes explicit Archive navigation while retaining background priority for automatic reset navigation.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_observedAutomaticSelection == AutomaticSelection)
            return;

        _observedAutomaticSelection = AutomaticSelection;
        if (AutomaticSelection)
            _explicitPreparationRequested = false;

        RestartObtainabilityPrecalculation();
    }

    /// <summary>
    /// Opens save-file history without starting run preparation.
    /// </summary>
    private void ShowHistory()
    {
        _showHistory = true;
        StopObtainabilityPrecalculation();
    }

    /// <summary>
    /// Selects a completed run while retaining its information tab.
    /// </summary>
    /// <param name="runId">The archived run identifier.</param>
    private void SelectRun(string runId)
    {
        if (!_recipes.Any(recipe => recipe.RunId == runId))
            return;

        if (_selection.SelectedRunId != runId)
            _historySpeciesId = null;

        _selection.Select(runId);
        _selection.SetExpanded(true);
        _showHistory = false;
        _explicitPreparationRequested = true;
        RestartObtainabilityPrecalculation();
    }

    /// <summary>
    /// Opens a run summary directly from save-file history.
    /// </summary>
    /// <param name="runId">The archived run identifier.</param>
    private void OpenHistoryRun(string runId)
    {
        _selectedSection = ArchiveSection.Summary;
        SelectRun(runId);
    }

    /// <summary>
    /// Gets the currently selected completed-run recipe.
    /// </summary>
    /// <returns>The selected recipe or null.</returns>
    private CompletedRunRecipePayload? GetSelectedRecipe()
        => _recipes.FirstOrDefault(recipe => recipe.RunId == _selection.SelectedRunId);

    /// <summary>
    /// Selects one completed-run Archive section.
    /// </summary>
    /// <param name="section">The selected section.</param>
    private void SelectSection(ArchiveSection section)
    {
        _selectedSection = section;
        RequestForegroundPreparation();
    }

    /// <summary>
    /// Promotes an automatically selected run once the user requests its visible information.
    /// </summary>
    private void RequestForegroundPreparation()
    {
        bool alreadyForeground = ForegroundPreparationRequested;
        _explicitPreparationRequested = true;
        if (!alreadyForeground)
            RestartObtainabilityPrecalculation();
    }

    /// <summary>
    /// Opens a recorded runner directly in the selected run's normal Pokemon tab, including repeated visits.
    /// </summary>
    /// <param name="speciesId">The retained species and form identity to inspect.</param>
    private void OpenHistoryPokemon(string speciesId)
    {
        if (string.IsNullOrWhiteSpace(speciesId) || GetSelectedRecipe() is null)
            return;

        _historySpeciesId = speciesId;
        _historyLookupVersion++;
        _selectedSection = ArchiveSection.Pokemon;
        RequestForegroundPreparation();
    }

    /// <summary>
    /// Gets the localized name of one Archive section.
    /// </summary>
    /// <param name="section">The section to name.</param>
    /// <returns>The localized section name.</returns>
    private string GetSectionName(ArchiveSection section) => section switch
    {
        ArchiveSection.Summary => Text["Archive.Sections.Summary"],
        ArchiveSection.Areas => Text["Archive.Sections.Areas"],
        ArchiveSection.Pokemon => Text["Archive.Sections.Pokemon"],
        _ => string.Empty
    };

    /// <summary>
    /// Gets the shared icon identifying an archived-run section.
    /// </summary>
    /// <param name="section">The section to represent.</param>
    /// <returns>The corresponding outline icon.</returns>
    private static ObsidianIconKind GetSectionIcon(ArchiveSection section) => section switch
    {
        ArchiveSection.Summary => ObsidianIconKind.Chart,
        ArchiveSection.Areas => ObsidianIconKind.Map,
        _ => ObsidianIconKind.Search
    };

    /// <summary>
    /// Reloads recipes while retaining a still-valid selection.
    /// </summary>
    /// <param name="requestedRunId">An explicitly requested completed run.</param>
    private void RefreshRecipes(string? requestedRunId = null)
    {
        string? previous = _selection.SelectedRunId;
        _recipes = CompletedRuns.Recipes;
        _selection.Refresh([.. _recipes.Select(recipe => recipe.RunId)], requestedRunId);
        if (previous != _selection.SelectedRunId)
            _historySpeciesId = null;
    }

    /// <summary>
    /// Keeps the selected run's work and timings, or replaces it when selection and availability change.
    /// </summary>
    private void RestartObtainabilityPrecalculation()
    {
        CompletedRunRecipePayload? recipe = GetSelectedRecipe();
        bool prepare = !_showHistory && _selection.IsExpanded && recipe is not null
            && _connectionStatus == TrackerConnectionStatus.Connected
            && !Connection.ObtainabilityProgress.HasCompletedPreparation(recipe.RunId);
        if (prepare && _obtainabilityPrecalculationCancellation is { IsCancellationRequested: false }
            && string.Equals(_obtainabilityPrecalculationRunId, recipe!.RunId, StringComparison.Ordinal))
        {
            return;
        }

        StopObtainabilityPrecalculation();
        if (!prepare)
            return;

        CancellationTokenSource cancellation = new();
        _obtainabilityPrecalculationCancellation = cancellation;
        _obtainabilityPrecalculationRunId = recipe!.RunId;
        Connection.SelectArchiveObtainabilityPrecalculation(recipe.RunId);
        _ = PrecalculateObtainabilityAsync(recipe, cancellation);
    }

    /// <summary>
    /// Advances the selected archived run until its shared calculation is complete or canceled.
    /// </summary>
    /// <param name="recipe">The selected archived run recipe.</param>
    /// <param name="cancellation">The selection-owned cancellation source.</param>
    /// <returns>A task representing preparation at the visible selection's priority.</returns>
    private async Task PrecalculateObtainabilityAsync(CompletedRunRecipePayload recipe, CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                PokemonObtainabilityResponsePayload response = await Connection.AdvancePokemonObtainabilityAsync(recipe, foreground: ForegroundPreparationRequested, cancellationToken: cancellation.Token);
                if (response.Complete || response.BackgroundComplete)
                    break;

                await Task.Delay(_obtainabilityPrecalculationInterval, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
        }
        finally
        {
            if (ReferenceEquals(_obtainabilityPrecalculationCancellation, cancellation))
            {
                _obtainabilityPrecalculationCancellation = null;
                _obtainabilityPrecalculationRunId = null;
                Connection.ClearArchiveObtainabilityPrecalculation(recipe.RunId);
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Cancels and releases the currently selected archived-run calculation.
    /// </summary>
    private void StopObtainabilityPrecalculation()
    {
        CancellationTokenSource? cancellation = _obtainabilityPrecalculationCancellation;
        string? runId = _obtainabilityPrecalculationRunId;
        _obtainabilityPrecalculationCancellation = null;
        _obtainabilityPrecalculationRunId = null;
        cancellation?.Cancel();
        if (runId is not null)
            Connection.ClearArchiveObtainabilityPrecalculation(runId);
    }

    /// <summary>
    /// Refreshes the component after the completed-run archive changes.
    /// </summary>
    /// <param name="sender">The archive raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleCompletedRunsChanged(object? sender, EventArgs args)
    {
        string? requestedRunId = CompletedRuns.RequestedRunId;
        bool newlyRequestedRun = requestedRunId is not null
            && !string.Equals(
                requestedRunId,
                _selection.SelectedRunId,
                StringComparison.Ordinal);
        if (newlyRequestedRun)
        {
            _explicitPreparationRequested = false;
            _selection.SetExpanded(true);
            _showHistory = false;
            _selectedSection = ArchiveSection.Summary;
            StopObtainabilityPrecalculation();
            RefreshRecipes(requestedRunId);
            RestartObtainabilityPrecalculation();
        }
        else
        {
            RefreshRecipes();
            RestartObtainabilityPrecalculation();
        }

        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Selects a newly archived completion and refreshes the component.
    /// </summary>
    /// <param name="sender">The archive requesting selection.</param>
    /// <param name="args">The selection event arguments.</param>
    private void HandleCompletedRunSelectionRequested(object? sender, EventArgs args)
    {
        _explicitPreparationRequested = false;
        _selection.SetExpanded(true);
        _showHistory = false;
        _selectedSection = ArchiveSection.Summary;
        StopObtainabilityPrecalculation();
        RefreshRecipes(CompletedRuns.RequestedRunId);
        RestartObtainabilityPrecalculation();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Starts or stops Archive preparation when the game connection lifecycle changes.
    /// </summary>
    /// <param name="sender">The connection state raising the event.</param>
    /// <param name="args">The connection change event arguments.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
    {
        TrackerConnectionStatus status = ConnectionState.Snapshot.Status;
        if (status == _connectionStatus)
            return;

        _connectionStatus = status;
        RestartObtainabilityPrecalculation();
    }

    /// <summary>
    /// Removes completed-run archive subscriptions.
    /// </summary>
    public void Dispose()
    {
        UpdateNavigation?.Unregister(UpdateNavigationKey);
        StopObtainabilityPrecalculation();
        CompletedRuns.Changed -= HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested -= HandleCompletedRunSelectionRequested;
        ConnectionState.Changed -= HandleConnectionChanged;
        GC.SuppressFinalize(this);
    }
}
