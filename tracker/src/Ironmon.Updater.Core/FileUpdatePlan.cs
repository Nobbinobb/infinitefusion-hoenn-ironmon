namespace Ironmon.Updater.Core;

/// <summary>
/// Holds immutable review decisions, backup requirements and complete revalidation expectations.
/// </summary>
public sealed class FileUpdatePlan
{
    private readonly FileUpdatePlanner _planner;
    private readonly IReadOnlyList<ManagedFile> _baseline;
    private readonly IReadOnlyList<ManagedFile> _target;
    private readonly IReadOnlyList<LocalFileEntry> _before;
    private readonly IReadOnlyList<LocalFileEntry> _after;
    private readonly IReadOnlyList<PlannedFileOperation> _operations;
    private readonly HashSet<string> _approved;

    /// <summary>
    /// Gets the ordered decisions, including preserved local edits and unresolved conflicts.
    /// </summary>
    public IReadOnlyList<FilePlanEntry> Entries { get; }

    /// <summary>
    /// Exports the reviewed inputs for reconstruction by an independently authorized recovery process.
    /// </summary>
    /// <returns>A detached specification; serialization alone does not authorize applying it.</returns>
    public FilePlanSpecification Export()
        => new([.. _baseline], [.. _before], [.. _target], [.. _approved.Order(StringComparer.Ordinal)], _planner.ExportProtectedPaths());

    /// <summary>
    /// Gets whether all conflicts are resolved; application still requires fresh snapshot and backup checks.
    /// </summary>
    public bool CanApply => Entries.All(entry => entry.Action != FilePlanAction.Conflict);

    /// <summary>
    /// Freezes planner inputs and calculates the complete expected result for repeat-application checks.
    /// </summary>
    /// <param name="planner">The planner retaining trusted policy.</param>
    /// <param name="baseline">The complete prior inventory.</param>
    /// <param name="local">The complete inspected state.</param>
    /// <param name="target">The complete desired inventory.</param>
    /// <param name="approved">The exact consented conflict paths.</param>
    /// <param name="entries">The ordered review decisions.</param>
    /// <param name="operations">The proposed mutation sequence.</param>
    internal FileUpdatePlan(FileUpdatePlanner planner, IEnumerable<ManagedFile> baseline, IEnumerable<LocalFileEntry> local, IEnumerable<ManagedFile> target, IEnumerable<string> approved, IEnumerable<FilePlanEntry> entries, IEnumerable<PlannedFileOperation> operations)
    {
        _planner = planner;
        _baseline = Array.AsReadOnly(baseline.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
        _target = Array.AsReadOnly(target.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
        _before = Array.AsReadOnly(local.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray());
        _approved = new HashSet<string>(approved, StringComparer.Ordinal);
        Entries = Array.AsReadOnly(entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray());
        _operations = Array.AsReadOnly(operations.ToArray());
        _after = ExpectedResult();
    }

    /// <summary>
    /// Approves explicit file conflicts for replacement after verified backup, without changing the original plan.
    /// </summary>
    /// <remarks>
    /// Consent applies only to these captured local bytes. New changes require a fresh plan and renewed conflict review. Protected or structural collisions cannot be overridden here; cancel or move the obstruction and replan.
    /// </remarks>
    /// <param name="paths">The exact review paths the user approved, with no wildcard or blanket approval.</param>
    /// <returns>A new plan; every remaining unresolved conflict still blocks all operations.</returns>
    public FileUpdatePlan ApproveReplacements(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var approved = new HashSet<string>(_approved, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!Entries.Any(entry => entry.Path == path && entry.Action == FilePlanAction.Conflict && entry.CanReplaceAfterBackup))
                throw new InvalidOperationException(UpdaterText.FileUpdatePlanOnlyAnExplicitlyListedReplaceableFileConflictCanBe);

            approved.Add(path);
        }

        return _planner.Build(_baseline, _before, _target, approved);
    }

    /// <summary>
    /// Returns ordered operations only after the whole update is free of unresolved conflicts.
    /// </summary>
    /// <returns>Exact non-recursive operations requiring a later transaction, fresh revalidation and verified backups.</returns>
    public IReadOnlyList<PlannedFileOperation> GetOperations()
    {
        if (!CanApply)
            throw new InvalidOperationException(UpdaterText.FileUpdatePlanUnresolvedFileConflictsBlockTheCompleteUpdate);

        return _operations;
    }

    /// <summary>
    /// Compares a fresh complete snapshot with both the reviewed state and the complete expected result.
    /// </summary>
    /// <remarks>
    /// AlreadyApplied requires every expected file and preserved entry to match. Partial application returns Changed and belongs to transaction recovery; matching one target file is never sufficient to replay a plan.
    /// </remarks>
    /// <param name="current">A newly inspected complete installation snapshot.</param>
    /// <returns>Whether to apply once, skip an already completed plan, replan/recover, or resolve conflicts.</returns>
    public FilePlanApplicability Assess(IEnumerable<LocalFileEntry> current)
    {
        if (!CanApply)
            return FilePlanApplicability.Blocked;

        var snapshot = FileUpdatePlanner.ReadLocal(current).Values.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        if (_before.SequenceEqual(snapshot))
            return _operations.Count == 0 ? FilePlanApplicability.AlreadyApplied : FilePlanApplicability.Ready;

        return _after.SequenceEqual(snapshot) ? FilePlanApplicability.AlreadyApplied : FilePlanApplicability.Changed;
    }

    /// <summary>
    /// Derives final file and directory expectations while retaining every untouched entry.
    /// </summary>
    /// <returns>The complete immutable expected snapshot.</returns>
    private IReadOnlyList<LocalFileEntry> ExpectedResult()
    {
        var result = _before.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var operation in _operations)
        {
            if (operation.Kind is FileOperationKind.DeleteFile or FileOperationKind.RemoveDirectory)
            {
                result.Remove(operation.Path);
            }
            else
            {
                result[operation.Path] = new LocalFileEntry(operation.Path, operation.ExpectedAfter);
            }
        }

        return Array.AsReadOnly(result.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray());
    }
}
