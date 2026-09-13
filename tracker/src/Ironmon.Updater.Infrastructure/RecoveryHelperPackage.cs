using System.IO.Compression;
using System.Security.Cryptography;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Stages immutable helper versions outside the tracker so an update never overwrites its executing helper.
/// </summary>
internal static class RecoveryHelperPackage
{
    internal const string DirectoryName = "recovery";
    private const string TemporaryPrefix = "stage-";
    private const long MaximumHelperBytes = 256L * 1024 * 1024;
    private const string EmbeddedHelperPath = "Ironmon Tracker/Updater/Ironmon.Updater.exe";

    /// <summary>
    /// Resolves retained helper bytes directly from an authenticated tracker inventory for offline recovery.
    /// </summary>
    /// <param name="manifest">The independently authenticated release.</param>
    /// <param name="inventory">Its independently authenticated selected tracker inventory.</param>
    /// <returns>The signed helper identity, or null for older metadata without an embedded helper entry.</returns>
    internal static StagedRecoveryHelper? ResolveIdentity(ReleaseManifest manifest, ReleaseFileInventory inventory)
    {
        var file = inventory.Files.SingleOrDefault(file => file.Path == EmbeddedHelperPath);
        if (manifest.Helper is { } helper)
        {
            if (file is null || ReleaseProtocol.Content(file.Bytes, file.Sha256) != ReleaseProtocol.Content(helper.Bytes, helper.Sha256))
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageThePackageInventoryDisagreesWithTheSignedEmbeddedHelper);

            return new StagedRecoveryHelper(helper.Version, ReleaseProtocol.Content(helper.Bytes, helper.Sha256));
        }

        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.UpdaterRole);
        return file is null ? null : new StagedRecoveryHelper(ReleaseProtocol.HelperVersion(asset.Name), ReleaseProtocol.Content(file.Bytes, file.Sha256));
    }

    /// <summary>
    /// Extracts the single executable from a signed archive and retains all previous helper versions.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="manifest">The authenticated release manifest.</param>
    /// <param name="downloads">The verified download cache.</param>
    /// <param name="flavor">The selected tracker package, reused when obtaining the embedded helper.</param>
    /// <param name="cancellationToken">The staging token.</param>
    /// <returns>The versioned helper and its authenticated extracted content.</returns>
    internal static async Task<StagedRecoveryHelper> StageAsync(string root, ReleaseManifest manifest, ReleaseDownloadStore downloads, string flavor, CancellationToken cancellationToken)
    {
        if (manifest.Helper is not null)
            return await StageEmbeddedAsync(root, manifest, downloads, flavor, cancellationToken).ConfigureAwait(false);

        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.UpdaterRole);
        var version = ReleaseProtocol.HelperVersion(asset.Name);
        var archivePath = await downloads.GetAsync(asset, cancellationToken).ConfigureAwait(false);
        GameFileContent content;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            if (archive.Entries.Count != 1 || archive.Entries[0].FullName != UpdaterHandoff.HelperFileName || archive.Entries[0].Length is <= 0 or > MaximumHelperBytes)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheAuthenticatedHelperPackageMustContainOneBoundedUpdater);

            await using var stream = archive.Entries[0].Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long size = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                size += read;
                if (size > archive.Entries[0].Length)
                    throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheHelperExceedsItsDeclaredExpandedSize);

                hash.AppendData(buffer, 0, read);
            }

            if (size != archive.Entries[0].Length)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheHelperPackageIsIncomplete);

            content = new GameFileContent(size, Convert.ToHexString(hash.GetHashAndReset()));
        }

        var parent = PlainPaths.Child(root, InstallationLease.StateDirectory + '/' + DirectoryName);
        Directory.CreateDirectory(parent);
        var destination = PlainPaths.Child(parent, version);
        if (Directory.Exists(destination))
        {
            PlainPaths.CheckTree(destination);
            if (Directory.EnumerateFileSystemEntries(destination).Count() != 1 || await TransactionStorage.ContentAsync(PlainPaths.Child(destination, UpdaterHandoff.HelperFileName), cancellationToken).ConfigureAwait(false) != content)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageAnExistingImmutableUpdaterVersionHasDifferentContent);

            return new StagedRecoveryHelper(version, content);
        }

        var temporary = PlainPaths.Child(parent, TemporaryPrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        try
        {
            var file = new ReleaseFile(UpdaterHandoff.HelperFileName, content.Sha256, content.Length, ReleaseProtocol.IronmonScope, ReleaseProtocol.ReplacePolicy);
            await ReleaseArchive.ExtractAsync(archivePath, ReleaseProtocol.Content(asset.Bytes, asset.Sha256), temporary, [file], cancellationToken).ConfigureAwait(false);
            Directory.Move(temporary, destination);
            return new StagedRecoveryHelper(version, content);
        }
        finally
        {
            if (Directory.Exists(temporary))
                PlainPaths.DeleteOwned(parent, temporary);
        }
    }

    /// <summary>
    /// Stages only the signed executable from the selected player ZIP, reusing its existing verified download.
    /// </summary>
    /// <param name="root">The owned installation or private staging root.</param>
    /// <param name="manifest">The independently authenticated compact release.</param>
    /// <param name="downloads">The verified package cache.</param>
    /// <param name="flavor">The selected package flavor.</param>
    /// <param name="cancellationToken">The staging token.</param>
    /// <returns>The retained immutable helper identity.</returns>
    private static async Task<StagedRecoveryHelper> StageEmbeddedAsync(string root, ReleaseManifest manifest, ReleaseDownloadStore downloads, string flavor, CancellationToken cancellationToken)
    {
        var helper = manifest.Helper ?? throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheEmbeddedHelperIdentityIsMissing);
        var content = ReleaseProtocol.Content(helper.Bytes, helper.Sha256);
        var parent = PlainPaths.Child(root, InstallationLease.StateDirectory + '/' + DirectoryName);
        var destination = PlainPaths.Child(parent, helper.Version);
        if (Directory.Exists(destination))
        {
            PlainPaths.CheckTree(destination);
            if (Directory.EnumerateFileSystemEntries(destination).Count() != 1 || await TransactionStorage.ContentAsync(PlainPaths.Child(destination, UpdaterHandoff.HelperFileName), cancellationToken).ConfigureAwait(false) != content)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageAnExistingImmutableUpdaterVersionHasDifferentContent);

            return new StagedRecoveryHelper(helper.Version, content);
        }

        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + flavor);
        var archivePath = await downloads.GetAsync(asset, cancellationToken).ConfigureAwait(false);
        var temporary = PlainPaths.Child(parent, TemporaryPrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        Directory.CreateDirectory(temporary);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry[] entries = [.. archive.Entries.Where(entry => entry.FullName.Equals(EmbeddedHelperPath, StringComparison.OrdinalIgnoreCase))];
            if (archive.Entries.Count > 200000 || entries.Length != 1 || entries[0].FullName != EmbeddedHelperPath || entries[0].Length != helper.Bytes || helper.Bytes is <= 0 or > MaximumHelperBytes)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageThePlayerPackageHasNoUniqueBoundedEmbeddedHelper);

            var entry = entries[0];
            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if (type is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheEmbeddedHelperCannotBeAnArchiveLink);

            var target = PlainPaths.Child(temporary, UpdaterHandoff.HelperFileName);
            await using (var input = entry.Open())
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    received = checked(received + count);
                    if (received > helper.Bytes)
                        throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheEmbeddedHelperExceedsItsSignedSize);

                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }

                output.Flush(true);
            }

            if (await TransactionStorage.ContentAsync(target, cancellationToken).ConfigureAwait(false) != content)
                throw new InvalidDataException(UpdaterText.RecoveryHelperPackageTheEmbeddedHelperDiffersFromItsSignedExecutableIdentity);

            Directory.Move(temporary, destination);
            return new StagedRecoveryHelper(helper.Version, content);
        }
        finally
        {
            if (Directory.Exists(temporary))
                PlainPaths.DeleteOwned(parent, temporary);
        }
    }
}

/// <summary>
/// Identifies an authenticated executable in an immutable helper version directory.
/// </summary>
/// <remarks>
/// Constructs the bounded helper identity used by the tracker handoff.
/// </remarks>
/// <param name="Version">The numeric helper version.</param>
/// <param name="Content">The extracted executable fingerprint.</param>
internal sealed record StagedRecoveryHelper(string Version, GameFileContent Content);
