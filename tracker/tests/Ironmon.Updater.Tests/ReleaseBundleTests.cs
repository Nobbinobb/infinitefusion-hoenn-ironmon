using System.Security.Cryptography;
using System.Text;
using Ironmon.ReleaseTool;
using Ironmon.Updater.Infrastructure;
using static Ironmon.Updater.Tests.ReleaseBundleFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises generated release artifacts through signing, installation, update and immutable history validation.
/// </summary>
public sealed class ReleaseBundleTests
{
    private const string SavePath = "File A.rxdata";
    private const string UnknownPath = "Graphics/player-fixture.png";
    private const string CandidateName = "candidate.json";

    /// <summary>
    /// Rejects internally consistent ownership hashes when required components or trust disagree with the release.
    /// </summary>
    /// <param name="path">The packaged component to corrupt.</param>
    /// <param name="remove">Whether to omit the component instead of changing its bytes.</param>
    [Theory]
    [InlineData(BundleFiles.HelperPath, false)]
    [InlineData(BundleFiles.TrustPath, false)]
    [InlineData(BundleFiles.NoticesPath, true)]
    [InlineData(Ironmon.Updater.Core.FileManagementPolicy.BootstrapPath, true)]
    [InlineData(RuntimePath, true)]
    public void PackageComponentsMustAgreeBeforeMetadataCreation(string path, bool remove)
    {
        using var fixture = new ReleaseBundleFixture();
        Assert.Throws<InvalidDataException>(() => fixture.Build(VersionA, customize: (root, _) =>
        {
            if (remove)
            {
                File.Delete(Path.Combine(root, path));
            }
            else
            {
                Write(root, path, [99]);
            }
        }));
    }

