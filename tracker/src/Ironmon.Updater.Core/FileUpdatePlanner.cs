namespace Ironmon.Updater.Core;

/// <summary>
/// Builds deterministic three-way file plans without reading or changing an installation.
/// </summary>
/// <remarks>
/// Initializes the planner with trusted protection rules shared by every component inventory.
/// </remarks>
/// <param name="policy">The application-owned file policy, including runtime-discovered protected paths.</param>
public sealed class FileUpdatePlanner(FileManagementPolicy policy)
{
    private const int MaximumEntries = 200000;
    private readonly FileManagementPolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    /// <summary>
    /// Copies the trusted policy for a recoverable reviewed specification.
    /// </summary>
    /// <returns>The captured protected paths.</returns>
    internal string[] ExportProtectedPaths()
        => _policy.ExportProtectedPaths();

    /// <summary>
    /// Compares complete composed baseline/target inventories and a complete local snapshot.
    /// </summary>
    /// <remarks>
    /// Inputs must already be authenticated and composed across game and Ironmon ownership. This method rejects duplicate ownership; it never guesses which overlapping package wins.
    /// </remarks>
    /// <param name="baseline">The complete previously managed files for every participating component.</param>
    /// <param name="local">Every inspected file and directory inside the installation, excluding separately managed Git/updater metadata.</param>
    /// <param name="target">The complete desired files for the same participating components.</param>
    /// <returns>An immutable review plan that blocks all operations while any conflict remains.</returns>
    public FileUpdatePlan Create(IEnumerable<ManagedFile> baseline, IEnumerable<LocalFileEntry> local, IEnumerable<ManagedFile> target)
        => Build(baseline, local, target, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Rebuilds decisions using consent bound to the same immutable inputs.
    /// </summary>
    /// <param name="baseline">The prior managed files.</param>
    /// <param name="local">The original local snapshot.</param>
    /// <param name="target">The desired managed files.</param>
    /// <param name="approved">Exact conflict paths explicitly approved for replacement after backup.</param>
    /// <returns>The re-evaluated immutable plan.</returns>
    internal FileUpdatePlan Build(IEnumerable<ManagedFile> baseline, IEnumerable<LocalFileEntry> local, IEnumerable<ManagedFile> target, IReadOnlySet<string> approved)
    {
        var before = ReadManaged(baseline, false);
        var after = ReadManaged(target, true);
        var current = ReadLocal(local);
        ValidateCasing(before.Keys.Concat(after.Keys).Concat(current.Keys));
        var paths = before.Keys.Concat(after.Keys).Concat(current.Values.Where(entry => entry.Content is not null).Select(entry => entry.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal);
        var decisions = new Dictionary<string, FilePlanEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            before.TryGetValue(path, out var oldFile);
            after.TryGetValue(path, out var newFile);
            current.TryGetValue(path, out var localFile);
            var entry = Decide(path, oldFile, localFile, newFile);
            if (entry.Action == FilePlanAction.Conflict && entry.CanReplaceAfterBackup && approved.Contains(path))
                entry = entry with { Action = newFile is null ? FilePlanAction.Delete : FilePlanAction.Write, Conflict = FileConflictKind.None, CanReplaceAfterBackup = false };

            decisions.Add(path, entry);
        }

        var operations = BuildOperations(before, current, decisions);
        return new FileUpdatePlan(this, before.Values, current.Values, after.Values, approved, decisions.Values, operations);
    }

    /// <summary>
    /// Validates and indexes complete managed inventories, including ownership and ancestor collisions.
    /// </summary>
    /// <param name="files">The authenticated entries to index.</param>
    /// <param name="target">Whether the entries describe the new release.</param>
    /// <returns>A case-insensitive index with unambiguous original spelling.</returns>
    private Dictionary<string, ManagedFile> ReadManaged(IEnumerable<ManagedFile> files, bool target)
    {
        ArgumentNullException.ThrowIfNull(files);
        var result = new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            _policy.Validate(file, target);
            if (result.Count >= MaximumEntries || !result.TryAdd(file.Path, file))
                throw new InvalidDataException(UpdaterText.FileUpdatePlannerTheInventoryIsTooLargeOrClaimsAPath);
        }

        foreach (var path in result.Keys)
        {
            if (UpdateFilePath.Parents(path).Any(result.ContainsKey))
                throw new InvalidDataException(UpdaterText.FileUpdatePlannerTheInventoryUsesOnePathAsBothAFile);
        }

        return result;
    }

