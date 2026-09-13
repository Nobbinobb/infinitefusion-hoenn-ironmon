using Ironmon.ReleaseTool;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises compact metadata integrity and the embedded helper through actual producer output.
/// </summary>
public sealed class CompactReleaseTests
{
    private const string DuplicateName = "duplicate.json";
    private const string UnsafeName = "../outside.json";
    private const string CacheDirectory = "downloads";

    /// <summary>
    /// Verifies published packages and restores exact history without private build evidence or loose inventories.
    /// </summary>
    [Fact]
    public void PublicFilesSupportVerificationAndSubsequentHistory()
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(ReleaseBundleFixture.VersionA);
        fixture.Sign(ReleaseBundleFixture.VersionA);
        var directory = fixture.DirectoryFor(ReleaseBundleFixture.VersionA);
        var publicNames = new HashSet<string>(StringComparer.Ordinal)
        {
            ReleaseBundle.PackageName(ReleaseBundleFixture.VersionA, ReleaseProtocol.SelfContained),
            ReleaseBundle.PackageName(ReleaseBundleFixture.VersionA, ReleaseProtocol.RuntimeRequired),
            ReleaseBundle.SetupName(ReleaseBundleFixture.VersionA),
            ReleaseProtocol.ManifestName,
            ReleaseProtocol.SignatureName,
            ReleaseMetadata.Name
        };

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (!publicNames.Contains(Path.GetFileName(path)))
                File.Delete(path);
        }

        var first = ReleaseBundle.Verify(directory, fixture.Trust, true);
        fixture.Build(ReleaseBundleFixture.VersionB, directory);
        fixture.Sign(ReleaseBundleFixture.VersionB);
        var second = ReleaseBundle.Verify(fixture.DirectoryFor(ReleaseBundleFixture.VersionB), fixture.Trust, true);
        Assert.Equal(first.ReleaseSequence + 1, second.ReleaseSequence);
        foreach (var asset in first.Assets.Where(asset => asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.GameInventoryRole))
        {
            var preserved = second.Assets.Single(next => next.Name == asset.Name);
            Assert.Equal(ReleaseBundle.ReadAsset(directory, asset), ReleaseBundle.ReadAsset(fixture.DirectoryFor(ReleaseBundleFixture.VersionB), preserved));
        }
    }

    /// <summary>
    /// Rejects corruption at either the shared container or individually signed document boundary.
    /// </summary>
    /// <param name="rebindContainer">Whether a deliberately inconsistent publisher signed the altered outer container.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataAuthenticatesContainerAndDocument(bool rebindContainer)
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(ReleaseBundleFixture.VersionA);
        var manifest = ReleaseBundle.Verify(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), fixture.Trust, false);
        var asset = manifest.Assets.First(asset => asset.Container is not null);
        var metadata = ReleaseJson.Parse<ReleaseMetadataDocument>(File.ReadAllBytes(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), ReleaseMetadata.Name)), ReleaseMetadata.Limit);
        var changed = metadata with { Files = [.. metadata.Files.Select(file => file.Name == asset.Name ? file with { Bytes = [99] } : file)] };
        var bytes = ReleaseJson.Serialize(changed);
        if (rebindContainer)
            asset = asset with { Container = new ReleaseContainer(ReleaseMetadata.Name, bytes.Length, TransactionStorage.Hash(bytes)) };

        Assert.Throws<InvalidDataException>(() => ReleaseMetadata.Read(bytes, asset));
    }

    /// <summary>
    /// Rejects unsafe and ambiguous document names even in an otherwise hash-matching container.
    /// </summary>
    /// <param name="duplicate">Whether the container repeats a name instead of attempting traversal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataRejectsUnsafeOrDuplicateNames(bool duplicate)
    {
        ReleaseMetadataFile[] files = duplicate ? [new(DuplicateName, [1]), new(DuplicateName, [2])] : [new(UnsafeName, [1])];
        var bytes = ReleaseJson.Serialize(new ReleaseMetadataDocument(ReleaseMetadata.DocumentType, 1, files));
        var identity = new ReleaseContainer(ReleaseMetadata.Name, bytes.Length, TransactionStorage.Hash(bytes));
        Assert.Throws<InvalidDataException>(() => ReleaseMetadata.Parse(bytes, identity));
    }

    /// <summary>
    /// Rejects declared metadata sizes before allocating or parsing an oversized document.
    /// </summary>
    [Fact]
    public void MetadataRejectsOversizedContainer()
    {
        var identity = new ReleaseContainer(ReleaseMetadata.Name, ReleaseMetadata.Limit + 1L, new string('0', 64));
        Assert.Throws<InvalidDataException>(() => ReleaseMetadata.Parse([], identity));
    }

    /// <summary>
    /// Rejects a helper that disagrees with its signed executable identity inside a valid player ZIP.
    /// </summary>
    /// <param name="flavor">The package selected by the player.</param>
    [Theory]
    [InlineData(ReleaseProtocol.SelfContained)]
    [InlineData(ReleaseProtocol.RuntimeRequired)]
    public async Task EmbeddedHelperMustMatchSignedIdentity(string flavor)
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(ReleaseBundleFixture.VersionA);
        fixture.Sign(ReleaseBundleFixture.VersionA);
        var manifest = ReleaseBundle.Verify(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), fixture.Trust, true);
        var changed = manifest with { Helper = manifest.Helper! with { Sha256 = new string('0', 64) } };
        var downloads = new ReleaseDownloadStore(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), CacheDirectory), fixture);
        await Assert.ThrowsAsync<InvalidDataException>(() => RecoveryHelperPackage.StageAsync(fixture.Root, changed, downloads, flavor, CancellationToken.None));
        Assert.DoesNotContain(fixture.Downloads, url => url.EndsWith(ReleaseBundle.HelperName(1), StringComparison.Ordinal));
    }
}
