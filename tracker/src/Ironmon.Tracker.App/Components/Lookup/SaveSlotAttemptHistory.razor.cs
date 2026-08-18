using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents the newest archived cumulative attempt totals for each identified save slot.
/// </summary>
public partial class SaveSlotAttemptHistory
{
    private IReadOnlyList<KeyValuePair<string, RunStatisticsPayload>> _slots = [];
    private string? _selectedSlot;

    /// <summary>
    /// Gets or sets completed recipes used to obtain save-slot totals.
    /// </summary>
    [Parameter]
    public IReadOnlyList<CompletedRunRecipePayload> Recipes { get; set; } = [];

    /// <summary>
    /// Rebuilds identified save-slot summaries when the archive changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        IEnumerable<RunStatisticsPayload> statistics = Recipes
            .Select(recipe => recipe.Statistics)
            .OfType<RunStatisticsPayload>()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SaveSlot));

        _slots = [.. statistics
            .GroupBy(entry => entry.SaveSlot!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new KeyValuePair<string, RunStatisticsPayload>(group.Key, group
                .OrderByDescending(entry => entry.AttemptsStarted)
                .ThenByDescending(entry => entry.AttemptNumber)
                .First()))];

        if (_selectedSlot is null || _slots.All(slot => !string.Equals(slot.Key, _selectedSlot, StringComparison.OrdinalIgnoreCase)))
            _selectedSlot = _slots.FirstOrDefault().Key;
    }

    /// <summary>
    /// Selects one save-slot history tab.
    /// </summary>
    /// <param name="slot">The save-slot identifier.</param>
    private void SelectSlot(string slot)
        => _selectedSlot = slot;

    /// <summary>
    /// Gets the cumulative totals for the selected save slot.
    /// </summary>
    /// <returns>The newest archived statistics for the selected slot.</returns>
    private RunStatisticsPayload? GetSelectedStatistics()
        => _slots.FirstOrDefault(slot => string.Equals(slot.Key, _selectedSlot, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// Gets the visual classes for one save-slot tab.
    /// </summary>
    /// <param name="slot">The represented save-slot identifier.</param>
    /// <returns>The tab classes.</returns>
    private string GetTabClass(string slot)
        => string.Equals(slot, _selectedSlot, StringComparison.OrdinalIgnoreCase) ? "save-slot-tab selected" : "save-slot-tab";
}
