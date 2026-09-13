using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies release-specific inventory identities and rejects mismatched generated resources.
/// </summary>
public sealed class HoennGameBaselineTests
{
    private const string FirstCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecondCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string FilePath = "Game.ini";
    private const string Mode = "100644";
    private const string EmptyDigest = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

    /// <summary>
    /// Accepts different release commits without any version-specific loader constants.
    /// </summary>
    /// <param name="commit">The commit selected for the synthetic release.</param>
    [Theory]
    [InlineData(FirstCommit)]
    [InlineData(SecondCommit)]
    public void LoadsSelectedReleaseCommit(string commit)
    {
        using var inventory = Inventory(commit);
        using var manifest = Manifest(inventory, commit, 1);
        var baseline = HoennGameBaseline.Load(inventory, manifest);
        Assert.Equal(commit, baseline.Commit);
        Assert.Single(baseline.Files);
    }

    /// <summary>
    /// Rejects an inventory paired with another release's manifest before parsing its contents.
    /// </summary>
    [Fact]
    public void RejectsResourcesFromDifferentReleases()
    {
        using var first = Inventory(FirstCommit);
        using var second = Inventory(SecondCommit);
        using var manifest = Manifest(first, FirstCommit, 1);
        Assert.Throws<InvalidDataException>(() => HoennGameBaseline.Load(second, manifest));
    }

    /// <summary>
    /// Rejects mismatched identities even when the manifest contains the inventory's correct checksum.
    /// </summary>
    /// <param name="commit">The manifest's expected game commit.</param>
    /// <param name="count">The manifest's expected file count.</param>
    [Theory]
    [InlineData(SecondCommit, 1)]
    [InlineData(FirstCommit, 2)]
    public void RejectsIncorrectReleaseIdentity(string commit, int count)
    {
        using var inventory = Inventory(FirstCommit);
        using var manifest = Manifest(inventory, commit, count);
        Assert.Throws<InvalidDataException>(() => HoennGameBaseline.Load(inventory, manifest));
    }

    /// <summary>
    /// Creates a compressed synthetic inventory for one release revision.
    /// </summary>
    /// <param name="commit">The inventory's game commit.</param>
    /// <returns>A caller-owned compressed stream positioned at its start.</returns>
    private static MemoryStream Inventory(string commit)
    {
        var file = new GameBaselineFile(FilePath, Mode, FirstCommit, new GameFileContent(0, EmptyDigest), null);
        var baseline = new GameBaseline(commit, [file]);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
            JsonSerializer.Serialize(gzip, baseline);

        return new MemoryStream(output.ToArray());
    }

    /// <summary>
    /// Creates trusted fixture metadata without altering the inventory stream's position.
    /// </summary>
    /// <param name="inventory">The inventory whose compressed checksum should be recorded.</param>
    /// <param name="commit">The expected game commit.</param>
    /// <param name="count">The expected file count.</param>
    /// <returns>A caller-owned JSON manifest stream.</returns>
    private static MemoryStream Manifest(MemoryStream inventory, string commit, int count)
    {
        var manifest = new { Commit = commit, FileCount = count, Sha256 = Convert.ToHexString(SHA256.HashData(inventory.ToArray())) };
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest));
    }
}
