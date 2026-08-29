namespace Ironmon.Tracker.Core;

/// <summary>
/// Retains an explicit save-slot history selection while prioritizing the slot loaded by the game.
/// </summary>
public sealed class SaveSlotSelectionState
{
    private string? _currentSaveSlot;

    /// <summary>
    /// Initializes an empty save-slot selection.
    /// </summary>
    public SaveSlotSelectionState()
    {
    }

    /// <summary>
    /// Gets the selected save-slot identifier.
    /// </summary>
    public string? SelectedSaveSlot { get; private set; }

    /// <summary>
    /// Records a deliberate save-slot selection.
    /// </summary>
    /// <param name="saveSlot">The save-slot identifier.</param>
    public void Select(string saveSlot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveSlot);
        SelectedSaveSlot = saveSlot;
    }

    /// <summary>
    /// Reconciles the selection with available histories and the slot loaded by the game.
    /// </summary>
    /// <param name="saveSlots">Available save-slot identifiers in display order.</param>
    /// <param name="currentSaveSlot">The save slot currently loaded by the game.</param>
    public void Refresh(IReadOnlyList<string> saveSlots, string? currentSaveSlot)
    {
        ArgumentNullException.ThrowIfNull(saveSlots);
        string? availableCurrent = saveSlots.FirstOrDefault(saveSlot => string.Equals(saveSlot, currentSaveSlot, StringComparison.OrdinalIgnoreCase));
        bool currentChanged = !string.Equals(currentSaveSlot, _currentSaveSlot, StringComparison.OrdinalIgnoreCase);
        _currentSaveSlot = currentSaveSlot;
        if (currentChanged && availableCurrent is not null)
        {
            SelectedSaveSlot = availableCurrent;
            return;
        }

        bool selectedAvailable = SelectedSaveSlot is not null && saveSlots.Any(saveSlot => string.Equals(saveSlot, SelectedSaveSlot, StringComparison.OrdinalIgnoreCase));
        if (!selectedAvailable)
            SelectedSaveSlot = availableCurrent ?? (saveSlots.Count > 0 ? saveSlots[0] : null);
    }
}
