using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Binds the standalone updater to the installation and release that the player selected in the tracker.
/// </summary>
/// <param name="InstallationRoot">The selected installed game's local directory.</param>
/// <param name="Version">The canonical displayed Ironmon version.</param>
/// <param name="ManifestSha256">The exact reviewed release manifest identity.</param>
public sealed record StandaloneUpdateTarget(string InstallationRoot, string Version, string ManifestSha256)
{
    /// <summary>
    /// Identifies the standalone Setup command that opens a review for an existing installation.
    /// </summary>
    public const string Argument = "--update-installation";

    /// <summary>
    /// Reads the fixed version-one launch arguments without accepting file approvals or process authority from them.
    /// </summary>
    /// <param name="args">The arguments passed to the standalone updater.</param>
    /// <returns>The requested installation and exact signed release, or null for normal Setup.</returns>
    public static StandaloneUpdateTarget? ReadArguments(string[] args)
    {
        if (args.Length == 0)
            return null;

        if (args is not [Argument, var root, var version, var hash])
            throw new InvalidDataException(UpdaterText.SetupVersionsMissingMetadata);

        ReleaseProtocol.ParseVersion(version);
        return new StandaloneUpdateTarget(PlainPaths.Full(root), version, ReleaseProtocol.Content(0, hash).Sha256);
    }

    /// <summary>
    /// Ensures a fresh independently verified release still matches the selection shown in the tracker.
    /// </summary>
    /// <param name="release">The freshly authenticated signed release.</param>
    public void RequireMatch(ReleaseEvidence release)
    {
        if (TransactionStorage.Hash(release.Manifest) != ManifestSha256)
            throw new InvalidDataException(UpdaterText.SetupVersionsReleaseChanged);
    }
}
