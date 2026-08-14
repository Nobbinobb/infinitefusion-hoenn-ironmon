namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies active and archived run lookup selection across asynchronous connection recovery.
/// </summary>
public sealed class LookupRunSelectionStateTests
{
    /// <summary>
    /// Initializes the lookup selection state tests.
    /// </summary>
    public LookupRunSelectionStateTests()
    {
    }

    /// <summary>
    /// Verifies that a temporary archive fallback yields when current-state recovery exposes the active run.
    /// </summary>
    [Fact]
    public void AutomaticArchiveFallbackYieldsToRecoveredActiveRun()
    {
        LookupRunSelectionState selection = new();

        selection.Refresh(false, ["run-old"]);
        Assert.Equal("run-old", selection.SelectedRunId);

        selection.Refresh(true, ["run-old"]);
        Assert.Equal(LookupRunSelectionState.ActiveRunSelection, selection.SelectedRunId);
    }

    /// <summary>
    /// Verifies that a deliberate archived-run choice survives unrelated active progress updates.
    /// </summary>
    [Fact]
    public void ExplicitArchiveSelectionSurvivesActiveRunRefresh()
    {
        LookupRunSelectionState selection = new();
        selection.Refresh(true, ["run-old"]);
        selection.Select("run-old");

        selection.Refresh(true, ["run-old"]);

        Assert.Equal("run-old", selection.SelectedRunId);
        Assert.False(selection.IsAutomatic);
    }
}
