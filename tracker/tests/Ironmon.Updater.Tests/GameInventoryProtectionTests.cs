using Ironmon.ReleaseTool;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Separates required installed-game hashes from complete upstream evidence and fresh-install defaults.
/// </summary>
public sealed class GameInventoryProtectionTests
{
    private const string DownloadedSettingsPath = "Data/Scripts/DownloadedSettings.rb";
    private const string DownloadedPokedexPath = "Data/pokedex/dex.json";
    private const string SimilarSettingsPath = "Data/Scripts/DownloadedSettings-other.rb";
    private const string SimilarPokedexPath = "Data/pokedex/dex-other.json";
    private const string OtherScriptPath = "Data/Scripts/Main.rb";
    private const string PokedexBackupPath = "Data/pokedex/dex.json.backup";

    /// <summary>
    /// Retains complete upstream evidence while allowing protected state and runtime downloads to differ or be missing.
    /// </summary>
    /// <param name="path">The exact upstream file managed by runtime downloads or protected sprite state.</param>
    /// <param name="present">Whether the player has a locally changed runtime download or protected file.</param>
    /// <param name="managed">Whether fresh game installations must still receive the signed default.</param>
    /// <returns>A task representing signed baseline and snapshot verification without modifying runtime-owned content.</returns>
    [Theory]
    [InlineData(ReleaseBundleFixture.ProtectedGamePath, false, false)]
    [InlineData(ReleaseBundleFixture.ProtectedGamePath, true, false)]
    [InlineData(DownloadedSettingsPath, false, true)]
    [InlineData(DownloadedSettingsPath, true, true)]
    [InlineData(DownloadedPokedexPath, false, true)]
    [InlineData(DownloadedPokedexPath, true, true)]
    public async Task RuntimeAndProtectedGameFilesDoNotRequireOriginalBytes(string path, bool present, bool managed)
    {
        using var fixture = new ReleaseBundleFixture(path);
        var request = fixture.Build(ReleaseBundleFixture.VersionA);
        var evidence = fixture.Sign(ReleaseBundleFixture.VersionA);
        var manifest = fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures);
        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.GameInventoryRole);
        var inventoryBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), asset.Name));
        var inventory = fixture.Verifier.VerifyInventory(manifest, new InventoryEvidence(asset.Name, inventoryBytes));
        Assert.Contains(inventory.Files, file => file.Path == path);
        Assert.Equal(managed, ReleaseVerifier.ManagedFiles(inventory).Any(file => file.Path == path));
        Assert.Equal(managed, SignedIronmonAuthority.GameFiles(inventory).Any(file => file.Path == path));
        Assert.False(ReleaseVerifier.IsRequiredGamePath(path));
        ReleaseBundle.ValidateInputs(request.GameInventory, request.GameManifest, request.GameVersion, null, fixture.Trust);
        ReleaseBundleFixture.Write(fixture.Root, ReleaseBundleFixture.GamePath, [1]);
        if (present)
            ReleaseBundleFixture.Write(fixture.Root, path, [99]);

        await SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None);
        var local = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        SignedIronmonAuthority.VerifyGameSnapshot(inventory, local);
        var gameFiles = SignedIronmonAuthority.GameFiles(inventory);
        var plan = new FileUpdatePlanner(new FileManagementPolicy()).Create(gameFiles, local, gameFiles);
        Assert.True(plan.CanApply);
        if (present || managed)
        {
            var retained = Assert.Single(plan.Entries, entry => entry.Path == path);
            Assert.Equal(FilePlanAction.Keep, retained.Action);
            Assert.Equal(managed, retained.Baseline is not null);
            Assert.Equal(managed, retained.Target is not null);
        }
        else
        {
            Assert.DoesNotContain(plan.Entries, entry => entry.Path == path);
        }

        var localPath = Path.Combine(fixture.Root, path);
        if (present)
        {
            var retainedBytes = await File.ReadAllBytesAsync(localPath);
            Assert.Equal<byte>([99], retainedBytes);
        }
        else
        {
            Assert.False(File.Exists(localPath));
        }

        ReleaseBundleFixture.Write(fixture.Root, ReleaseBundleFixture.GamePath, [2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None));
        var changed = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        Assert.Throws<InvalidDataException>(() => SignedIronmonAuthority.VerifyGameSnapshot(inventory, changed));
    }

    /// <summary>
    /// Keeps neighboring scripts and data files subject to signed content verification.
    /// </summary>
    /// <param name="path">The ordinary game file that must remain required and checked.</param>
    /// <returns>A task representing rejection by both direct and captured-snapshot verification.</returns>
    [Theory]
    [InlineData(SimilarSettingsPath)]
    [InlineData(SimilarPokedexPath)]
    [InlineData(OtherScriptPath)]
    [InlineData(PokedexBackupPath)]
    public async Task OtherGameFilesStillRequireSignedContent(string path)
    {
        using var fixture = new ReleaseBundleFixture(path);
        fixture.Build(ReleaseBundleFixture.VersionA);
        var evidence = fixture.Sign(ReleaseBundleFixture.VersionA);
        var manifest = fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures);
        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.GameInventoryRole);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), asset.Name));
        var inventory = fixture.Verifier.VerifyInventory(manifest, new InventoryEvidence(asset.Name, bytes));
        ReleaseBundleFixture.Write(fixture.Root, ReleaseBundleFixture.GamePath, [1]);
        ReleaseBundleFixture.Write(fixture.Root, path, [99]);
        Assert.Contains(SignedIronmonAuthority.GameFiles(inventory), file => file.Path == path);
        Assert.True(ReleaseVerifier.IsRequiredGamePath(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None));
        var local = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        Assert.Throws<InvalidDataException>(() => SignedIronmonAuthority.VerifyGameSnapshot(inventory, local));
    }

    /// <summary>
    /// Keeps signed download defaults in fresh game plans so first startup does not depend on a successful refresh.
    /// </summary>
    /// <param name="path">The runtime-owned settings or Pokedex download.</param>
    /// <returns>A task representing verification of the signed default and its initial write plan.</returns>
    [Theory]
    [InlineData(DownloadedSettingsPath)]
    [InlineData(DownloadedPokedexPath)]
    public async Task FreshGamePlansRetainSignedRuntimeDownloadDefaults(string path)
    {
        using var fixture = new ReleaseBundleFixture(path);
        fixture.Build(ReleaseBundleFixture.VersionA);
        var evidence = fixture.Sign(ReleaseBundleFixture.VersionA);
        var manifest = fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures);
        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.GameInventoryRole);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), asset.Name));
        var inventory = fixture.Verifier.VerifyInventory(manifest, new InventoryEvidence(asset.Name, bytes));
        var planner = new FileUpdatePlanner(new FileManagementPolicy());
        var plan = planner.Create([], [], SignedIronmonAuthority.GameFiles(inventory));
        var initial = Assert.Single(plan.Entries, entry => entry.Path == path);
        Assert.True(plan.CanApply);
        Assert.Equal(FilePlanAction.Write, initial.Action);
        Assert.Null(initial.Baseline);
        Assert.Null(initial.Local);
        Assert.Equal(ManagedFileOwner.Game, initial.Target!.Owner);
        Assert.Equal(FilePlannerFixture.Content(1), initial.Target.Content);
    }
}
