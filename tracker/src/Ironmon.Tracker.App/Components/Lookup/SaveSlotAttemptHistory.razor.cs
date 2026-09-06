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
