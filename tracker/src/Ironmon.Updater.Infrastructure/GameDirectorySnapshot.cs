using System.Security.Cryptography;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Records actual files and directories so preparation can detect changes before metadata promotion.
/// </summary>
/// <remarks>
/// Initializes an immutable entry description from an inspected installation.
/// </remarks>
/// <param name="Path">The slash-separated installation-relative path.</param>
/// <param name="Content">The file fingerprint, or null for a directory.</param>
internal sealed record GameDirectoryEntry(string Path, GameFileContent? Content);

/// <summary>
/// Takes bounded, content-based snapshots without following redirected filesystem entries.
/// </summary>
internal static class GameDirectorySnapshot
{
    internal const string GitDirectory = ".git";
    private const int MaximumEntries = 200000;

    /// <summary>
    /// Reads every entry, rejecting an existing Git repository and redirected paths.
    /// </summary>
    /// <param name="root">The fully qualified existing installation root.</param>
    /// <param name="cancellationToken">The token used to cancel enumeration and hashing.</param>
    /// <returns>A sorted snapshot containing exact file bytes and empty directories.</returns>
    internal static async Task<IReadOnlyList<GameDirectoryEntry>> ReadAsync(string root, CancellationToken cancellationToken)
    {
        PlainPaths.Full(root);
        var entries = new List<GameDirectoryEntry>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Path.GetFileName(path).Equals(GitDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(UpdaterText.GameDirectorySnapshotAFolderContainingGitMetadataCannotBeAdoptedAs);

                PlainPaths.Child(root, relative);
                if (entries.Count >= MaximumEntries)
                    throw new InvalidDataException(UpdaterText.GameDirectorySnapshotTheInstallationHasTooManyEntriesForAutomaticPreparation);

                if (Directory.Exists(path))
                {
                    pending.Push(path);
                    entries.Add(new GameDirectoryEntry(relative, null));
                }
                else
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var length = stream.Length;
                    var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                    entries.Add(new GameDirectoryEntry(relative, new GameFileContent(length, Convert.ToHexString(digest))));
                }
            }
        }

        return [.. entries.OrderBy(entry => entry.Path, StringComparer.Ordinal)];
    }
}
