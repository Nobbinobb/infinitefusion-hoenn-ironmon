using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents the newest archived cumulative attempt totals for each identified save slot.
/// </summary>
public partial class SaveSlotAttemptHistory
{
    private readonly SaveSlotSelectionState _selection = new();
    private IReadOnlyList<KeyValuePair<string, RunStatisticsPayload>> _slots = [];

    /// <summary>
    /// Gets or sets the authoritative totals for each represented save slot.
    /// </summary>
    [Parameter]
    public IReadOnlyList<KeyValuePair<string, RunStatisticsPayload>> Slots { get; set; } = [];

    /// <summary>
    /// Gets or sets the save slot currently loaded by the game.
    /// </summary>
    [Parameter]
    public string? CurrentSaveSlot { get; set; }

    /// <summary>
    /// Gets or sets the archived runs available for direct navigation.
    /// </summary>
    [Parameter]
    public IReadOnlyList<CompletedRunRecipePayload> Recipes { get; set; } = [];

    /// <summary>
    /// Gets or sets the callback opening a recorded run.
    /// </summary>
    [Parameter]
    public EventCallback<string> RunSelected { get; set; }

    /// <summary>
    /// Gets archived runs belonging to the selected save file.
    /// </summary>
    private IReadOnlyList<CompletedRunRecipePayload> SelectedRecipes
        => _slots.Count == 0 ? Recipes : [.. Recipes.Where(recipe => string.Equals(recipe.Statistics?.SaveSlot, _selection.SelectedSaveSlot, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Gets legacy archived runs with no recorded save-file identity.
    /// </summary>
    private IReadOnlyList<CompletedRunRecipePayload> UnidentifiedRecipes
        => [.. Recipes.Where(recipe => string.IsNullOrWhiteSpace(recipe.Statistics?.SaveSlot))];

    /// <summary>
    /// Rebuilds identified save-slot summaries when the archive changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        _slots = Slots;

        _selection.Refresh([.. _slots.Select(slot => slot.Key)], CurrentSaveSlot);
    }

    /// <summary>
    /// Selects one save-slot history tab.
    /// </summary>
    /// <param name="slot">The save-slot identifier.</param>
    private void SelectSlot(string slot)
        => _selection.Select(slot);

    /// <summary>
    /// Gets the cumulative totals for the selected save slot.
    /// </summary>
    /// <returns>The newest archived statistics for the selected slot.</returns>
    private RunStatisticsPayload? GetSelectedStatistics()
        => _slots.FirstOrDefault(slot => string.Equals(slot.Key, _selection.SelectedSaveSlot, StringComparison.OrdinalIgnoreCase)).Value;

}
