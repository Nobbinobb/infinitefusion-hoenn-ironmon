using Ironmon.Setup.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Checks compatible signed sizing metadata, exact commit binding and honest selected-download totals.
/// </summary>
public sealed class ReleaseDownloadSizesTests
{
    private const string Cache = "size-cache";

    /// <summary>
    /// Preserves the existing manifest schema and reads only estimates bound to its authenticated metadata container.
    /// </summary>
    /// <param name="estimate">The synthetic game estimate, or null to represent an older release.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(1262930260L)]
    public async Task ProducerAndReaderRoundTripWithoutChangingManifestSchema(long? estimate)
    {
        using var fixture = new ReleaseBundleFixture();
        using var workspace = new TestWorkspace();
        fixture.Build(ReleaseBundleFixture.VersionA, gameDownloadBytes: estimate);
        var evidence = fixture.Sign(ReleaseBundleFixture.VersionA);
        var manifest = fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures);
        Assert.Equal(2, manifest.SchemaVersion);
        var store = new ReleaseDownloadStore(workspace.PathFor(Cache), fixture);
        Assert.Equal(estimate, await ReleaseDownloadSizes.ReadGameAsync(manifest, store, ReleaseBundleFixture.CommitA));
        Assert.Null(await ReleaseDownloadSizes.ReadGameAsync(manifest, store, ReleaseBundleFixture.CommitB));
        Assert.Single(fixture.Downloads);
        var container = manifest.Assets.First(asset => asset.Container is not null).Container!;
        var bytes = File.ReadAllBytes(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), ReleaseMetadata.Name));
        bytes[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => ReleaseMetadata.Parse(bytes, container));
    }

    /// <summary>
    /// Rejects invalid sizes at publication and rejects a sidecar bound to a different game snapshot.
    /// </summary>
    /// <param name="bytes">An invalid informational size.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void InvalidMeasurementsCannotBePublished(long bytes)
    {
        using var fixture = new ReleaseBundleFixture();
        Assert.Throws<InvalidDataException>(() => fixture.Build(ReleaseBundleFixture.VersionA, gameDownloadBytes: bytes));
        var wrongCommit = new ReleaseDownloadSizeDocument(ReleaseDownloadSizes.DocumentType, 1, ReleaseBundleFixture.CommitB, 1024);
        var metadata = new ReleaseMetadataDocument(ReleaseMetadata.DocumentType, 1, [new ReleaseMetadataFile(ReleaseDownloadSizes.FileName, ReleaseJson.Serialize(wrongCommit))]);
        Assert.Throws<InvalidDataException>(() => ReleaseDownloadSizes.Read(metadata, ReleaseBundleFixture.CommitA, ReleaseBundleFixture.CommitA));
    }

    /// <summary>
    /// Includes only selected known downloads and marks any missing game, runtime or sprite bytes as additional.
    /// </summary>
    /// <param name="gameBytes">The signed estimate or unavailable size.</param>
    /// <param name="webViewSelected">Whether prerequisite bytes should be counted.</param>
    /// <param name="webViewBytes">The observed runtime size.</param>
    /// <param name="spritesSelected">Whether variable sprite downloads are included.</param>
    /// <param name="incomplete">Whether the result must be labelled as a subtotal.</param>
    [Theory]
    [InlineData(2000L, true, 300L, false, false)]
    [InlineData(2000L, false, 300L, false, false)]
    [InlineData(2000L, true, null, false, true)]
    [InlineData(null, false, null, false, true)]
    [InlineData(2000L, false, null, true, true)]
    public void SelectedTotalsKeepUnknownBytesExplicit(long? gameBytes, bool webViewSelected, long? webViewBytes, bool spritesSelected, bool incomplete)
    {
        var review = new SetupReview(null!, null!, null!, 100, true, false) { GameDownloadBytes = gameBytes };
        var summary = SetupDownloadSummary.Create(review, webViewSelected, webViewBytes, spritesSelected);
        Assert.Equal(100 + MinGitPackage.Pinned.Size + (gameBytes ?? 0) + (webViewSelected ? webViewBytes ?? 0 : 0), summary.KnownBytes);
        Assert.Equal(gameBytes > 0, summary.Estimated);
        Assert.Equal(incomplete, summary.Incomplete);
        var unchangedGame = SetupDownloadSummary.Create(review with { IncludesGame = false }, false, null, false);
        Assert.Equal(100, unchangedGame.KnownBytes);
        Assert.False(unchangedGame.Estimated);
        Assert.False(unchangedGame.Incomplete);
    }
}
