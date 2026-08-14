using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates completed-run selection for deterministic Pokémon lookup.
/// </summary>
public partial class PostRunLookup : IDisposable
{
    private readonly LookupRunSelectionState _selection = new();
    private IReadOnlyList<CompletedRunRecipePayload> _recipes = [];
    private TrackerConnectionSnapshot _connection = new(TrackerConnectionStatus.Stopped, null, null, null);

    /// <summary>
    /// Gets or initializes the completed-run recipe archive.
    /// </summary>
    [Inject]
    private CompletedRunArchive CompletedRuns { get; set; } = null!;

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
    /// Loads available recipes and subscribes to archive changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _connection = ConnectionState.Snapshot;
        RefreshRecipes();
        CompletedRuns.Changed += HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
        ConnectionState.Changed += HandleConnectionChanged;
    }

    /// <summary>
    /// Selects a completed run.
    /// </summary>
    /// <param name="args">The select element change.</param>
    private void SelectRun(ChangeEventArgs args)
        => _selection.Select(args.Value?.ToString());

    /// <summary>
    /// Gets the currently selected completed-run recipe.
    /// </summary>
    /// <returns>The selected recipe or null.</returns>
    private CompletedRunRecipePayload? GetSelectedRecipe()
        => _recipes.FirstOrDefault(recipe => recipe.RunId == _selection.SelectedRunId);

    /// <summary>
    /// Gets whether the connected game exposes an active Ironmon run.
    /// </summary>
    /// <returns>Whether an active run can be selected.</returns>
    private bool HasActiveRun()
    {
        return _connection.Status == TrackerConnectionStatus.Connected
            && _connection.CurrentState?.IronmonActive == true
            && GetActiveRunId() is not null;
    }

    /// <summary>
    /// Gets whether the active run is the selected lookup source.
    /// </summary>
    /// <returns>Whether the active run is selected.</returns>
    private bool IsActiveRunSelected()
        => _selection.SelectedRunId == LookupRunSelectionState.ActiveRunSelection && HasActiveRun();

    /// <summary>
    /// Gets the connected run identifier.
    /// </summary>
    /// <returns>The active run identifier, or null.</returns>
    private string? GetActiveRunId()
        => _connection.CurrentState?.RunId ?? _connection.Game?.RunId;

    /// <summary>
    /// Formats one completed-run selection label.
    /// </summary>
    /// <param name="recipe">The completed-run recipe.</param>
    /// <returns>The concise run label.</returns>
    private string FormatRun(CompletedRunRecipePayload recipe)
        => Text["Lookup.Runs.CompletedRunOption", recipe.Result, recipe.Seed];

    /// <summary>
    /// Reloads recipes while retaining a still-valid selection.
    /// </summary>
    private void RefreshRecipes(string? requestedRunId = null)
    {
        _recipes = CompletedRuns.Recipes;
        _selection.Refresh(HasActiveRun(), [.. _recipes.Select(recipe => recipe.RunId)], requestedRunId);
    }

    /// <summary>
    /// Refreshes active-run availability and progress after a connection update.
    /// </summary>
    /// <param name="sender">The connection state raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
    {
        _connection = ConnectionState.Snapshot;
        RefreshRecipes();
        _ = InvokeAsync(StateHasChanged);
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
    /// Selects a newly archived completion and refreshes the component.
    /// </summary>
    /// <param name="sender">The archive requesting selection.</param>
    /// <param name="args">The selection event arguments.</param>
    private void HandleCompletedRunSelectionRequested(object? sender, EventArgs args)
    {
        RefreshRecipes(CompletedRuns.RequestedRunId);
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes the completed-run archive subscription.
    /// </summary>
    public void Dispose()
    {
        CompletedRuns.Changed -= HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested -= HandleCompletedRunSelectionRequested;
        ConnectionState.Changed -= HandleConnectionChanged;
    }
}
