namespace Ironmon.Tracker.Core;

/// <summary>
/// Retains an explicit run lookup choice while allowing temporary startup fallbacks to yield to an active run.
/// </summary>
public sealed class LookupRunSelectionState
{
    /// <summary>
    /// Gets the stable selector value representing the connected active run.
    /// </summary>
    public const string ActiveRunSelection = "__active__";

    /// <summary>
    /// Initializes an empty automatic lookup selection.
    /// </summary>
    public LookupRunSelectionState()
    {
    }

    /// <summary>
    /// Gets the selected active-run marker or archived run identifier.
    /// </summary>
    public string? SelectedRunId { get; private set; }

    /// <summary>
    /// Gets whether the current selection was chosen as an availability fallback.
    /// </summary>
    public bool IsAutomatic { get; private set; } = true;

    /// <summary>
    /// Records a deliberate selection from the run picker.
    /// </summary>
    /// <param name="runId">The active-run marker or archived run identifier.</param>
    public void Select(string? runId)
    {
        SelectedRunId = runId;
        IsAutomatic = false;
    }

    /// <summary>
    /// Reconciles the selection with current active-run and archive availability.
    /// </summary>
    /// <param name="activeAvailable">Whether a connected active run can be selected.</param>
    /// <param name="archivedRunIds">Archived run identifiers in display order.</param>
    /// <param name="requestedRunId">An archived run explicitly requested by completion navigation.</param>
    public void Refresh(bool activeAvailable, IReadOnlyList<string> archivedRunIds, string? requestedRunId = null)
    {
        ArgumentNullException.ThrowIfNull(archivedRunIds);
        if (requestedRunId is not null && archivedRunIds.Contains(requestedRunId))
        {
            Select(requestedRunId);
            return;
        }

        if (activeAvailable && (IsAutomatic || SelectedRunId == ActiveRunSelection))
        {
            SelectedRunId = ActiveRunSelection;
            IsAutomatic = true;
            return;
        }

        if (!activeAvailable && (SelectedRunId == ActiveRunSelection || SelectedRunId is null || !archivedRunIds.Contains(SelectedRunId)))
        {
            SelectedRunId = archivedRunIds.Count > 0 ? archivedRunIds[0] : null;
            IsAutomatic = true;
        }
    }
}
