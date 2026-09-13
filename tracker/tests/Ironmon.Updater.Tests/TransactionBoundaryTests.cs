using Ironmon.Updater.Infrastructure;
using System.Security.Cryptography;
using static Ironmon.Updater.Tests.FilePlannerFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Rejects damaged authority and exercises structural rollback, permissions and coordinated writer shutdown.
/// </summary>
public sealed class TransactionBoundaryTests
{
    private const string ShapePath = "Data/shape";
    private const string ShapeChild = "Data/shape/owned.bin";
    private const string RootName = "installation";
    private const string PayloadName = "payload";
    private const string DamagedJson = "{}";
    private const string ChangedNavigation = "archive/run-123";

    /// <summary>
    /// Commits both file/directory transitions while Windows parent handles remain scoped to each operation.
    /// </summary>
    /// <param name="directoryToFile">Whether a managed directory becomes one file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuralCommitAllowsOperationScopedParentLocks(bool directoryToFile)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        var payload = workspace.PathFor(PayloadName);
        var beforePath = directoryToFile ? ShapeChild : ShapePath;
        var afterPath = directoryToFile ? ShapePath : ShapeChild;
        TransactionFixture.Write(root, beforePath, 1);
        TransactionFixture.Write(payload, afterPath, 2);
        var plan = Planner().Create([File(beforePath, 1)], await InstallationFileSnapshot.ReadAsync(root), [File(afterPath, 2)]);
        var description = await UpdateTransaction.DescribeAsync(root, plan);
        var engine = new UpdateTransaction(new ExactAuthority(TransactionStorage.Serialize(description)), _ => Task.CompletedTask);
        await engine.PrepareAsync(description, payload);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(root, description.TransactionId)).Phase);
        Assert.Equal(new byte[] { 2 }, System.IO.File.ReadAllBytes(Path.Combine(root, afterPath)));
        Assert.Equal(directoryToFile, System.IO.File.Exists(Path.Combine(root, ShapePath)));
        Assert.Null(UpdateTransaction.ReadActiveId(root));
    }

    /// <summary>
    /// Rebuilds the original file/directory shape after a failure at every actual mutation in both directions.
    /// </summary>
    /// <param name="directoryToFile">Whether a managed directory becomes one file.</param>
    /// <param name="mutation">The selected mutation boundary.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task StructuralRollbackRestoresOriginalShape(bool directoryToFile, int mutation)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        var payload = workspace.PathFor(PayloadName);
        var beforePath = directoryToFile ? ShapeChild : ShapePath;
        var afterPath = directoryToFile ? ShapePath : ShapeChild;
        TransactionFixture.Write(root, beforePath, 1);
        TransactionFixture.Write(payload, afterPath, 2);
        var snapshot = await InstallationFileSnapshot.ReadAsync(root);
        var plan = Planner().Create([File(beforePath, 1)], snapshot, [File(afterPath, 2)]);
        var description = await UpdateTransaction.DescribeAsync(root, plan);
        var authority = new ExactAuthority(TransactionStorage.Serialize(description));
        var engine = new UpdateTransaction(authority, _ => Task.CompletedTask, progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Operation == mutation && progress.Boundary == TransactionBoundary.Mutation)
                throw new IOException("Injected structural mutation failure.");
        });
        await engine.PrepareAsync(description, payload);
        Assert.Equal(TransactionPhase.RolledBack, (await engine.ApplyAsync(root, description.TransactionId)).Phase);
        Assert.Equal(snapshot, await InstallationFileSnapshot.ReadAsync(root));
    }

    /// <summary>
    /// Refuses altered descriptor bytes even when an attacker also updates the local active-pointer hash.
    /// </summary>
    [Fact]
    public async Task LocalJournalCannotAuthorizeAlteredDescriptor()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        var description = fixture.Description with { Plan = fixture.Description.Plan with { ApprovedPaths = [TransactionFixture.ChangedFile] } };
        var bytes = TransactionStorage.Serialize(description);
        System.IO.File.WriteAllBytes(Path.Combine(fixture.DirectoryPath, TransactionStorage.DescriptionFile), bytes);
        var active = new { SchemaVersion = 1, Id = fixture.Description.TransactionId, DescriptionHash = TransactionStorage.Hash(bytes) };
        TransactionStorage.WriteDurable(Path.Combine(fixture.Root, InstallationLease.StateDirectory, InstallationLease.ActiveFile), TransactionStorage.Serialize(active));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Engine().RecoverAsync(fixture.Root, fixture.Description.TransactionId));
        Assert.Equal(new byte[] { 1 }, System.IO.File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.ChangedFile)));
    }

    /// <summary>
    /// Keeps recovery data and refuses unsupported journal protocols or damaged generations.
    /// </summary>
    /// <param name="unsupported">Whether the current journal is valid JSON with an unsupported schema.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedOrDamagedJournalFailsClosed(bool unsupported)
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        if (unsupported)
        {
            var journal = TransactionStorage.ReadJournal(fixture.DirectoryPath);
            TransactionStorage.WriteJournal(fixture.DirectoryPath, journal with { SchemaVersion = 999 });
        }
        else
        {
            System.IO.File.WriteAllText(Path.Combine(fixture.DirectoryPath, TransactionStorage.JournalFile), DamagedJson);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Engine().RecoverAsync(fixture.Root, fixture.Description.TransactionId));
        Assert.Throws<IOException>(() => InstallationLease.Acquire(fixture.Root));
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.DirectoryPath, TransactionStorage.BackupDirectory, TransactionFixture.ChangedFile)));
    }

    /// <summary>
    /// Preserves unexpected user bytes instead of silently overwriting them while recovering.
    /// </summary>
    [Fact]
    public async Task ExternalEditDuringApplyRetainsRecoveryBlocker()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        var engine = fixture.Engine(progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Operation == 2 && progress.Boundary == TransactionBoundary.Mutation)
            {
                TransactionFixture.Write(fixture.Root, TransactionFixture.ChangedFile, 7);
                throw new IOException("Injected external modification.");
            }
        });
        Assert.Equal(TransactionPhase.RecoveryRequired, (await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        Assert.Equal(new byte[] { 7 }, System.IO.File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.ChangedFile)));
    }

    /// <summary>
    /// Treats a read-only destination failure as failed installation and restores prior mutations.
    /// </summary>
    [Fact]
    public async Task PermissionFailureCannotReportSuccess()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        var path = Path.Combine(fixture.Root, TransactionFixture.ChangedFile);
        System.IO.File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(TransactionPhase.RolledBack, (await fixture.Engine().ApplyAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
            await fixture.AssertRestoredAsync();
        }
        finally
        {
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// Retains the last durable intent when journal replacement fails after a real installed-file mutation.
    /// </summary>
    [Fact]
    public async Task JournalWriteFailurePreservesRecoverableIntent()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        var journalPath = Path.Combine(fixture.DirectoryPath, TransactionStorage.JournalFile);
        var engine = fixture.Engine(progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Operation == 0 && progress.Boundary == TransactionBoundary.Mutation)
                System.IO.File.SetAttributes(journalPath, FileAttributes.ReadOnly);
        });
        try
        {
            var result = await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId);
            Assert.Equal(TransactionPhase.RecoveryRequired, result.Phase);
            Assert.Equal(TransactionPhase.Applying, TransactionStorage.ReadJournal(fixture.DirectoryPath).Phase);
        }
        finally
        {
            System.IO.File.SetAttributes(journalPath, FileAttributes.Normal);
        }

        Assert.Equal(TransactionPhase.RolledBack, (await fixture.Engine().RecoverAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        await fixture.AssertRestoredAsync();
    }

    /// <summary>
    /// Waits for cancellation cleanup and blocks replacement writers until the tracker explicitly resumes.
    /// </summary>
    [Fact]
    public async Task ShutdownDrainsWriterBeforeAllowingHandoff()
    {
        using var workspace = new TestWorkspace();
        var writers = new InstallationWriterCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var work = writers.RunAsync(workspace.Root, async token =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                await Task.Yield();
                cleaned = true;
            }

            return true;
        });
        await started.Task;
        await writers.PauseAsync(workspace.Root);
        Assert.True(cleaned);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writers.RunAsync(workspace.Root, _ => Task.FromResult(true)));
        writers.Resume(workspace.Root);
        Assert.True(await writers.RunAsync(workspace.Root, _ => Task.FromResult(true)));
    }

    /// <summary>
    /// Keeps navigation in updater state independently of the replaced tracker folder and binds it to one transaction.
    /// </summary>
    [Fact]
    public async Task NavigationSurvivesIndependentHandoff()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync();
        var tracker = new UpdateProcessIdentity(1, 1, Path.Combine(fixture.Root, UpdaterHandoff.TrackerRelativePath));
        TrackerRelaunch.Preserve(new UpdaterHandoffRequest(fixture.Root, fixture.Description.TransactionId, tracker, null, ChangedNavigation));
        Assert.Equal(ChangedNavigation, TrackerRelaunch.ReadNavigation(fixture.Root, fixture.Description.TransactionId));
        Assert.Throws<InvalidDataException>(() => TrackerRelaunch.ReadNavigation(fixture.Root, Guid.NewGuid()));
    }

    /// <summary>
    /// Authorizes only descriptor bytes held independently by the structural fixture.
    /// </summary>
    /// <remarks>
    /// Constructs the isolated fixture verifier.
    /// </remarks>
    /// <param name="bytes">The original authorized descriptor bytes.</param>
    private sealed class ExactAuthority(byte[] bytes) : ITransactionAuthority
    {
        /// <summary>
        /// Rejects changes to the fixture's captured descriptor.
        /// </summary>
        /// <param name="description">The proposed descriptor.</param>
        /// <param name="exactBytes">The persisted bytes.</param>
        /// <param name="cancellationToken">The verification token.</param>
        /// <returns>The verification result.</returns>
        public Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken)
            => CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(exactBytes.Span)) ? Task.CompletedTask : Task.FromException(new InvalidDataException("The structural fixture authority changed."));

        /// <summary>
        /// Completes fixture-only component verification after the engine's complete snapshot checks.
        /// </summary>
        /// <param name="description">The authenticated descriptor.</param>
        /// <param name="cancellationToken">The verification token.</param>
        /// <returns>A completed fixture check.</returns>
        public Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
