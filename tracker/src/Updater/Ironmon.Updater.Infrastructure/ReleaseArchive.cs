using System.IO.Compression;
using System.Security.Cryptography;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Extracts hash-verified archives into new private directories with exact signed file inventories.
/// </summary>
public static class ReleaseArchive
{
    private const int MaximumEntries = 200000;
    private const int UnixTypeMask = 0xF000;
    private const int UnixRegularFile = 0x8000;
    private const int UnixDirectory = 0x4000;

    /// <summary>
    /// Verifies the archive fingerprint and every file while rejecting traversal, links, collisions and unlisted content.
    /// </summary>
    /// <param name="archivePath">The downloaded archive.</param>
    /// <param name="expectedArchive">Its authenticated byte identity.</param>
    /// <param name="destination">A new private extraction directory.</param>
    /// <param name="files">The exact signed file inventory.</param>
    /// <param name="cancellationToken">The extraction cancellation token.</param>
    /// <returns>A task completing only after all expected entries are verified.</returns>
    public static async Task ExtractAsync(string archivePath, GameFileContent expectedArchive, string destination, IReadOnlyList<ReleaseFile> files, CancellationToken cancellationToken = default)
    {
        var root = PlainPaths.Full(destination);
        if (Path.Exists(root))
            throw new IOException(UpdaterText.ReleaseArchiveAnUpdateArchiveMustBeExtractedIntoANew);

        if (await TransactionStorage.ContentAsync(archivePath, cancellationToken).ConfigureAwait(false) != expectedArchive)
            throw new InvalidDataException(UpdaterText.ReleaseArchiveTheDownloadedArchiveDoesNotMatchItsSignedFingerprint);

        var expected = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ValidateRelativePath(file.Path);
            foreach (var parent in UpdateFilePath.Parents(file.Path))
                directories.Add(parent);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extracted = new HashSet<string>(StringComparer.Ordinal);
        Directory.CreateDirectory(root);
        try
        {
            using var archive = ZipFile.OpenRead(PlainPaths.Full(archivePath));
            if (archive.Entries.Count > MaximumEntries)
                throw new InvalidDataException(UpdaterText.ReleaseArchiveThePackageArchiveContainsTooManyEntries);

            InstallationProgressScope.Report(new(InstallationStage.ExtractingFiles, 0, expected.Count));
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = entry.FullName.EndsWith('/');
                var relative = directory ? entry.FullName[..^1] : entry.FullName;
                ValidateRelativePath(relative);
                var unixType = (entry.ExternalAttributes >> 16) & UnixTypeMask;
                if (!visited.Add(relative) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || (unixType != 0 && unixType != (directory ? UnixDirectory : UnixRegularFile)))
                    throw new InvalidDataException(UpdaterText.ReleaseArchiveThePackageContainsDuplicatePathsLinksOrUnsupportedFile);

                if (directory)
                {
                    if (entry.Length != 0 || !directories.Contains(relative))
                        throw new InvalidDataException(UpdaterText.ReleaseArchiveThePackageContainsAnUnlistedOrIncorrectlyCasedDirectory);

                    Directory.CreateDirectory(PlainPaths.Child(root, relative));
                    continue;
                }

                if (!expected.TryGetValue(relative, out var file) || entry.Length != file.Bytes)
                    throw new InvalidDataException(UpdaterText.ReleaseArchiveThePackageContainsAnUnlistedFileOrAnUnexpected);

                var target = PlainPaths.Child(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long count = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    count = checked(count + read);
                    if (count > file.Bytes)
                        throw new InvalidDataException(UpdaterText.ReleaseArchiveTheExpandedPackageExceedsItsSignedFileSize);

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                if (count != file.Bytes || Convert.ToHexString(hash.GetHashAndReset()) != ReleaseProtocol.Content(file.Bytes, file.Sha256).Sha256)
                    throw new InvalidDataException(UpdaterText.ReleaseArchiveAnExtractedFileFailedItsSignedContentVerification);

                extracted.Add(relative);
                InstallationProgressScope.Report(new(InstallationStage.ExtractingFiles, extracted.Count, expected.Count));
            }

            if (extracted.Count != expected.Count)
                throw new InvalidDataException(UpdaterText.ReleaseArchiveThePackageIsMissingSignedInventoryFiles);
        }
        catch
        {
            PlainPaths.DeleteOwned(Path.GetDirectoryName(root)!, root);
            throw;
        }
    }

    /// <summary>
    /// Applies the same portable Windows path rules as the transaction planner.
    /// </summary>
    /// <param name="path">The exact archive or inventory path.</param>
    internal static void ValidateRelativePath(string path)
        => UpdateFilePath.Validate(path);
}
