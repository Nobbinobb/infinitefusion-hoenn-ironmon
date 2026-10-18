using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Loads the release-generated Hoenn inventory independently of installed game metadata.
/// </summary>
public static class HoennGameBaseline
{
    private const string ResourceName = "Ironmon.Updater.Infrastructure.Baselines.hoenn.json.gz";
    private const string ManifestResourceName = "Ironmon.Updater.Infrastructure.Baselines.hoenn.manifest.json";
    private static readonly JsonSerializerOptions _options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    /// <summary>
    /// Verifies and loads the release's selected game baseline with explicitly enumerated Windows text alternatives.
    /// </summary>
    /// <returns>The immutable release inventory, containing no player files or machine-specific paths.</returns>
    public static GameBaseline Load()
    {
        using var resource = typeof(HoennGameBaseline).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException(UpdaterText.HoennGameBaselineTheReleaseGameInventoryIsMissing);

        using var manifest = typeof(HoennGameBaseline).Assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidDataException(UpdaterText.HoennGameBaselineTheReleaseGameInventoryManifestIsMissing);

        return Load(resource, manifest);
    }

    /// <summary>
    /// Checks inventory content against its trusted release manifest before returning immutable data.
    /// </summary>
    /// <param name="resource">The compressed inventory stream.</param>
    /// <param name="manifestResource">The companion manifest from the same trusted release, never from installed player metadata.</param>
    /// <returns>The verified release inventory.</returns>
    internal static GameBaseline Load(Stream resource, Stream manifestResource)
    {
        var manifest = JsonSerializer.Deserialize<BaselineManifest>(manifestResource, _options)
            ?? throw new InvalidDataException(UpdaterText.HoennGameBaselineTheReleaseGameInventoryManifestIsEmpty);
        using var compressed = new MemoryStream();
        resource.CopyTo(compressed);
        if (Convert.ToHexString(SHA256.HashData(compressed.ToArray())) != manifest.Sha256)
            throw new InvalidDataException(UpdaterText.HoennGameBaselineTheGameInventoryDoesNotMatchItsReleaseChecksum);

        compressed.Position = 0;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        var baseline = JsonSerializer.Deserialize<GameBaseline>(gzip, _options)
            ?? throw new InvalidDataException(UpdaterText.HoennGameBaselineTheReleaseGameInventoryIsEmpty);
        if (baseline.Commit != manifest.Commit || baseline.Files.Count != manifest.FileCount || manifest.FileCount <= 0)
            throw new InvalidDataException(UpdaterText.HoennGameBaselineTheGameInventoryDoesNotMatchItsReleaseIdentity);

        return baseline with { Files = Array.AsReadOnly(baseline.Files.ToArray()) };
    }

    /// <summary>
    /// Describes the inventory generated from the same game revision as the release.
    /// </summary>
    /// <remarks>
    /// Initializes the generated identity and checksum embedded with the inventory.
    /// </remarks>
    /// <param name="Commit">The exact game commit selected for the release.</param>
    /// <param name="FileCount">The number of files in its complete tree.</param>
    /// <param name="Sha256">The SHA-256 digest of the compressed inventory.</param>
    private sealed record BaselineManifest(string Commit, int FileCount, string Sha256);
}
