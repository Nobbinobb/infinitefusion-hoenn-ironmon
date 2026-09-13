using Ironmon.ReleaseTool;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Separates complete upstream evidence from permission to replace protected player files.
/// </summary>
public sealed class GameInventoryProtectionTests
{
    /// <summary>
    /// Retains protected upstream entries in signed evidence while preserving local edits and missing optional files.
    /// </summary>
    /// <param name="present">Whether the player has an independently edited protected file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedUpstreamFilesDoNotBecomeManagedOrRequired(bool present)
    {
        using var fixture = new ReleaseBundleFixture();
        var request = fixture.Build(ReleaseBundleFixture.VersionA);
        var evidence = fixture.Sign(ReleaseBundleFixture.VersionA);
        var manifest = fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures);
        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.GameInventoryRole);
        var inventoryBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), asset.Name));
        var inventory = fixture.Verifier.VerifyInventory(manifest, new InventoryEvidence(asset.Name, inventoryBytes));
        Assert.Contains(inventory.Files, file => file.Path == ReleaseBundleFixture.ProtectedGamePath);
        Assert.DoesNotContain(ReleaseVerifier.ManagedFiles(inventory), file => file.Path == ReleaseBundleFixture.ProtectedGamePath);
        Assert.DoesNotContain(SignedIronmonAuthority.GameFiles(inventory), file => file.Path == ReleaseBundleFixture.ProtectedGamePath);
        ReleaseBundle.ValidateInputs(request.GameInventory, request.GameManifest, request.GameVersion, null, fixture.Trust);
        ReleaseBundleFixture.Write(fixture.Root, ReleaseBundleFixture.GamePath, [1]);
        if (present)
            ReleaseBundleFixture.Write(fixture.Root, ReleaseBundleFixture.ProtectedGamePath, [99]);

        await SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None);
        var protectedPath = Path.Combine(fixture.Root, ReleaseBundleFixture.ProtectedGamePath);
        if (present)
        {
            Assert.Equal("c"u8.ToArray(), await File.ReadAllBytesAsync(protectedPath));
        }
        else
        {
            Assert.False(File.Exists(protectedPath));
        }

        ReleaseBundleFixture.Write(fixture.Root, ReleaseBundleFixture.GamePath, [2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None));
    }
}
