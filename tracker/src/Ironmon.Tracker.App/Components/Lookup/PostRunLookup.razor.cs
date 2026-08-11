using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates completed-run selection for deterministic Pokémon lookup.
/// </summary>
public partial class PostRunLookup : IDisposable
{
    private IReadOnlyList<CompletedRunRecipePayload> _recipes = [];
    private string? _selectedRunId;

    /// <summary>
    /// Gets or initializes the completed-run recipe archive.
    /// </summary>
    [Inject]
    private CompletedRunArchive CompletedRuns { get; set; } = null!;

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
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
    }

    /// <summary>
    /// Selects a completed run.
    /// </summary>
    /// <param name="args">The select element change.</param>
    private void SelectRun(ChangeEventArgs args)
        => _selectedRunId = args.Value?.ToString();

    /// <summary>
    /// Gets the currently selected completed-run recipe.
    /// </summary>
    /// <returns>The selected recipe or null.</returns>
    private CompletedRunRecipePayload? GetSelectedRecipe()
        => _recipes.FirstOrDefault(recipe => recipe.RunId == _selectedRunId);

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
        if (requestedRunId is not null && _recipes.Any(recipe => recipe.RunId == requestedRunId))
        {
            _selectedRunId = requestedRunId;
        }
        else if (_selectedRunId is null || _recipes.All(recipe => recipe.RunId != _selectedRunId))
        {
            _selectedRunId = _recipes.Count > 0 ? _recipes[0].RunId : null;
        }
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
    }
}
