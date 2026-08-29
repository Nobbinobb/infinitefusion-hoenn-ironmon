namespace Ironmon.Tracker.Core;

/// <summary>
/// Retains a completed-run selection while reconciling changes to the archive.
/// </summary>
public sealed class ArchiveRunSelectionState
{
    /// <summary>
    /// Initializes an empty automatic archive selection.
    /// </summary>
    public ArchiveRunSelectionState()
    {
    }

    /// <summary>
    /// Gets the selected completed-run identifier.
    /// </summary>
    public string? SelectedRunId { get; private set; }

    /// <summary>
    /// Gets whether completed-run details are explicitly expanded.
    /// </summary>
    public bool IsExpanded { get; private set; }

    /// <summary>
    /// Gets whether the current selection was chosen as an availability fallback.
    /// </summary>
    public bool IsAutomatic { get; private set; } = true;

    /// <summary>
    /// Records a deliberate completed-run selection.
    /// </summary>
    /// <param name="runId">The completed-run identifier.</param>
    public void Select(string? runId)
    {
        SelectedRunId = runId;
        IsAutomatic = false;
    }

    /// <summary>
    /// Records whether completed-run details are explicitly expanded.
    /// </summary>
    /// <param name="expanded">Whether the completed-run disclosure is open.</param>
    public void SetExpanded(bool expanded)
        => IsExpanded = expanded;

    /// <summary>
    /// Reconciles the selection with current archive availability.
    /// </summary>
    /// <param name="archivedRunIds">Archived run identifiers in display order.</param>
    /// <param name="requestedRunId">An archived run explicitly requested by completion navigation.</param>
    public void Refresh(IReadOnlyList<string> archivedRunIds, string? requestedRunId = null)
    {
        ArgumentNullException.ThrowIfNull(archivedRunIds);
        if (requestedRunId is not null && archivedRunIds.Contains(requestedRunId))
        {
            Select(requestedRunId);
            return;
        }

        if (SelectedRunId is not null && archivedRunIds.Contains(SelectedRunId))
            return;

        SelectedRunId = archivedRunIds.Count > 0 ? archivedRunIds[0] : null;
        IsAutomatic = true;
    }
}