    /// <summary>
    /// Installs A and updates through B and C using producer output for both runtime flavors, preserving player content.
    /// </summary>
    /// <param name="flavor">The chosen deployment flavor.</param>
    [Theory]
    [InlineData(ReleaseProtocol.SelfContained)]
    [InlineData(ReleaseProtocol.RuntimeRequired)]
    public async Task GeneratedBundlesInstallAndUpdate(string flavor)
    {
        using var fixture = new ReleaseBundleFixture();
        Write(fixture.Root, GamePath, [1]);
        Write(fixture.Root, SavePath, [8]);
        Write(fixture.Root, UnknownPath, [9]);
        string? history = null;
        ReleaseEvidence? previous = null;
        var current = SetupProtocol.UninstalledVersion;
        var helperVersions = new HashSet<string>();
        foreach (var version in new[] { VersionA, VersionB, VersionC })
        {
            fixture.Build(version, history);
            var evidence = fixture.Sign(version);
            var request = new IronmonUpdateRequest(fixture.Root, current, flavor, flavor, CommitA, false, []);
            var purpose = current == SetupProtocol.UninstalledVersion ? InstallationPurpose.AddIronmon : InstallationPurpose.Update;
            var prepared = await fixture.Coordinator().PrepareInstallationAsync(request, evidence, previous, purpose);
            Assert.True(helperVersions.Add(prepared.HelperVersion));
            var transaction = new UpdateTransaction(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
            var result = await transaction.ApplyAsync(fixture.Root, prepared.TransactionId);
            Assert.True(result.Phase == TransactionPhase.Committed, result.Error);
            Assert.Equal(version, File.ReadAllText(Path.Combine(fixture.Root, ScriptPath)));
            Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(Path.Combine(fixture.Root, SavePath)));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(fixture.Root, UnknownPath)));
            Assert.True(File.Exists(Path.Combine(fixture.Root, OldProfile)));
            Assert.Single(fixture.Downloads, url => url == ReleaseProtocol.AssetUrl(version, ReleaseMetadata.Name));
            Assert.Single(fixture.Downloads, url => url == ReleaseProtocol.AssetUrl(version, ReleaseBundle.PackageName(version, flavor)));
            Assert.DoesNotContain(fixture.Downloads, url => url == ReleaseProtocol.AssetUrl(version, ReleaseBundle.PackageName(version, flavor == ReleaseProtocol.SelfContained ? ReleaseProtocol.RuntimeRequired : ReleaseProtocol.SelfContained)));
            current = version;
            previous = evidence;
            history = fixture.DirectoryFor(version);
        }

        Assert.True(File.Exists(Path.Combine(fixture.Root, NewProfile)));
    }

    /// <summary>
    /// Carries byte-identical history across upstream changes while selecting the new workflow game commit.
    /// </summary>
    [Fact]
    public void HistoryAndGameSelectionFollowReleaseInputs()
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(VersionA);
        fixture.Sign(VersionA);
        fixture.Build(VersionB, fixture.DirectoryFor(VersionA), CommitB);
        fixture.Sign(VersionB);
        var first = ReleaseBundle.Verify(fixture.DirectoryFor(VersionA), fixture.Trust, true);
        var second = ReleaseBundle.Verify(fixture.DirectoryFor(VersionB), fixture.Trust, true);
        Assert.Equal(CommitB, second.Game.PreferredCommit);
        Assert.Equal(new[] { CommitB }, second.Game.SupportedCommits);
        Assert.Equal(2, second.ReleaseSequence);
        Assert.Equal(2, second.AdoptionBaselines.Length);
        foreach (var asset in first.Assets.Where(asset => asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.GameInventoryRole))
            Assert.Equal(File.ReadAllBytes(Path.Combine(fixture.DirectoryFor(VersionA), asset.Name)), File.ReadAllBytes(Path.Combine(fixture.DirectoryFor(VersionB), asset.Name)));
    }

    /// <summary>
    /// Rejects missing artifacts and changed frozen bytes before a detached signature is created.
    /// </summary>
    /// <param name="role">The asset type being corrupted.</param>
    /// <param name="remove">Whether the artifact is absent instead of changed.</param>
    [Theory]
    [InlineData(ReleaseProtocol.SetupRole, true)]
    [InlineData(ReleaseProtocol.TrackerRolePrefix + ReleaseProtocol.SelfContained, false)]
    [InlineData(ReleaseProtocol.InventoryRole, false)]
    [InlineData(ReleaseProtocol.NotesRole, false)]
    [InlineData(ReleaseProtocol.TrackerRolePrefix + ReleaseProtocol.RuntimeRequired, true)]
    public void IncompleteOrCorruptBundlesCannotBeSigned(string role, bool remove)
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(VersionA);
        var manifest = ReleaseBundle.Verify(fixture.DirectoryFor(VersionA), fixture.Trust, false);
        var asset = manifest.Assets.First(asset => asset.Role == role);
        var path = Path.Combine(fixture.DirectoryFor(VersionA), asset.Container?.Name ?? asset.Name);
        if (remove)
        {
            File.Delete(path);
        }
        else
        {
            File.AppendAllText(path, "Corrupt fixture content.");
        }

        if (remove)
        {
            Assert.Throws<FileNotFoundException>(() => fixture.Sign(VersionA));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => fixture.Sign(VersionA));
        }

        Assert.False(File.Exists(Path.Combine(fixture.DirectoryFor(VersionA), ReleaseProtocol.SignatureName)));
    }

    /// <summary>
    /// Checks key identity, exact signature bytes and immutable signing retries.
    /// </summary>
    [Fact]
    public void SigningRequiresDedicatedTrustAndPreservesRetries()
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(VersionA);
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrongPrivate = wrongKey.ExportPkcs8PrivateKey();
        try
        {
            Assert.Throws<InvalidDataException>(() => ReleaseSigning.Sign(fixture.DirectoryFor(VersionA), fixture.Trust, KeyId, wrongPrivate));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrongPrivate);
        }

        var first = fixture.Sign(VersionA);
        var retry = fixture.Sign(VersionA);
        Assert.Equal(first.Signatures, retry.Signatures);
        var changed = first.Manifest.Concat(Encoding.UTF8.GetBytes(Environment.NewLine)).ToArray();
        Assert.Throws<InvalidDataException>(() => fixture.Verifier.Verify(changed, first.Signatures));
    }

    /// <summary>
    /// Verifies the first updater release can import independently restored pre-updater candidate packages.
    /// </summary>
    [Fact]
    public void LegacyBridgeChecksExactCandidateBytes()
    {
        using var fixture = new ReleaseBundleFixture();
        var first = fixture.Build(VersionA);
        var directory = first.Directory;
        var assets = new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired }.Select(flavor => ReleaseBundle.Asset(directory, VersionA, ReleaseBundle.PackageName(VersionA, flavor), ReleaseProtocol.TrackerRolePrefix + flavor)).ToArray();
        File.Copy(first.GameInventory, Path.Combine(directory, Path.GetFileName(first.GameInventory)));
        File.Copy(first.GameManifest, Path.Combine(directory, Path.GetFileName(first.GameManifest)));
        File.WriteAllBytes(Path.Combine(directory, LegacyHistory.ImportName), ReleaseJson.Serialize(new LegacyImport(VersionA, first.GameVersion, Path.GetFileName(first.GameInventory), Path.GetFileName(first.GameManifest))));
        File.WriteAllBytes(Path.Combine(directory, CandidateName), ReleaseJson.Serialize(new { schema_version = 1, repository = ReleaseProtocol.Repository, version = VersionA, inputs = new { game_commit = CommitA }, assets }));
        var imported = LegacyHistory.Read(directory);
        Assert.Equal(2, imported.Packages.Length);
        Assert.Equal(CommitA, imported.Game.GameCommit);
        File.Delete(Path.Combine(directory, ReleaseProtocol.ManifestName));
        fixture.Build(VersionB, directory);
        fixture.Sign(VersionB);
        var firstSignedRelease = ReleaseBundle.Verify(fixture.DirectoryFor(VersionB), fixture.Trust, true);
        Assert.Equal(1, firstSignedRelease.ReleaseSequence);
        Assert.Equal(2, firstSignedRelease.AdoptionBaselines.Single().LegacyPackages.Length);
        File.AppendAllText(Path.Combine(directory, assets[0].Name), "Changed legacy archive.");
        Assert.Throws<InvalidDataException>(() => LegacyHistory.Read(directory));
    }

    /// <summary>
    /// Prevents metadata regeneration over an existing immutable bundle.
    /// </summary>
    [Fact]
    public void ExistingMetadataCannotBeRegenerated()
    {
        using var fixture = new ReleaseBundleFixture();
        var request = fixture.Build(VersionA);
        Assert.Throws<IOException>(() => ReleaseBundle.Create(request));
        Assert.Throws<InvalidDataException>(() => ReleaseBundle.Create(request with { GameCommit = CommitB }));
    }
}
