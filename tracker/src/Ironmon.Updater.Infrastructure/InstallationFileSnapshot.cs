using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reads full or inventory-scoped content snapshots without following redirected program paths or writing files.
/// </summary>
public static class InstallationFileSnapshot
{
    private const string GitDirectory = ".git";
    private const string UpdateDirectory = ".ironmon-update";
    private const int MaximumEntries = 200000;

    /// <summary>
    /// Hashes regular installation files and records directories without reading the tracker's private browser state.
    /// </summary>
    /// <remarks>
    /// Root Git and updater directories belong to separate transaction participants and are excluded.
    /// The protected browser directory is recorded as an opaque boundary because the running tracker owns mutable, locked files inside it.
    /// Existing repository validation remains the private Git provider's responsibility. Call again immediately before applying while holding the later transaction lock.
    /// </remarks>
    /// <param name="installationRoot">The fully qualified validated game root.</param>
    /// <param name="cancellationToken">The token used to cancel inspection.</param>
    /// <param name="stage">The purpose displayed while hashing the enumerated files.</param>
    /// <returns>A sorted complete snapshot for the pure file planner and subsequent revalidation.</returns>
    public static async Task<IReadOnlyList<LocalFileEntry>> ReadAsync(string installationRoot, CancellationToken cancellationToken = default, InstallationStage stage = InstallationStage.VerifyingFiles)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = PlainPaths.Full(installationRoot);
        var pending = new Stack<string>();
        var result = new List<LocalFileEntry>();
        var files = new List<(int Index, string Path)>();
        pending.Push(root);
        InstallationProgressScope.Report(new(InstallationStage.EnumeratingFiles));
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                PlainPaths.Child(root, relative);
                var attributes = File.GetAttributes(path);
                if (relative.Equals(GitDirectory, StringComparison.OrdinalIgnoreCase) || relative.Equals(UpdateDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    if ((attributes & FileAttributes.Directory) == 0)
                        throw new InvalidDataException(UpdaterText.InstallationFileSnapshotGitAndUpdaterMetadataMustBeOrdinaryDirectories);

                    continue;
                }

                if (result.Count >= MaximumEntries)
                    throw new InvalidDataException(UpdaterText.InstallationFileSnapshotTheInstallationHasTooManyEntriesForAutomaticFile);

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!relative.Equals(FileManagementPolicy.TrackerBrowserDataPath, StringComparison.OrdinalIgnoreCase))
                        pending.Push(path);

                    result.Add(new LocalFileEntry(relative, null));
                }
                else
                {
                    files.Add((result.Count, path));
                    result.Add(new LocalFileEntry(relative, null));
                }

                InstallationProgressScope.Report(new(InstallationStage.EnumeratingFiles, result.Count));
            }
        }

        return await HashAsync(result, files, stage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inspects authenticated program paths without enumerating or opening unrelated player data.
    /// </summary>
    /// <remarks>
    /// Ancestors are recorded to detect file collisions. A directory occupying a managed file path is inspected completely so replacing it cannot remove unreviewed children.
    /// </remarks>
    /// <param name="installationRoot">The validated installation root.</param>
    /// <param name="paths">The managed files and directory replacement boundaries.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    /// <param name="stage">The reported verification stage.</param>
    /// <returns>The relevant files, their ancestors and structural obstructions.</returns>
    public static Task<IReadOnlyList<LocalFileEntry>> ReadManagedAsync(string installationRoot, IEnumerable<string> paths, CancellationToken cancellationToken = default, InstallationStage stage = InstallationStage.VerifyingFiles)
    {
        var root = PlainPaths.Full(installationRoot);
        var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LocalFileEntry>();
        var files = new List<(int Index, string Path)>();
        var pending = new Stack<string>(selected);
        InstallationProgressScope.Report(new(InstallationStage.EnumeratingFiles));
        while (pending.TryPop(out var relative))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = relative.Split('/');
            for (var index = 1; index <= parts.Length; index++)
            {
                var name = string.Join('/', parts.Take(index));
                var path = PlainPaths.Child(root, name);
                if (!Path.Exists(path))
                    break;

                var directory = Directory.Exists(path);
                if (seen.Add(name))
                {
                    if (result.Count >= MaximumEntries)
                        throw new InvalidDataException(UpdaterText.InstallationFileSnapshotTheInstallationHasTooManyEntriesForAutomaticFile);

                    if (!directory)
                        files.Add((result.Count, path));

                    result.Add(new LocalFileEntry(name, null));
                }

                if (!directory)
                    break;

                if (index == parts.Length)
                    foreach (var child in Directory.EnumerateFileSystemEntries(path))
                        pending.Push(Path.GetRelativePath(root, child).Replace('\\', '/'));
            }
        }

        return HashAsync(result, files, stage, cancellationToken);
    }

    /// <summary>
    /// Hashes the selected regular files with bounded concurrency and ordered progress reporting.
    /// </summary>
    /// <param name="result">The enumerated file and directory entries.</param>
    /// <param name="files">The regular files and their result positions.</param>
    /// <param name="stage">The displayed verification purpose.</param>
    /// <param name="cancellationToken">The hashing token.</param>
    /// <returns>The sorted content snapshot.</returns>
    private static async Task<IReadOnlyList<LocalFileEntry>> HashAsync(List<LocalFileEntry> result, List<(int Index, string Path)> files, InstallationStage stage, CancellationToken cancellationToken)
    {
        var entries = result.ToArray();
        var verified = 0;
        var progressGate = new Lock();
        InstallationProgressScope.Report(new(stage, 0, files.Count));
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = TransactionStorage.FileConcurrency, CancellationToken = cancellationToken }, async (file, token) =>
        {
            entries[file.Index] = entries[file.Index] with { Content = await TransactionStorage.ContentAsync(file.Path, token).ConfigureAwait(false) };
            lock (progressGate)
                InstallationProgressScope.Report(new(stage, ++verified, files.Count));
        }).ConfigureAwait(false);
        return Array.AsReadOnly(entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray());
    }
}
