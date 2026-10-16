using System.Text.Json;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Locates the signed current release without letting unrelated damaged history block an installation.
/// </summary>
internal static class InstalledReleaseEvidence
{
    private const string EvidencePattern = "*.json";

    /// <summary>
    /// Searches bounded authorization documents and accepts only evidence matching the installed receipt and embedded trust.
    /// </summary>
    /// <param name="root">The current installation root, which may have moved after a completed update.</param>
    /// <param name="receipt">The installed release reference, which grants no ownership by itself.</param>
    /// <param name="verifier">The independently configured release verifier.</param>
    /// <returns>The authenticated matching authorization with its current destination.</returns>
    internal static IronmonUpdateAuthorization Read(string root, IronmonInstalledReceipt receipt, ReleaseVerifier verifier)
    {
        var directory = PlainPaths.Child(root, IronmonOnlyUpdate.EvidenceDirectory);
        if (receipt.SchemaVersion != 1 || !Directory.Exists(directory))
            throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordsAreMissingUse);

        foreach (var path in Directory.EnumerateFiles(directory, EvidencePattern))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), TransactionStorage.GuidFormat, out var id))
                continue;

            try
            {
                var authorization = ReleaseJson.Parse<IronmonUpdateAuthorization>(TransactionStorage.ReadBytes(IronmonOnlyUpdate.AuthorizationPath(root, id)), 64 * ReleaseJson.ManifestLimit);
                if (TransactionStorage.Hash(authorization.Target.Manifest) != receipt.ManifestSha256)
                    continue;

                var manifest = verifier.Verify(authorization.Target.Manifest, authorization.Target.Signatures);
                if (authorization.SchemaVersion != 1 || manifest.IronmonVersion != receipt.Version || authorization.Request.Flavor != receipt.Flavor || authorization.Request.GameCommit != receipt.GameCommit)
                    continue;

                return authorization with { Request = authorization.Request with { InstallationRoot = PlainPaths.Full(root) } };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
            }
        }

        throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordCouldNotBe);
    }
}
