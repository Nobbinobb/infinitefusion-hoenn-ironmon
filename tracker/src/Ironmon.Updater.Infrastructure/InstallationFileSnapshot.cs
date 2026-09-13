using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reads complete content snapshots for file planning without following redirected paths or writing files.
/// </summary>
public static class InstallationFileSnapshot
{
    private const string GitDirectory = ".git";
    private const string UpdateDirectory = ".ironmon-update";
    private const int MaximumEntries = 200000;

    /// <summary>
    /// Hashes all regular files and records directories, including protected and unrelated content.
    /// </summary>
    /// <remarks>
    /// Root Git and updater directories belong to separate transaction participants and are excluded. Existing repository validation remains the private Git provider's responsibility. Call again immediately before applying while holding the later transaction lock.
    /// </remarks>
    /// <param name="installationRoot">The fully qualified validated game root.</param>
    /// <param name="cancellationToken">The token used to cancel inspection.</param>
    /// <returns>A sorted complete snapshot for the pure file planner and subsequent revalidation.</returns>
    public static async Task<IReadOnlyList<LocalFileEntry>> ReadAsync(string installationRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = PlainPaths.Full(installationRoot);
        var pending = new Stack<string>();
        var result = new List<LocalFileEntry>();
        var files = new List<(int Index, string Path)>();
        pending.Push(root);
        InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles));
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
                    pending.Push(path);
                    result.Add(new LocalFileEntry(relative, null));
                }
                else
                {
                    files.Add((result.Count, path));
                    result.Add(new LocalFileEntry(relative, null));
                }

                InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles, result.Count));
            }
        }

        var entries = result.ToArray();
        var verified = 0;
        var progressGate = new Lock();
        InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles, 0, files.Count));
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = TransactionStorage.FileConcurrency, CancellationToken = cancellationToken }, async (file, token) =>
        {
            entries[file.Index] = entries[file.Index] with { Content = await TransactionStorage.ContentAsync(file.Path, token).ConfigureAwait(false) };
            lock (progressGate)
                InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles, ++verified, files.Count));
        }).ConfigureAwait(false);
        return Array.AsReadOnly(entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray());
    }
}
