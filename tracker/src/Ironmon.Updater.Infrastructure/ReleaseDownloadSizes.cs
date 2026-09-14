namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reads optional download estimates from the existing signed metadata container without changing its wire schema.
/// </summary>
public static class ReleaseDownloadSizes
{
    internal const string FileName = "download-sizes.json";
    internal const string DocumentType = "download-sizes";
    private const long MaximumGameBytes = 100L * 1024 * 1024 * 1024;

    /// <summary>
    /// Reads the pipeline's compressed snapshot estimate for the exact selected game commit.
    /// </summary>
    /// <param name="manifest">The already authenticated release manifest.</param>
    /// <param name="downloads">The shared authenticated metadata cache.</param>
    /// <param name="gameCommit">The selected supported game commit.</param>
    /// <param name="cancellationToken">The metadata-read token.</param>
    /// <returns>The estimate, or null for older releases or a different supported game commit.</returns>
    public static async Task<long?> ReadGameAsync(ReleaseManifest manifest, ReleaseDownloadStore downloads, string gameCommit, CancellationToken cancellationToken = default)
    {
        var asset = manifest.Assets.FirstOrDefault(item => item.Container is not null);
        if (asset?.Container is not { } container)
            return null;

        var path = await downloads.GetAsync(new ReleaseAsset(container.Name, ReleaseMetadata.Role, asset.Url, container.Bytes, container.Sha256), cancellationToken).ConfigureAwait(false);
        var metadata = ReleaseMetadata.Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), container);
        return Read(metadata, manifest.Game.PreferredCommit, gameCommit);
    }

    /// <summary>
    /// Validates the optional estimate only after the enclosing metadata has been authenticated.
    /// </summary>
    /// <param name="metadata">The verified metadata container.</param>
    /// <param name="preferredCommit">The release's pinned preferred game commit.</param>
    /// <param name="selectedCommit">The game commit whose estimate is needed.</param>
    /// <returns>The matching estimate, or null when none was published.</returns>
    internal static long? Read(ReleaseMetadataDocument? metadata, string preferredCommit, string selectedCommit)
    {
        var file = metadata?.Files.SingleOrDefault(item => item.Name == FileName);
        if (file is null)
            return null;

        var sizes = ReleaseJson.Parse<ReleaseDownloadSizeDocument>(file.Bytes, ReleaseJson.ManifestLimit);
        if (sizes.DocumentType != DocumentType || sizes.SchemaVersion != 1 || sizes.GameCommit != preferredCommit || sizes.GameDownloadBytes is <= 0 or > MaximumGameBytes)
            throw new InvalidDataException("The release download estimate does not match its pinned game version.");

        return selectedCommit == sizes.GameCommit ? sizes.GameDownloadBytes : null;
    }
}

/// <summary>
/// Carries a measured compressed game snapshot estimate inside an authenticated release metadata container.
/// </summary>
/// <remarks>Constructs informational sizing data; it never authorizes installation content or changes download limits.</remarks>
/// <param name="DocumentType">The fixed document discriminator.</param>
/// <param name="SchemaVersion">The size-document version.</param>
/// <param name="GameCommit">The exact measured game snapshot.</param>
/// <param name="GameDownloadBytes">The compressed full-snapshot estimate; actual Git transfer can differ.</param>
internal sealed record ReleaseDownloadSizeDocument(string DocumentType, int SchemaVersion, string GameCommit, long GameDownloadBytes);
