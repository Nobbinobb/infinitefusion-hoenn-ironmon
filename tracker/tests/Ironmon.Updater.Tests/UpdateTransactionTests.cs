using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises real backups, mutations, cancellation, metadata rollback and durable recovery blockers.
/// </summary>
public sealed class UpdateTransactionTests
{
    private const string CorruptText = "corrupt";
    private const string ActivePath = ".ironmon-update/active.json";
    private const string GitIndex = ".git/index";
    private const string GitRef = ".git/refs/heads/releases";
    private const string GitObject = ".git/objects/old";

    /// <summary>
    /// Commits exact version B while retaining version A backups and preserving saves.
    /// </summary>
    /// <param name="git">Whether metadata participates.</param>
    /// <param name="zip">Whether Git did not exist before installation.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompleteUpdateCommitsOnlyAfterAllChecks(bool git, bool zip)
    {
        using var fixture = new TransactionFixture();
        var engine = await fixture.PrepareAsync(git, zip);
        var result = await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.ChangedFile)));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.SaveFile)));
        Assert.False(File.Exists(Path.Combine(fixture.Root, TransactionFixture.OldFile)));
        Assert.True(File.Exists(Path.Combine(fixture.DirectoryPath, TransactionStorage.BackupDirectory, TransactionFixture.OldFile)));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ActivePath)));
        if (git)
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitIndex)));

        await Assert.ThrowsAsync<FileNotFoundException>(() => engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId));
    }

    /// <summary>
    /// Restores original index, references and objects when final component verification rejects the installed result.
    /// </summary>
    /// <param name="zip">Whether rollback must restore the absence of Git.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalVerificationFailureRestoresFilesAndEntireGit(bool zip)
    {
        using var fixture = new TransactionFixture();
        var engine = await fixture.PrepareAsync(git: true, zip);
        fixture.RejectFinal = true;
        Assert.Equal(TransactionPhase.RolledBack, (await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        await fixture.AssertRestoredAsync();
        if (!zip)
        {
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitIndex)));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitRef)));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitObject)));
        }
        else
        {
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, TransactionStorage.GitDirectory)));
        }
    }

    /// <summary>
    /// Cancels at each forward durability boundary and requires complete non-cancellable rollback.
    /// </summary>
    /// <param name="index">The selected operation.</param>
    /// <param name="boundary">The selected durability boundary.</param>
    [Theory]
    [InlineData(0, TransactionBoundary.Intent)]
    [InlineData(0, TransactionBoundary.Mutation)]
    [InlineData(0, TransactionBoundary.Completion)]
    [InlineData(1, TransactionBoundary.Mutation)]
    [InlineData(2, TransactionBoundary.Mutation)]
    [InlineData(3, TransactionBoundary.Mutation)]
    [InlineData(4, TransactionBoundary.GitDetached)]
    [InlineData(4, TransactionBoundary.Mutation)]
    public async Task CancellationNeverAbandonsPartialInstallation(int index, TransactionBoundary boundary)
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync(git: true);
        using var cancellation = new CancellationTokenSource();
        var reached = false;
        var engine = fixture.Engine(progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Operation == index && progress.Boundary == boundary)
            {
                reached = true;
                cancellation.Cancel();
            }
        });
        var result = await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId, cancellationToken: cancellation.Token);
        Assert.True(reached);
        Assert.Equal(TransactionPhase.RolledBack, result.Phase);
        await fixture.AssertRestoredAsync();
    }

    /// <summary>
    /// Leaves preparation intact when insufficient capacity or a running process blocks application before intent.
    /// </summary>
    [Fact]
    public async Task PreflightBlockersDoNotMutatePrograms()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        await Assert.ThrowsAsync<IOException>(() => fixture.Engine(space: _ => 0).ApplyAsync(fixture.Root, fixture.Description.TransactionId));
        await Assert.ThrowsAsync<IOException>(() => fixture.Engine(idle: _ => Task.FromException(new IOException("The game is still running."))).ApplyAsync(fixture.Root, fixture.Description.TransactionId));
        Assert.Equal(TransactionPhase.Prepared, TransactionStorage.ReadJournal(fixture.DirectoryPath).Phase);
        Assert.Equal(TransactionPhase.RolledBack, (await fixture.Engine().RecoverAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        await fixture.AssertRestoredAsync();
    }

    /// <summary>
    /// Rejects damaged payloads and backups before any installed file is changed.
    /// </summary>
    /// <param name="backup">Whether the damaged file is needed for recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptCopiesBlockApplication(bool backup)
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, backup ? TransactionStorage.BackupDirectory : TransactionStorage.PayloadDirectory, TransactionFixture.ChangedFile), CorruptText);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Engine().ApplyAsync(fixture.Root, fixture.Description.TransactionId));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.ChangedFile)));
        Assert.True(File.Exists(Path.Combine(fixture.Root, ActivePath)));
    }

    /// <summary>
    /// Retains unresolved recovery and succeeds after the external file lock is released.
    /// </summary>
    [Fact]
    public async Task FailedRollbackRetainsJournalAndBackupsForRetry()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        FileStream? held = null;
        var engine = fixture.Engine(progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Boundary == TransactionBoundary.Mutation && progress.Operation == 2)
            {
                held = new FileStream(Path.Combine(fixture.Root, TransactionFixture.ChangedFile), FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("Injected failure after replacement.");
            }
        });
        try
        {
            Assert.Equal(TransactionPhase.RecoveryRequired, (await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
            Assert.Throws<IOException>(() => InstallationLease.Acquire(fixture.Root));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(TransactionPhase.RolledBack, (await fixture.Engine().RecoverAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        await fixture.AssertRestoredAsync();
    }

    /// <summary>
    /// Reports relaunch failure separately from a successfully committed installation.
    /// </summary>
    [Fact]
    public async Task RelaunchFailureDoesNotUndoSuccessfulInstall()
    {
        using var fixture = new TransactionFixture();
        var engine = await fixture.PrepareAsync();
        var result = await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId, relaunch: () => Task.FromException(new IOException("Tracker could not start.")));
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.NotNull(result.RelaunchError);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Blocks duplicate updater and sprite writers while the operating-system lease is held.
    /// </summary>
    [Fact]
    public async Task InstallationLeaseExcludesEveryCooperatingWriter()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        using var lease = InstallationLease.Acquire(fixture.Root, recovery: true);
        Assert.Throws<IOException>(() => InstallationLease.Acquire(fixture.Root, recovery: true));
        Assert.Throws<IOException>(() => InstallationLease.Acquire(fixture.Root));
    }
}