    /// <summary>
    /// Validates complete snapshots and ensures every file's directories are explicitly represented.
    /// </summary>
    /// <param name="entries">The inspected installation entries.</param>
    /// <returns>A case-insensitive snapshot index.</returns>
    internal static Dictionary<string, LocalFileEntry> ReadLocal(IEnumerable<LocalFileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var result = new Dictionary<string, LocalFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            UpdateFilePath.Validate(entry.Path);
            if (entry.Content is not null)
                UpdateFilePath.ValidateContent(entry.Content);

            if (result.Count >= MaximumEntries || !result.TryAdd(entry.Path, entry))
                throw new InvalidDataException(UpdaterText.FileUpdatePlannerTheInstallationSnapshotIsTooLargeOrContainsDuplicate);
        }

        foreach (var path in result.Keys)
        {
            if (UpdateFilePath.Parents(path).Any(parent => !result.TryGetValue(parent, out var entry) || entry.Content is not null))
                throw new InvalidDataException(UpdaterText.FileUpdatePlannerTheInstallationSnapshotHasMissingDirectoriesOrAFile);
        }

        ValidateCasing(result.Keys);
        return result;
    }

    /// <summary>
    /// Rejects case-only renames and inconsistent directory spelling across all three inputs.
    /// </summary>
    /// <param name="paths">The paths and their implicit parent directories.</param>
    private static void ValidateCasing(IEnumerable<string> paths)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.SelectMany(path => UpdateFilePath.Parents(path).Prepend(path)))
        {
            if (names.TryGetValue(path, out var existing) && existing != path)
                throw new InvalidDataException(UpdaterText.FileUpdatePlannerCaseOnlyPathChangesRequireASeparateSupportedRename);

            names[path] = path;
        }
    }

    /// <summary>
    /// Applies the three-way content rules before considering directory transitions.
    /// </summary>
    /// <param name="path">The common file path.</param>
    /// <param name="baseline">The previous managed content, if any.</param>
    /// <param name="local">The current file or directory, if any.</param>
    /// <param name="target">The desired managed content, if any.</param>
    /// <returns>A file decision retaining exact before/after evidence.</returns>
    private static FilePlanEntry Decide(string path, ManagedFile? baseline, LocalFileEntry? local, ManagedFile? target)
    {
        var entry = new FilePlanEntry(path, baseline, local, target, FilePlanAction.Keep);
        if (baseline is not null && target is not null && (baseline.Owner != target.Owner || baseline.Policy != target.Policy))
            throw new InvalidDataException(UpdaterText.FileUpdatePlannerChangingFileOwnershipOrRetentionPolicyRequiresAnExplicit);

        if (baseline?.Policy == ManagedFilePolicy.Retain && target is not null && baseline.Content != target.Content)
            throw new InvalidDataException(UpdaterText.FileUpdatePlannerAReleaseAttemptsToChangeAnImmutableFileUnder);

        if (baseline is not null && FileManagementPolicy.IsLegacyDocument(baseline))
            return entry;

        if (target?.Policy == ManagedFilePolicy.Retain)
        {
            if (local is null)
                return entry with { Action = FilePlanAction.Write };

            return Matches(local.Content, target) ? entry : Conflict(entry, FileConflictKind.RetainedContent, false);
        }

        if (baseline?.Policy == ManagedFilePolicy.Retain || baseline?.Content == target?.Content || ((local is null || local.Content is not null) && Matches(local?.Content, target)))
            return entry;

        if (local?.Content is null && local is not null && target is null)
            return Conflict(entry, FileConflictKind.PathCollision, false);

        if ((local is null && baseline is null) || Matches(local?.Content, baseline))
            return entry with { Action = target is null ? FilePlanAction.Delete : FilePlanAction.Write };

        if (local is not null && local.Content is null && target is not null)
            return entry with { Action = FilePlanAction.Write };

        if (baseline is null)
            return Conflict(entry, FileConflictKind.NewFileCollision, true);

        if (target is null)
            return Conflict(entry, FileConflictKind.ModifiedObsoleteFile, true);

        return Conflict(entry, local is null ? FileConflictKind.LocalDeletion : FileConflictKind.LocalModification, true);
    }

    /// <summary>
    /// Tests exact content against canonical and explicitly allowed alternative bytes.
    /// </summary>
    /// <param name="local">The observed content or absence.</param>
    /// <param name="file">The managed content or absence.</param>
    /// <returns>Whether the observed content matches the managed expectation.</returns>
    private static bool Matches(GameFileContent? local, ManagedFile? file)
        => file is null ? local is null : local is not null && (local == file.Content || local == file.AlternateContent);

    /// <summary>
    /// Creates an unresolved review entry without authorizing any filesystem action.
    /// </summary>
    /// <param name="entry">The original decision evidence.</param>
    /// <param name="kind">The conflict reason.</param>
    /// <param name="replaceable">Whether explicit replacement consent is permitted.</param>
    /// <returns>The blocked decision.</returns>
    private static FilePlanEntry Conflict(FilePlanEntry entry, FileConflictKind kind, bool replaceable)
        => entry with { Action = FilePlanAction.Conflict, Conflict = kind, CanReplaceAfterBackup = replaceable };

    /// <summary>
    /// Orders exact file operations and allows directory transitions only after all affected content is accounted for.
    /// </summary>
    /// <param name="baseline">The original ownership index.</param>
    /// <param name="local">The complete local snapshot.</param>
    /// <param name="decisions">The decisions to annotate with structural conflicts.</param>
    /// <returns>Proposed operations, inaccessible to callers until every conflict is resolved.</returns>
    private IReadOnlyList<PlannedFileOperation> BuildOperations(Dictionary<string, ManagedFile> baseline, Dictionary<string, LocalFileEntry> local, Dictionary<string, FilePlanEntry> decisions)
    {
        var deletes = decisions.Values.Where(entry => entry.Action == FilePlanAction.Delete && entry.Local?.Content is not null).ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var writes = decisions.Values.Where(entry => entry.Action == FilePlanAction.Write).ToArray();
        var oldDirectories = baseline.Keys.SelectMany(UpdateFilePath.Parents).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directoryWrites = writes.Where(entry => entry.Local is { Content: null }).ToDictionary(entry => entry.Path, _ => new List<LocalFileEntry>(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in local.Values)
        {
            foreach (var parent in UpdateFilePath.Parents(entry.Path))
            {
                if (directoryWrites.TryGetValue(parent, out var children))
                    children.Add(entry);
            }
        }

        var removeDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operations = new List<PlannedFileOperation>();
        foreach (var write in writes)
        {
            var blockedParent = UpdateFilePath.Parents(write.Path).Any(parent => local.TryGetValue(parent, out var entry) && entry.Content is not null && !deletes.ContainsKey(parent));
            if (blockedParent)
            {
                decisions[write.Path] = Conflict(write, FileConflictKind.PathCollision, false);
                continue;
            }

            if (directoryWrites.TryGetValue(write.Path, out var descendants))
            {
                var removesUnknown = !oldDirectories.Contains(write.Path) || descendants.Any(entry => _policy.IsProtected(entry.Path) || (entry.Content is null ? !oldDirectories.Contains(entry.Path) : !deletes.ContainsKey(entry.Path)));
                if (removesUnknown)
                {
                    decisions[write.Path] = Conflict(write, FileConflictKind.PathCollision, false);
                    continue;
                }

                removeDirectories.Add(write.Path);
                removeDirectories.UnionWith(descendants.Where(entry => entry.Content is null).Select(entry => entry.Path));
            }

            createDirectories.UnionWith(UpdateFilePath.Parents(write.Path).Where(parent => !local.TryGetValue(parent, out var entry) || entry.Content is not null));
            operations.Add(new PlannedFileOperation(write.Path, FileOperationKind.WriteFile, write.Local?.Content, write.Target!.Content));
        }

        operations.AddRange(deletes.Values.Select(entry => new PlannedFileOperation(entry.Path, FileOperationKind.DeleteFile, entry.Local!.Content, null)));
        operations.AddRange(removeDirectories.Select(path => new PlannedFileOperation(path, FileOperationKind.RemoveDirectory, null, null)));
        operations.AddRange(createDirectories.Select(path => new PlannedFileOperation(path, FileOperationKind.CreateDirectory, null, null)));
        var bootstrapParentsExist = UpdateFilePath.Parents(FileManagementPolicy.BootstrapPath).All(path => local.TryGetValue(path, out var entry) && entry.Content is null);
        return [.. operations.OrderBy(operation => bootstrapParentsExist && operation.Path == FileManagementPolicy.BootstrapPath && operation.Kind == FileOperationKind.WriteFile ? 0 : 1).ThenBy(operation => operation.Kind).ThenBy(operation => operation.Kind == FileOperationKind.RemoveDirectory ? -operation.Path.Count(character => character == '/') : operation.Path.Count(character => character == '/')).ThenBy(operation => operation.Path, StringComparer.Ordinal)];
    }
}
