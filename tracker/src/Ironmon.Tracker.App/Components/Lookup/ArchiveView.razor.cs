using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates completed-run selection and archived information surfaces.
/// </summary>
public partial class ArchiveView : IDisposable
{
    private static readonly TimeSpan ObtainabilityPrecalculationInterval = TimeSpan.FromMilliseconds(250);
    private readonly ArchiveRunSelectionState _selection = new();
    private CancellationTokenSource? _obtainabilityPrecalculationCancellation;
    private string? _obtainabilityPrecalculationRunId;
    private IReadOnlyList<CompletedRunRecipePayload> _recipes = [];
    private ArchiveSection _selectedSection = ArchiveSection.Summary;
    private TrackerConnectionStatus _connectionStatus;

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
    /// Loads available recipes and subscribes to archive changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _connectionStatus = ConnectionState.Snapshot.Status;
        RefreshRecipes(CompletedRuns.RequestedRunId);
        CompletedRuns.Changed += HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
        ConnectionState.Changed += HandleConnectionChanged;
    }

    /// <summary>
    /// Opens or closes completed-run details and their optional background preparation.
    /// </summary>
    private void ToggleCompletedRuns()
    {
        _selection.SetExpanded(!_selection.IsExpanded);
        if (_selection.IsExpanded)
            RestartObtainabilityPrecalculation();
        else
            StopObtainabilityPrecalculation();
    }

    /// <summary>
    /// Selects a completed run.
    /// </summary>
    /// <param name="args">The select element change.</param>
    private void SelectRun(ChangeEventArgs args)
    {
        _selection.Select(args.Value?.ToString());
        RestartObtainabilityPrecalculation();
    }

    /// <summary>
    /// Gets the currently selected completed-run recipe.
    /// </summary>
    /// <returns>The selected recipe or null.</returns>
    private CompletedRunRecipePayload? GetSelectedRecipe()
        => _recipes.FirstOrDefault(recipe => recipe.RunId == _selection.SelectedRunId);

    /// <summary>
    /// Formats one completed-run selection label.
    /// </summary>
    /// <param name="recipe">The selected recipe.</param>
    /// <returns>The concise run label.</returns>
    private string FormatRun(CompletedRunRecipePayload recipe)
        => Text["Lookup.Runs.CompletedRunOption", recipe.Result, recipe.Seed];

    /// <summary>
    /// Selects one completed-run Archive section.
    /// </summary>
    /// <param name="section">The selected section.</param>
    private void SelectSection(ArchiveSection section)
        => _selectedSection = section;

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
    /// Gets the visual classes for one Archive tab.
    /// </summary>
    /// <param name="section">The represented section.</param>
    /// <returns>The tab classes.</returns>
    private string GetSectionTabClass(ArchiveSection section)
        => section == _selectedSection ? "archive-tab selected" : "archive-tab";

    /// <summary>
    /// Reloads recipes while retaining a still-valid selection.
    /// </summary>
    /// <param name="requestedRunId">An explicitly requested completed run.</param>
    private void RefreshRecipes(string? requestedRunId = null)
    {
        _recipes = CompletedRuns.Recipes;
        _selection.Refresh([.. _recipes.Select(recipe => recipe.RunId)], requestedRunId);
    }

    /// <summary>
    /// Cancels any previous Archive calculation and starts the selected run in the background.
    /// </summary>
    private void RestartObtainabilityPrecalculation()
    {
        StopObtainabilityPrecalculation();
        if (!_selection.IsExpanded)
            return;

        CompletedRunRecipePayload? recipe = GetSelectedRecipe();
        if (recipe is null || _connectionStatus != TrackerConnectionStatus.Connected)
            return;

        CancellationTokenSource cancellation = new();
        _obtainabilityPrecalculationCancellation = cancellation;
        _obtainabilityPrecalculationRunId = recipe.RunId;
        Connection.SelectArchiveObtainabilityPrecalculation(recipe.RunId);
        _ = PrecalculateObtainabilityAsync(recipe, cancellation);
    }

    /// <summary>
    /// Advances the selected archived run until its shared calculation is complete or canceled.
    /// </summary>
    /// <param name="recipe">The selected archived run recipe.</param>
    /// <param name="cancellation">The selection-owned cancellation source.</param>
    /// <returns>A task representing background preparation.</returns>
    private async Task PrecalculateObtainabilityAsync(CompletedRunRecipePayload recipe, CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                PokemonObtainabilityResponsePayload response = await Connection.AdvancePokemonObtainabilityAsync(recipe, foreground: false, cancellationToken: cancellation.Token);
                if (response.Complete || response.BackgroundComplete)
                    break;

                await Task.Delay(ObtainabilityPrecalculationInterval, cancellation.Token);
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
            _selection.SetExpanded(false);
            StopObtainabilityPrecalculation();
            RefreshRecipes(requestedRunId);
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
        _selection.SetExpanded(false);
        StopObtainabilityPrecalculation();
        RefreshRecipes(CompletedRuns.RequestedRunId);
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
        StopObtainabilityPrecalculation();
        CompletedRuns.Changed -= HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested -= HandleCompletedRunSelectionRequested;
        ConnectionState.Changed -= HandleConnectionChanged;
        GC.SuppressFinalize(this);
    }
}
