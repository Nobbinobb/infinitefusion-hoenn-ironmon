namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies save-slot history selection against the slot currently loaded by the game.
/// </summary>
public sealed class SaveSlotSelectionStateTests
{
    private const string FileA = "File A";
    private const string FileB = "File B";

    /// <summary>
    /// Initializes save-slot selection tests.
    /// </summary>
    public SaveSlotSelectionStateTests()
    {
    }

    /// <summary>
    /// Verifies that the current save slot is preferred over display order.
    /// </summary>
    [Fact]
    public void CurrentSaveSlotIsSelectedInitially()
    {
        SaveSlotSelectionState selection = new();

        selection.Refresh([FileA, FileB], FileB);

        Assert.Equal(FileB, selection.SelectedSaveSlot);
    }

    /// <summary>
    /// Verifies that manual selection survives refreshes while the current slot is unchanged.
    /// </summary>
    [Fact]
    public void ManualSelectionSurvivesUnchangedCurrentSlot()
    {
        SaveSlotSelectionState selection = new();
        selection.Refresh([FileA, FileB], FileB);
        selection.Select(FileA);

        selection.Refresh([FileA, FileB], FileB);

        Assert.Equal(FileA, selection.SelectedSaveSlot);
    }

    /// <summary>
    /// Verifies that loading another represented save slot selects its history.
    /// </summary>
    [Fact]
    public void ChangedCurrentSaveSlotReplacesManualSelection()
    {
        SaveSlotSelectionState selection = new();
        selection.Refresh([FileA, FileB], FileA);
        selection.Select(FileB);

        selection.Refresh([FileA, FileB], FileB);

        Assert.Equal(FileB, selection.SelectedSaveSlot);
    }
}
