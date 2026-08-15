namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies completed-run selection across archive changes and completion navigation.
/// </summary>
public sealed class ArchiveRunSelectionStateTests
{
    /// <summary>
    /// Initializes archive selection state tests.
    /// </summary>
    public ArchiveRunSelectionStateTests()
    {
    }

    /// <summary>
    /// Verifies that automatic selection follows the first available completed run.
    /// </summary>
    [Fact]
    public void AutomaticSelectionFollowsArchiveAvailability()
    {
        ArchiveRunSelectionState selection = new();

        selection.Refresh(["run-old", "run-older"]);
        Assert.Equal("run-old", selection.SelectedRunId);

        selection.Refresh(["run-older"]);
        Assert.Equal("run-older", selection.SelectedRunId);
        Assert.True(selection.IsAutomatic);
    }

    /// <summary>
    /// Verifies that a deliberate completed-run choice survives unrelated archive updates.
    /// </summary>
    [Fact]
    public void ExplicitArchiveSelectionSurvivesRefresh()
    {
        ArchiveRunSelectionState selection = new();
        selection.Refresh(["run-new", "run-old"]);
        selection.Select("run-old");

        selection.Refresh(["run-new", "run-old", "run-older"]);

        Assert.Equal("run-old", selection.SelectedRunId);
        Assert.False(selection.IsAutomatic);
    }

    /// <summary>
    /// Verifies that completion navigation selects its newly archived run.
    /// </summary>
    [Fact]
    public void RequestedCompletedRunOverridesCurrentSelection()
    {
        ArchiveRunSelectionState selection = new();
        selection.Refresh(["run-old"]);

        selection.Refresh(["run-new", "run-old"], "run-new");

        Assert.Equal("run-new", selection.SelectedRunId);
        Assert.False(selection.IsAutomatic);
    }
}
