using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using static Ironmon.Updater.Tests.FilePlannerFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises player-data independence, portable completed installations and damaged optional update history.
/// </summary>
public sealed class UpdaterStabilityTests
{
    private const string UserFolder = "Player files";
    private const string RenamedFolder = "Moved player files";
    private const string MovedSave = "Moved player files/renamed save.rxdata";
    private const string Profile = "Data/Ironmon/generation_profiles/imported profile.json";
    private const string Sprite = "Graphics/CustomBattlers/local_sprites/custom.png";
    private const string DownloadedSettings = "Data/Scripts/DownloadedSettings.rb";
    private const string RenamedInstallation = "renamed installation ü";
    private const string InvalidJson = "{";
    private const string DirectoryFile = "Data/transition/old.bin";
    private const string Replacement = "Data/transition";
    private const string NewChild = "Data/transition/player.bin";
    private const string NewParentUserFile = "Data/new/player profile.json";

    /// <summary>
    /// Commits or rolls back only program files while retaining saves, profiles and sprites changed after preparation.
    /// </summary>
    /// <param name="rollback">Whether final verification deliberately triggers rollback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayerFilesCanMoveAndChangeAfterPreparation(bool rollback)
    {
        using var fixture = new TransactionFixture();
        var engine = await fixture.PrepareAsync();
        var folder = PlainPaths.Child(fixture.Root, UserFolder);
        var moved = PlainPaths.Child(fixture.Root, RenamedFolder);
        Directory.CreateDirectory(folder);
        System.IO.File.Move(PlainPaths.Child(fixture.Root, TransactionFixture.SaveFile), Path.Combine(folder, Path.GetFileName(MovedSave)));
        Directory.Move(folder, moved);
        TransactionFixture.Write(fixture.Root, MovedSave, 9);
        TransactionFixture.Write(fixture.Root, Profile, 8);
        TransactionFixture.Write(fixture.Root, Sprite, 7);
        TransactionFixture.Write(fixture.Root, DownloadedSettings, 6);
        TransactionFixture.Write(fixture.Root, NewParentUserFile, 5);
        fixture.RejectFinal = rollback;
        using var locked = new FileStream(PlainPaths.Child(fixture.Root, MovedSave), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId);
        Assert.Equal(rollback ? TransactionPhase.RolledBack : TransactionPhase.Committed, result.Phase);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Root));
        Assert.Equal(new byte[] { 8 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(fixture.Root, Profile)));
        Assert.Equal(new byte[] { 7 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(fixture.Root, Sprite)));
        Assert.Equal(new byte[] { 6 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(fixture.Root, DownloadedSettings)));
        Assert.Equal(new byte[] { 5 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(fixture.Root, NewParentUserFile)));
        Assert.Equal(9, locked.ReadByte());
        Assert.False(System.IO.File.Exists(PlainPaths.Child(fixture.Root, TransactionFixture.SaveFile)));
    }

    /// <summary>
    /// Cancels untouched preparations without restoring stale program or save bytes over changes made afterward.
    /// </summary>
    [Fact]
    public async Task RecoverUntouchedPreparationPreservesChangesAndReleasesInstallation()
    {
        using var fixture = new TransactionFixture();
        var engine = await fixture.PrepareAsync();
        TransactionFixture.Write(fixture.Root, TransactionFixture.SaveFile, 9);
        TransactionFixture.Write(fixture.Root, TransactionFixture.ChangedFile, 8);
        await Assert.ThrowsAsync<IOException>(() => engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId));
        Assert.Equal(TransactionPhase.RolledBack, (await engine.RecoverAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Root));
        Assert.Equal(new byte[] { 9 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(fixture.Root, TransactionFixture.SaveFile)));
        Assert.Equal(new byte[] { 8 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(fixture.Root, TransactionFixture.ChangedFile)));
    }

    /// <summary>
    /// Allows a completed signed installation to be renamed or copied and then updated again with fresh directory binding.
    /// </summary>
    /// <param name="copy">Whether relocation creates a new filesystem directory identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedInstallationCanRelocateAndUpdateAgain(bool copy)
    {
        using var fixture = new SignedReleaseFixture();
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(fixture.Root, prepared.TransactionId)).Phase);
        var parent = Path.GetDirectoryName(fixture.Root)!;
        var relocated = PlainPaths.Child(parent, RenamedInstallation);
        if (copy)
        {
            await TransactionStorage.CopyTreeAsync(fixture.Root, relocated, await TransactionStorage.TreeAsync(fixture.Root, CancellationToken.None), CancellationToken.None);
        }
        else
        {
            Directory.Move(PlainPaths.Full(fixture.Root), relocated);
        }

        var receipt = ReleaseJson.Parse<IronmonInstalledReceipt>(TransactionStorage.ReadBytes(PlainPaths.Child(relocated, IronmonOnlyUpdate.ReceiptPath)), ReleaseJson.ManifestLimit);
        var current = InstalledReleaseEvidence.Read(relocated, receipt, fixture.Verifier);
        Assert.Equal(relocated, current.Request.InstallationRoot);
        fixture.AdvanceVersion();
        var request = fixture.Request() with { InstallationRoot = relocated, CurrentVersion = SignedReleaseFixture.VersionB };
        var next = await fixture.Coordinator().PrepareAsync(request, fixture.Evidence, current.Target);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(relocated, next.TransactionId)).Phase);
        Assert.Equal(SignedReleaseFixture.VersionC, await System.IO.File.ReadAllTextAsync(PlainPaths.Child(relocated, SignedReleaseFixture.Script)));
    }

    /// <summary>
    /// Refuses to rebind a relocated pending update and retains its recovery evidence.
    /// </summary>
    [Fact]
    public async Task PendingTransactionCannotBeRetargetedByMovingItsFolder()
    {
        using var fixture = new TransactionFixture();
        var engine = await fixture.PrepareAsync();
        var relocated = PlainPaths.Child(Path.GetDirectoryName(fixture.Root)!, RenamedInstallation);
        Directory.Move(PlainPaths.Full(fixture.Root), relocated);
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.RecoverAsync(relocated, fixture.Description.TransactionId));
        await Assert.ThrowsAsync<IOException>(() => UpdateTransaction.DescribeAsync(relocated, fixture.Description.Plan.Rebuild()));
        Assert.Equal(fixture.Description.TransactionId, UpdateTransaction.ReadActiveId(relocated));
    }

    /// <summary>
    /// Ignores malformed unrelated history beyond the old search cap while still rejecting lost current evidence.
    /// </summary>
    [Fact]
    public async Task CorruptHistoricalRecordsCannotHideValidCurrentEvidence()
    {
        using var fixture = new SignedReleaseFixture();
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(fixture.Root, prepared.TransactionId)).Phase);
        for (var index = 1; index <= 1030; index++)
            await System.IO.File.WriteAllTextAsync(IronmonOnlyUpdate.AuthorizationPath(fixture.Root, new Guid(index, 0, 0, new byte[8])), InvalidJson);

        var receipt = ReleaseJson.Parse<IronmonInstalledReceipt>(TransactionStorage.ReadBytes(PlainPaths.Child(fixture.Root, IronmonOnlyUpdate.ReceiptPath)), ReleaseJson.ManifestLimit);
        Assert.Equal(fixture.Evidence.Manifest, InstalledReleaseEvidence.Read(fixture.Root, receipt, fixture.Verifier).Target.Manifest);
        await System.IO.File.WriteAllTextAsync(IronmonOnlyUpdate.AuthorizationPath(fixture.Root, prepared.TransactionId), InvalidJson);
        Assert.Throws<InvalidDataException>(() => InstalledReleaseEvidence.Read(fixture.Root, receipt, fixture.Verifier));
    }

    /// <summary>
    /// Inspects new unknown children when an update replaces a directory with a file and refuses destructive reuse.
    /// </summary>
    [Fact]
    public async Task ScopedInspectionStillDetectsNewChildrenInsideDirectoryReplacement()
    {
        using var workspace = new TestWorkspace();
        TransactionFixture.Write(workspace.Root, DirectoryFile, 1);
        var before = await InstallationFileSnapshot.ReadManagedAsync(workspace.Root, [DirectoryFile, Replacement]);
        var plan = Planner().Create([File(DirectoryFile, 1)], before, [File(Replacement, 2)]);
        Assert.True(plan.CanApply);
        TransactionFixture.Write(workspace.Root, NewChild, 9);
        var current = await InstallationFileSnapshot.ReadManagedAsync(workspace.Root, plan.InspectionPaths);
        Assert.False(plan.MatchesBefore(current));
        Assert.Equal(FilePlanApplicability.Changed, plan.Assess(current));
        Assert.Equal(new byte[] { 9 }, await System.IO.File.ReadAllBytesAsync(PlainPaths.Child(workspace.Root, NewChild)));
    }
}
