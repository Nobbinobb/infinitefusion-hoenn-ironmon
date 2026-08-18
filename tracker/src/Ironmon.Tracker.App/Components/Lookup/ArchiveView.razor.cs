using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Coordinates completed-run selection and archived information surfaces.
/// </summary>
public partial class ArchiveView : IDisposable
{
    private readonly ArchiveRunSelectionState _selection = new();
    private IReadOnlyList<CompletedRunRecipePayload> _recipes = [];
    private ArchiveSection _selectedSection = ArchiveSection.Summary;

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
        RefreshRecipes(CompletedRuns.RequestedRunId);
        CompletedRuns.Changed += HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
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
        ArchiveSection.Analysis => Text["Archive.Sections.Analysis"],
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
    /// Refreshes the component after the completed-run archive changes.
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
    /// Removes completed-run archive subscriptions.
    /// </summary>
    public void Dispose()
    {
        CompletedRuns.Changed -= HandleCompletedRunsChanged;
        CompletedRuns.SelectionRequested -= HandleCompletedRunSelectionRequested;
        GC.SuppressFinalize(this);
    }
}
