using Ironmon.Updater.Core;
using static Ironmon.Updater.Infrastructure.TransactionStorage;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Applies an independently authorized file plan with complete backups and restartable rollback.
/// </summary>
/// <remarks>
/// Constructs an engine with trusted authorization and an installation-specific activity check. Release authentication and UI orchestration supply these dependencies; a journal cannot supply them.
/// </remarks>
/// <param name="authority">The independent descriptor and final-component verifier.</param>
/// <param name="ensureIdle">The check that the bound game and tracker have exited and no replacement game process is running.</param>
/// <param name="progress">An optional synchronous observer of completed durability boundaries.</param>
/// <param name="availableSpace">An optional platform capacity provider, used for controlled full-disk fixtures.</param>
public sealed partial class UpdateTransaction(ITransactionAuthority authority, Func<CancellationToken, Task> ensureIdle, Action<TransactionProgress>? progress = null, Func<string, long>? availableSpace = null)
{
    private const int SchemaVersion = 1;
    private const int EngineVersion = 1;
    private const long ReserveBytes = 32 * 1024 * 1024;
    private const string ReplacementFile = "replacement.bin";
    private const string ProbeFile = "write-probe";
    private readonly ITransactionAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly Func<CancellationToken, Task> _ensureIdle = ensureIdle ?? throw new ArgumentNullException(nameof(ensureIdle));
    private readonly Action<TransactionProgress>? _progress = progress;
    private readonly Func<string, long> _availableSpace = availableSpace ?? ReadAvailableSpace;

    /// <summary>
    /// Captures a bounded descriptor without modifying program files; independent authorization is still required.
    /// </summary>
    /// <param name="installationRoot">The existing destination.</param>
    /// <param name="plan">The reviewed complete plan.</param>
    /// <param name="preparedGitRoot">A separately prepared ordinary Git metadata tree, when Git participates.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    /// <returns>The descriptor to authenticate before preparation.</returns>
    public static async Task<TransactionDescription> DescribeAsync(string installationRoot, FileUpdatePlan plan, string? preparedGitRoot = null, CancellationToken cancellationToken = default)
    {
        using var lease = InstallationLease.Acquire(installationRoot);
        var identity = InstallationIdentity.Read(lease.Root);
        var identityPath = PlainPaths.Child(StateRoot(lease.Root), InstallationFile);
        var installation = File.Exists(identityPath) ? Deserialize<InstallationRecord>(ReadBytes(identityPath)) : new InstallationRecord(SchemaVersion, Guid.NewGuid(), lease.Root, identity);
        if (installation.SchemaVersion != SchemaVersion || installation.Root != lease.Root || installation.RootIdentity != identity || installation.Id == Guid.Empty)
            throw new InvalidDataException(UpdaterText.UpdateTransactionTheSavedInstallationIdentityDoesNotMatchThisDirectory);

        if (!File.Exists(identityPath))
            WriteDurable(identityPath, Serialize(installation));

        plan.GetOperations();
        var git = PlainPaths.Child(lease.Root, GitDirectory);
        var before = preparedGitRoot is not null && Directory.Exists(git) ? await TreeAsync(git, cancellationToken).ConfigureAwait(false) : null;
        var after = preparedGitRoot is null ? null : await TreeAsync(PlainPaths.Full(preparedGitRoot), cancellationToken).ConfigureAwait(false);
        return new TransactionDescription(SchemaVersion, EngineVersion, installation.Id, identity, Guid.NewGuid(), lease.Root, plan.Export(), before, after);
    }

    /// <summary>
    /// Verifies and durably stages every replacement and backup before publishing the active transaction marker.
    /// </summary>
    /// <param name="description">The independently authorized descriptor.</param>
    /// <param name="payloadRoot">The verified extraction root using installation-relative file paths.</param>
    /// <param name="preparedGitRoot">The complete prepared Git metadata, if participating.</param>
    /// <param name="cancellationToken">Cancellation is safe throughout preparation.</param>
    /// <returns>A task completing only when recovery can independently read the prepared transaction.</returns>
    public async Task PrepareAsync(TransactionDescription description, string payloadRoot, string? preparedGitRoot = null, CancellationToken cancellationToken = default)
        => await PrepareCoreAsync(description, payloadRoot, preparedGitRoot, false, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Takes ownership of disposable extraction files when preparing an empty installation on the same volume.
    /// </summary>
    /// <param name="description">The independently authorized descriptor.</param>
    /// <param name="payloadRoot">The disposable extraction directory.</param>
    /// <param name="preparedGitRoot">The prepared Git metadata.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    internal Task PrepareOwnedAsync(TransactionDescription description, string payloadRoot, string? preparedGitRoot, CancellationToken cancellationToken)
        => PrepareCoreAsync(description, payloadRoot, preparedGitRoot, true, cancellationToken);

    /// <summary>
    /// Stages durable recovery data with bounded file work before publishing any mutation intent.
    /// </summary>
    /// <param name="description">The independently authorized descriptor.</param>
    /// <param name="payloadRoot">The verified extraction directory.</param>
    /// <param name="preparedGitRoot">The prepared Git metadata.</param>
    /// <param name="consumePayload">Whether the caller transfers ownership of its disposable extraction.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    private async Task PrepareCoreAsync(TransactionDescription description, string payloadRoot, string? preparedGitRoot, bool consumePayload, CancellationToken cancellationToken)
    {
        InstallationProgressScope.Report(new(InstallationStage.PreparingRecovery));
        var bytes = Serialize(description);
        description = Deserialize<TransactionDescription>(bytes);
        using var lease = InstallationLease.Acquire(description.InstallationRoot);
        var plan = await ValidateAsync(description, bytes, lease.Root, cancellationToken).ConfigureAwait(false);
        await RequireBeforeAsync(description, plan, false, cancellationToken).ConfigureAwait(false);
        var operations = plan.GetOperations();
        var transferred = consumePayload && IsFreshInstallation(description, operations) && StringComparer.OrdinalIgnoreCase.Equals(Path.GetPathRoot(payloadRoot), Path.GetPathRoot(lease.Root));
        CheckSpace(lease.Root, transferred ? checked(ReserveBytes + 2 * (description.GitAfter?.Sum(entry => entry.Content?.Length ?? 0) ?? 0)) : RequiredBytes(description, operations));
        var directory = TransactionRoot(lease.Root, description.TransactionId);
        if (Path.Exists(directory))
            throw new IOException(UpdaterText.UpdateTransactionThisTransactionIdentifierAlreadyHasRecoveryData);

        Directory.CreateDirectory(directory);
        WriteDurable(PlainPaths.Child(directory, DescriptionFile), bytes);
        if (transferred)
            Directory.Move(PlainPaths.Full(payloadRoot), PlainPaths.Child(directory, PayloadDirectory));

        var preparedCount = 0;
        var progressGate = new Lock();
        InstallationProgressScope.Report(new(InstallationStage.PreparingRecovery, 0, operations.Count));
        await Parallel.ForEachAsync(operations, new ParallelOptions { MaxDegreeOfParallelism = FileConcurrency, CancellationToken = cancellationToken }, async (operation, token) =>
        {
            if (operation.ExpectedBefore is not null)
                await CopyAsync(PlainPaths.Child(lease.Root, operation.Path), StoredFile(directory, BackupDirectory, operation.Path), operation.ExpectedBefore, token).ConfigureAwait(false);

            if (operation.Kind == FileOperationKind.WriteFile)
            {
                if (transferred)
                {
                    await FlushContentAsync(StoredFile(directory, PayloadDirectory, operation.Path), operation.ExpectedAfter!, token).ConfigureAwait(false);
                }
                else
                {
                    await CopyAsync(PlainPaths.Child(payloadRoot, operation.Path), StoredFile(directory, PayloadDirectory, operation.Path), operation.ExpectedAfter!, token).ConfigureAwait(false);
                }
            }

            lock (progressGate)
                InstallationProgressScope.Report(new(InstallationStage.PreparingRecovery, ++preparedCount, operations.Count));
        }).ConfigureAwait(false);

        if (description.GitAfter is not null)
        {
            InstallationProgressScope.Report(new(InstallationStage.PreparingRecovery));
            ArgumentException.ThrowIfNullOrWhiteSpace(preparedGitRoot);
            await CopyTreeAsync(preparedGitRoot, PlainPaths.Child(directory, GitPreparedDirectory), description.GitAfter, cancellationToken).ConfigureAwait(false);
            if (description.GitBefore is not null)
                await CopyTreeAsync(PlainPaths.Child(lease.Root, GitDirectory), PlainPaths.Child(directory, GitBackupDirectory), description.GitBefore, cancellationToken).ConfigureAwait(false);
        }

        await RequireBeforeAsync(description, plan, false, cancellationToken).ConfigureAwait(false);
        var digest = Hash(bytes);
        WriteJournal(directory, new TransactionJournal(SchemaVersion, 0, digest, TransactionPhase.Prepared, -1, null));
        cancellationToken.ThrowIfCancellationRequested();
        WriteDurable(PlainPaths.Child(StateRoot(lease.Root), InstallationLease.ActiveFile), Serialize(new ActiveTransaction(SchemaVersion, description.TransactionId, digest)));
    }

    /// <summary>
    /// Applies only a freshly prepared transaction; partial transactions are recovered instead of resumed forward.
    /// </summary>
    /// <param name="installationRoot">The bound destination.</param>
    /// <param name="transactionId">The exact prepared transaction.</param>
    /// <param name="relaunch">An optional original-user tracker relaunch after the lease is released.</param>
    /// <param name="cancellationToken">Cancellation after intent routes through non-cancellable rollback.</param>
    /// <returns>The durable installation outcome and separate relaunch failure, if any.</returns>
    public async Task<TransactionResult> ApplyAsync(string installationRoot, Guid transactionId, Func<Task>? relaunch = null, CancellationToken cancellationToken = default)
    {
        TransactionResult result;
        using (var lease = InstallationLease.Acquire(installationRoot, recovery: true))
        {
            var loaded = await LoadAsync(lease.Root, transactionId, cancellationToken).ConfigureAwait(false);
            var (description, plan, directory, journal) = loaded;
            if (journal.Phase != TransactionPhase.Prepared)
                throw new InvalidOperationException(UpdaterText.UpdateTransactionThisTransactionMustUseRecoveryRatherThanReplayingApplication);

            await RequireBeforeAsync(description, plan, true, cancellationToken).ConfigureAwait(false);
            await VerifyCopiesAsync(description, plan, directory, includePayload: true, cancellationToken).ConfigureAwait(false);
            CheckSpace(lease.Root, IsFreshInstallation(description, plan.GetOperations()) ? ReserveBytes : RequiredBytes(description, plan.GetOperations()));
            ProbeWrite(directory);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var operations = plan.GetOperations();
                InstallationProgressScope.Report(new(InstallationStage.InstallingFiles, 0, operations.Count));
                var fresh = IsFreshInstallation(description, operations);
                if (fresh && operations.Count > 0)
                {
                    journal = Save(directory, journal, TransactionPhase.Applying, operations.Count - 1);
                    Notify(journal, TransactionBoundary.Intent);
                    await PromoteFreshAsync(description, directory, operations, journal, cancellationToken).ConfigureAwait(false);
                    journal = Save(directory, journal, TransactionPhase.Applying, operations.Count - 1);
                    Notify(journal, TransactionBoundary.Completion);
                }

                for (var index = 0; !fresh && index < operations.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _ensureIdle(cancellationToken).ConfigureAwait(false);
                    journal = Save(directory, journal, TransactionPhase.Applying, index);
                    Notify(journal, TransactionBoundary.Intent);
                    await ApplyFileAsync(description.InstallationRoot, directory, operations[index], cancellationToken).ConfigureAwait(false);
                    Notify(journal, TransactionBoundary.Mutation);
                    Notify(journal, TransactionBoundary.Completion);
                    InstallationProgressScope.Report(new(InstallationStage.InstallingFiles, index + 1, operations.Count));
                }

                if (description.GitAfter is not null)
                {
                    await _ensureIdle(cancellationToken).ConfigureAwait(false);
                    journal = Save(directory, journal, TransactionPhase.Applying, operations.Count);
                    Notify(journal, TransactionBoundary.Intent);
                    await PromoteGitAsync(description, directory, journal, cancellationToken).ConfigureAwait(false);
                    Notify(journal, TransactionBoundary.Mutation);
                    journal = Save(directory, journal, TransactionPhase.Applying, operations.Count);
                    Notify(journal, TransactionBoundary.Completion);
                }

                InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles));
                await VerifyAfterAsync(description, plan, cancellationToken).ConfigureAwait(false);
                journal = Save(directory, journal, TransactionPhase.Committed, journal.Cursor);
                ClearActive(lease.Root);
                result = new TransactionResult(TransactionPhase.Committed);
            }
            catch (Exception error)
            {
                result = journal.Phase == TransactionPhase.Committed
                    ? new TransactionResult(TransactionPhase.Committed, error.Message)
                    : await RollbackAsync(description, plan, directory, journal, error.Message).ConfigureAwait(false);
            }
        }

        if (result.Phase == TransactionPhase.Committed && relaunch is not null)
        {
            try
            {
                await relaunch().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                result = result with { RelaunchError = error.Message };
            }
        }

        return result;
    }

    /// <summary>
    /// Recovers from durable intent in a fresh process, retaining all evidence if restoration is blocked.
    /// </summary>
    /// <param name="installationRoot">The installation to recover.</param>
    /// <param name="transactionId">The exact active transaction.</param>
    /// <param name="cancellationToken">May cancel validation; rollback itself cannot be abandoned.</param>
    /// <returns>The verified committed or restored outcome, or a recovery-required blocker.</returns>
    public async Task<TransactionResult> RecoverAsync(string installationRoot, Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var lease = InstallationLease.Acquire(installationRoot, recovery: true);
        var (description, plan, directory, journal) = await LoadAsync(lease.Root, transactionId, cancellationToken).ConfigureAwait(false);
        if (journal.Phase == TransactionPhase.Committed)
        {
            await VerifyAfterAsync(description, plan, cancellationToken).ConfigureAwait(false);
            ClearActive(lease.Root);
            return new TransactionResult(TransactionPhase.Committed);
        }

        if (journal.Phase == TransactionPhase.RolledBack)
        {
            await RequireBeforeAsync(description, plan, true, cancellationToken).ConfigureAwait(false);
            ClearActive(lease.Root);
            return new TransactionResult(TransactionPhase.RolledBack);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await RollbackAsync(description, plan, directory, journal, journal.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Authenticates a prepared transaction and every recovery copy before an independent helper acknowledges readiness.
    /// </summary>
    /// <param name="installationRoot">The bound installation.</param>
    /// <param name="transactionId">The active transaction.</param>
    /// <param name="cancellationToken">The bounded handoff token.</param>
    /// <returns>A task that fails without closing the tracker if recovery is not ready.</returns>
    public async Task ValidatePreparedAsync(string installationRoot, Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var lease = InstallationLease.Acquire(installationRoot, recovery: true);
        var (description, plan, directory, journal) = await LoadAsync(lease.Root, transactionId, cancellationToken).ConfigureAwait(false);
        if (journal.Phase != TransactionPhase.Prepared)
            throw new InvalidOperationException(UpdaterText.UpdateTransactionTheUnfinishedTransactionRequiresRecoveryBeforeAnotherUpdateCan);

        await VerifyCopiesAsync(description, plan, directory, includePayload: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads only a bounded active transaction identity; this observation never authorizes recovery.
    /// </summary>
    /// <param name="installationRoot">The installed game directory.</param>
    /// <returns>The pending identity, or null when no update is active.</returns>
    public static Guid? ReadActiveId(string installationRoot)
    {
        var path = PlainPaths.Child(StateRoot(PlainPaths.Full(installationRoot)), InstallationLease.ActiveFile);
        if (!File.Exists(path))
            return null;

        if (new FileInfo(path).Length > 4096)
            throw new InvalidDataException(UpdaterText.UpdateTransactionThePendingUpdateRecordIsTooLarge);

        var active = Deserialize<ActiveTransaction>(ReadBytes(path));
        if (active.SchemaVersion != SchemaVersion || active.Id == Guid.Empty)
            throw new InvalidDataException(UpdaterText.UpdateTransactionThePendingUpdateRecordIsInvalid);

        return active.Id;
    }

    /// <summary>
    /// Authenticates pending recovery before the tracker hands control to the independent window.
    /// </summary>
    /// <param name="installationRoot">The bound installation.</param>
    /// <param name="transactionId">The pending transaction identity.</param>
    /// <param name="cancellationToken">The validation token.</param>
    /// <returns>A task that rejects altered recovery authority without closing the tracker.</returns>
    public async Task ValidateRecoveryAsync(string installationRoot, Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var lease = InstallationLease.Acquire(installationRoot, recovery: true);
        await LoadAsync(lease.Root, transactionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Abandons a preparation without replacing files, and refuses any transaction with durable application intent.
    /// </summary>
    /// <param name="installationRoot">The bound installation.</param>
    /// <param name="transactionId">The exact preparation owned by the caller.</param>
    /// <param name="cancellationToken">The validation token.</param>
    /// <returns>A task that retains recovery evidence while releasing the active marker.</returns>
    public async Task DiscardPreparedAsync(string installationRoot, Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var lease = InstallationLease.Acquire(installationRoot, recovery: true);
        var (description, plan, directory, journal) = await LoadAsync(lease.Root, transactionId, cancellationToken).ConfigureAwait(false);
        if (journal.Phase != TransactionPhase.Prepared)
            throw new InvalidOperationException(UpdaterText.UpdateTransactionThisUpdateHasAlreadyStartedReplacingFilesAndRequires);

        WriteJournal(directory, journal with { Generation = journal.Generation + 1, Phase = TransactionPhase.RolledBack });
        ClearActive(lease.Root);
    }

    /// <summary>
    /// Authenticates a loaded descriptor, checks filesystem identity and rebuilds all operations from policy.
    /// </summary>
    /// <param name="description">The immutable descriptor.</param>
    /// <param name="bytes">Its exact authorized bytes.</param>
    /// <param name="root">The actual leased installation.</param>
    /// <param name="cancellationToken">The validation token.</param>
    /// <returns>The reconstructed safe plan.</returns>
    private async Task<FileUpdatePlan> ValidateAsync(TransactionDescription description, byte[] bytes, string root, CancellationToken cancellationToken)
    {
        if (description.SchemaVersion != SchemaVersion || description.EngineVersion != EngineVersion || description.TransactionId == Guid.Empty || description.InstallationRoot != root || description.RootIdentity != InstallationIdentity.Read(root))
            throw new InvalidDataException(UpdaterText.UpdateTransactionThisRecoveryEngineInstallationPathOrDirectoryIdentityDoes);

        var installation = Deserialize<InstallationRecord>(ReadBytes(PlainPaths.Child(StateRoot(root), InstallationFile)));
        if (installation.SchemaVersion != SchemaVersion || installation.Id != description.InstallationId || installation.Root != root || installation.RootIdentity != description.RootIdentity)
            throw new InvalidDataException(UpdaterText.UpdateTransactionTheInstallationIdentityChangedAfterReview);

        await _authority.AuthorizeAsync(description, bytes, cancellationToken).ConfigureAwait(false);
        var plan = description.Plan.Rebuild();
        plan.GetOperations();
        return plan;
    }

    /// <summary>
    /// Loads only the active transaction and verifies its descriptor binding and supported journal state.
    /// </summary>
    /// <param name="root">The leased installation.</param>
    /// <param name="id">The requested transaction.</param>
    /// <param name="cancellationToken">The validation token.</param>
    /// <returns>The authenticated inputs and durable state.</returns>
    private async Task<(TransactionDescription Description, FileUpdatePlan Plan, string Directory, TransactionJournal Journal)> LoadAsync(string root, Guid id, CancellationToken cancellationToken)
    {
        var active = Deserialize<ActiveTransaction>(ReadBytes(PlainPaths.Child(StateRoot(root), InstallationLease.ActiveFile)));
        var directory = TransactionRoot(root, id);
        var bytes = ReadBytes(PlainPaths.Child(directory, DescriptionFile));
        if (active.SchemaVersion != SchemaVersion || active.Id != id || active.DescriptionHash != Hash(bytes))
            throw new InvalidDataException(UpdaterText.UpdateTransactionTheActiveTransactionDoesNotMatchItsDescriptor);

        var description = Deserialize<TransactionDescription>(bytes);
        if (description.TransactionId != id)
            throw new InvalidDataException(UpdaterText.UpdateTransactionTheTransactionDirectoryIdentityDoesNotMatchItsDescriptor);

        var plan = await ValidateAsync(description, bytes, root, cancellationToken).ConfigureAwait(false);
        var journal = ReadJournal(directory);
        var maximumCursor = plan.GetOperations().Count - (description.GitAfter is null ? 1 : 0);
        if (journal.SchemaVersion != SchemaVersion || journal.Generation < 0 || !Enum.IsDefined(journal.Phase) || journal.DescriptionHash != active.DescriptionHash || journal.Cursor < -1 || journal.Cursor > maximumCursor || (journal.Phase == TransactionPhase.Prepared && journal.Cursor != -1))
            throw new InvalidDataException(UpdaterText.UpdateTransactionTheTransactionJournalHasUnsupportedOrInconsistentProgress);

        return (description, plan, directory, journal);
    }

    /// <summary>
    /// Revalidates complete original bytes and Git metadata while the installation is idle.
    /// </summary>
    /// <param name="description">The authorized transaction.</param>
    /// <param name="plan">The reconstructed plan.</param>
    /// <param name="requireIdle">Whether program processes must already have exited before this inspection.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    private async Task RequireBeforeAsync(TransactionDescription description, FileUpdatePlan plan, bool requireIdle, CancellationToken cancellationToken)
    {
        if (requireIdle)
            await _ensureIdle(cancellationToken).ConfigureAwait(false);
        var snapshot = await InstallationFileSnapshot.ReadAsync(description.InstallationRoot, cancellationToken).ConfigureAwait(false);
        if (!snapshot.SequenceEqual(description.Plan.Local.OrderBy(entry => entry.Path, StringComparer.Ordinal)))
            throw new IOException(UpdaterText.UpdateTransactionInstallationFilesChangedSinceReviewApplicationOrRecoveryCannot);

        if (description.GitAfter is not null)
            await RequireGitAsync(description.InstallationRoot, description.GitBefore, cancellationToken).ConfigureAwait(false);

        plan.GetOperations();
    }

    /// <summary>
    /// Verifies complete resulting files and Git bytes before trusted component-level verification.
    /// </summary>
    /// <param name="description">The authorized transaction.</param>
    /// <param name="plan">The reviewed plan.</param>
    /// <param name="cancellationToken">The verification token.</param>
    private async Task VerifyAfterAsync(TransactionDescription description, FileUpdatePlan plan, CancellationToken cancellationToken)
    {
        await _ensureIdle(cancellationToken).ConfigureAwait(false);
        if (plan.Assess(await InstallationFileSnapshot.ReadAsync(description.InstallationRoot, cancellationToken).ConfigureAwait(false)) != FilePlanApplicability.AlreadyApplied)
            throw new IOException(UpdaterText.UpdateTransactionTheFinalInstallationDoesNotMatchTheCompleteReviewed);

        if (description.GitAfter is not null)
            await RequireGitAsync(description.InstallationRoot, description.GitAfter, cancellationToken).ConfigureAwait(false);

        await _authority.VerifyInstalledAsync(description, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every backup before any restoration, and every payload before application.
    /// </summary>
    /// <param name="description">The authorized transaction.</param>
    /// <param name="plan">The reconstructed plan.</param>
    /// <param name="directory">The owned transaction directory.</param>
    /// <param name="includePayload">Whether to require forward-application payloads as well.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    private static async Task VerifyCopiesAsync(TransactionDescription description, FileUpdatePlan plan, string directory, bool includePayload, CancellationToken cancellationToken)
    {
        var operations = plan.GetOperations();
        var verified = 0;
        var progressGate = new Lock();
        var fresh = IsFreshInstallation(description, operations);
        InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles, 0, operations.Count));
        if (includePayload && fresh && operations.Count > 0)
        {
            var expected = operations.Select(operation => new LocalFileEntry(operation.Path, operation.ExpectedAfter)).OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
            if (!(await TreeAsync(PlainPaths.Child(directory, PayloadDirectory), cancellationToken).ConfigureAwait(false)).SequenceEqual(expected))
                throw new InvalidDataException(UpdaterText.UpdateTransactionAReplacementPayloadFailedVerification);
        }

        await Parallel.ForEachAsync(fresh ? [] : operations, new ParallelOptions { MaxDegreeOfParallelism = FileConcurrency, CancellationToken = cancellationToken }, async (operation, token) =>
        {
            if (operation.ExpectedBefore is not null && await ContentAsync(StoredFile(directory, BackupDirectory, operation.Path), token).ConfigureAwait(false) != operation.ExpectedBefore)
                throw new InvalidDataException(UpdaterText.UpdateTransactionARecoveryBackupFailedVerification);

            if (includePayload && operation.Kind == FileOperationKind.WriteFile && await ContentAsync(StoredFile(directory, PayloadDirectory, operation.Path), token).ConfigureAwait(false) != operation.ExpectedAfter)
                throw new InvalidDataException(UpdaterText.UpdateTransactionAReplacementPayloadFailedVerification);

            lock (progressGate)
                InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles, ++verified, operations.Count));
        }).ConfigureAwait(false);

        if (description.GitBefore is not null && !(await TreeAsync(PlainPaths.Child(directory, GitBackupDirectory), cancellationToken).ConfigureAwait(false)).SequenceEqual(description.GitBefore))
            throw new InvalidDataException(UpdaterText.UpdateTransactionTheGitRollbackBackupFailedVerification);

        if (includePayload && description.GitAfter is not null && !(await TreeAsync(PlainPaths.Child(directory, GitPreparedDirectory), cancellationToken).ConfigureAwait(false)).SequenceEqual(description.GitAfter))
            throw new InvalidDataException(UpdaterText.UpdateTransactionThePreparedGitTreeFailedVerification);
    }

    /// <summary>
    /// Applies one bounded file operation after checking its immediate precondition again.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="directory">The transaction directory.</param>
    /// <param name="operation">The validated operation.</param>
    /// <param name="cancellationToken">The mutation token.</param>
    private static async Task ApplyFileAsync(string root, string directory, PlannedFileOperation operation, CancellationToken cancellationToken)
    {
        var path = PlainPaths.Child(root, operation.Path);
        using var ancestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(path) : null;
        if (operation.Kind == FileOperationKind.CreateDirectory)
        {
            if (Path.Exists(path))
                throw new IOException(UpdaterText.UpdateTransactionAPlannedNewDirectoryIsUnexpectedlyOccupied);

            Directory.CreateDirectory(path);
        }
        else if (operation.Kind == FileOperationKind.RemoveDirectory)
        {
            Directory.Delete(path, recursive: false);
        }
        else
        {
            await RequireFileAsync(path, operation.ExpectedBefore, cancellationToken).ConfigureAwait(false);
            if (operation.Kind == FileOperationKind.DeleteFile)
            {
                File.Delete(path);
            }
            else
            {
                var payload = StoredFile(directory, PayloadDirectory, operation.Path);
                using var payloadAncestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(payload) : null;
                if (await ContentAsync(payload, cancellationToken).ConfigureAwait(false) != operation.ExpectedAfter)
                    throw new InvalidDataException(UpdaterText.UpdateTransactionAReplacementPayloadFailedVerification);

                File.Move(payload, path, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Restores only operations with durable intent and verifies the entire original snapshot before releasing writers.
    /// </summary>
    /// <param name="description">The authenticated transaction.</param>
    /// <param name="plan">The reviewed file plan.</param>
    /// <param name="directory">The recovery directory.</param>
    /// <param name="journal">The latest known durable intent.</param>
    /// <param name="error">The initiating failure.</param>
    /// <returns>A restored outcome or a retained recovery blocker.</returns>
    private async Task<TransactionResult> RollbackAsync(TransactionDescription description, FileUpdatePlan plan, string directory, TransactionJournal journal, string? error)
    {
        try
        {
            await _ensureIdle(CancellationToken.None).ConfigureAwait(false);
            InstallationProgressScope.Report(new(InstallationStage.RestoringFiles));
            await VerifyCopiesAsync(description, plan, directory, includePayload: false, CancellationToken.None).ConfigureAwait(false);
            journal = Save(directory, journal, TransactionPhase.RollingBack, journal.Cursor, error);
            var operations = plan.GetOperations();
            if (description.GitAfter is not null && journal.Cursor == operations.Count)
            {
                Notify(journal, TransactionBoundary.Intent);
                await RestoreGitAsync(description, directory, journal).ConfigureAwait(false);
                Notify(journal, TransactionBoundary.Mutation);
                journal = Save(directory, journal, TransactionPhase.RollingBack, journal.Cursor, error);
                Notify(journal, TransactionBoundary.Completion);
            }

            for (var index = Math.Min(journal.Cursor, operations.Count - 1); index >= 0; index--)
            {
                await _ensureIdle(CancellationToken.None).ConfigureAwait(false);
                journal = Save(directory, journal, TransactionPhase.RollingBack, index, error);
                Notify(journal, TransactionBoundary.Intent);
                await RestoreFileAsync(description.InstallationRoot, directory, operations[index]).ConfigureAwait(false);
                Notify(journal, TransactionBoundary.Mutation);
                journal = Save(directory, journal, TransactionPhase.RollingBack, index - 1, error);
                Notify(journal, TransactionBoundary.Completion);
                InstallationProgressScope.Report(new(InstallationStage.RestoringFiles, operations.Count - index, operations.Count));
            }

            await RequireBeforeAsync(description, plan, true, CancellationToken.None).ConfigureAwait(false);
            Save(directory, journal, TransactionPhase.RolledBack, -1, error);
            ClearActive(description.InstallationRoot);
            return new TransactionResult(TransactionPhase.RolledBack, error);
        }
        catch (Exception recoveryError)
        {
            var detail = error is null ? recoveryError.Message : error + Environment.NewLine + recoveryError.Message;
            try
            {
                Save(directory, journal, TransactionPhase.RecoveryRequired, journal.Cursor, detail);
            }
            catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException)
            {
                detail += Environment.NewLine + journalError.Message;
            }

            return new TransactionResult(TransactionPhase.RecoveryRequired, detail);
        }
    }

    /// <summary>
    /// Reconciles actual before/after bytes so repeating an interrupted rollback is safe.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="directory">The verified backup directory owner.</param>
    /// <param name="operation">The operation whose intent may or may not have executed.</param>
    private static async Task RestoreFileAsync(string root, string directory, PlannedFileOperation operation)
    {
        var path = PlainPaths.Child(root, operation.Path);
        using var ancestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(path) : null;
        if (operation.Kind == FileOperationKind.CreateDirectory)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);

            if (File.Exists(path))
                throw new IOException(UpdaterText.UpdateTransactionANewDirectoryWasReplacedWithAnUnexpectedFile);
        }
        else if (operation.Kind == FileOperationKind.RemoveDirectory)
        {
            if (File.Exists(path))
                throw new IOException(UpdaterText.UpdateTransactionARemovedDirectoryIsUnexpectedlyOccupiedDuringRecovery);

            Directory.CreateDirectory(path);
        }
        else
        {
            if (Directory.Exists(path))
                throw new IOException(UpdaterText.UpdateTransactionAManagedFileIsUnexpectedlyADirectoryDuringRecovery);

            var current = File.Exists(path) ? await ContentAsync(path, CancellationToken.None).ConfigureAwait(false) : null;
            if (current == operation.ExpectedBefore)
                return;

            if (current != operation.ExpectedAfter)
                throw new IOException(UpdaterText.UpdateTransactionAManagedFileChangedOutsideTheTransactionItsBackup);

            if (operation.ExpectedBefore is null)
            {
                File.Delete(path);
            }
            else
            {
                await ReplaceAsync(directory, StoredFile(directory, BackupDirectory, operation.Path), path, operation.ExpectedBefore, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Promotes a complete prepared Git tree through two recoverable same-volume renames.
    /// </summary>
    /// <param name="description">The authenticated before/after metadata snapshots.</param>
    /// <param name="directory">The transaction owner.</param>
    /// <param name="journal">The durable Git intent.</param>
    /// <param name="cancellationToken">The verification token.</param>
    private async Task PromoteGitAsync(TransactionDescription description, string directory, TransactionJournal journal, CancellationToken cancellationToken)
    {
        await RequireGitAsync(description.InstallationRoot, description.GitBefore, cancellationToken).ConfigureAwait(false);
        var live = PlainPaths.Child(description.InstallationRoot, GitDirectory);
        if (description.GitBefore is not null)
            Directory.Move(live, PlainPaths.Child(directory, GitRetiredDirectory));

        Notify(journal, TransactionBoundary.GitDetached);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(PlainPaths.Child(directory, GitPreparedDirectory), live);
    }

    /// <summary>
    /// Restores index, refs, objects and all original Git metadata together, including ZIP installations with no original Git directory.
    /// </summary>
    /// <param name="description">The authenticated metadata inventories.</param>
    /// <param name="directory">The transaction owner.</param>
    /// <param name="journal">The durable Git rollback intent.</param>
    private async Task RestoreGitAsync(TransactionDescription description, string directory, TransactionJournal journal)
    {
        var live = PlainPaths.Child(description.InstallationRoot, GitDirectory);
        if (File.Exists(live))
            throw new IOException(UpdaterText.UpdateTransactionGitMetadataIsUnexpectedlyAFile);

        var current = Directory.Exists(live) ? await TreeAsync(live, CancellationToken.None).ConfigureAwait(false) : null;
        if (TreesEqual(current, description.GitBefore))
            return;

        if (current is not null && !TreesEqual(current, description.GitAfter))
            throw new IOException(UpdaterText.UpdateTransactionGitMetadataChangedOutsideTheTransactionRecoveryRequiresAttention);

        var restore = PlainPaths.Child(directory, GitRestoreDirectory);
        if (description.GitBefore is not null)
            await CopyTreeAsync(PlainPaths.Child(directory, GitBackupDirectory), restore, description.GitBefore, CancellationToken.None).ConfigureAwait(false);

        if (current is not null)
            Directory.Move(live, PlainPaths.Child(directory, GitPreparedDirectory));

        Notify(journal, TransactionBoundary.GitDetached);
        if (description.GitBefore is not null)
            Directory.Move(restore, live);
    }

    /// <summary>
    /// Compares missing and complete Git metadata states without treating missing as empty.
    /// </summary>
    /// <param name="left">The observed snapshot.</param>
    /// <param name="right">The expected snapshot.</param>
    /// <returns>Whether both complete states agree.</returns>
    private static bool TreesEqual(LocalFileEntry[]? left, LocalFileEntry[]? right)
        => left is null ? right is null : right is not null && left.SequenceEqual(right);

    /// <summary>
    /// Requires exact complete Git metadata or its original absence.
    /// </summary>
    /// <param name="root">The installation.</param>
    /// <param name="expected">The expected tree or absence.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    private static async Task RequireGitAsync(string root, LocalFileEntry[]? expected, CancellationToken cancellationToken)
    {
        var path = PlainPaths.Child(root, GitDirectory);
        var current = Directory.Exists(path) ? await TreeAsync(path, cancellationToken).ConfigureAwait(false) : null;
        if (File.Exists(path) || !TreesEqual(current, expected))
            throw new IOException(UpdaterText.UpdateTransactionGitMetadataNoLongerMatchesTheReviewedState);
    }

    /// <summary>
    /// Requires one exact file identity or a genuinely absent path.
    /// </summary>
    /// <param name="path">The checked destination.</param>
    /// <param name="expected">The required content or absence.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    private static async Task RequireFileAsync(string path, GameFileContent? expected, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path) || (File.Exists(path) ? await ContentAsync(path, cancellationToken).ConfigureAwait(false) : null) != expected)
            throw new IOException(UpdaterText.UpdateTransactionADestinationChangedImmediatelyBeforeItsPlannedMutation);
    }

    /// <summary>
    /// Creates a verified flushed replacement outside the program tree before a same-volume atomic move.
    /// </summary>
    /// <param name="directory">The transaction-owned scratch location.</param>
    /// <param name="source">The verified immutable source.</param>
    /// <param name="destination">The already revalidated program destination.</param>
    /// <param name="expected">The exact replacement content.</param>
    /// <param name="cancellationToken">The copy token.</param>
    private static async Task ReplaceAsync(string directory, string source, string destination, GameFileContent expected, CancellationToken cancellationToken)
    {
        var scratch = PlainPaths.Child(directory, ReplacementFile);
        await CopyAsync(source, scratch, expected, cancellationToken).ConfigureAwait(false);
        PlainPaths.Full(destination);
        File.Move(scratch, destination, overwrite: true);
    }

    /// <summary>
    /// Persists a new generation before returning the updated in-memory state.
    /// </summary>
    /// <param name="directory">The transaction directory.</param>
    /// <param name="journal">The last durable generation.</param>
    /// <param name="phase">The next phase.</param>
    /// <param name="cursor">The highest possibly applied operation.</param>
    /// <param name="error">The retained error.</param>
    /// <returns>The newly flushed journal.</returns>
    private static TransactionJournal Save(string directory, TransactionJournal journal, TransactionPhase phase, int cursor, string? error = null)
    {
        var next = journal with { Generation = checked(journal.Generation + 1), Phase = phase, Cursor = cursor, Error = error };
        WriteJournal(directory, next);
        return next;
    }

    /// <summary>
    /// Publishes a completed boundary synchronously, including to deterministic fault-injection fixtures.
    /// </summary>
    /// <param name="journal">The current phase and operation.</param>
    /// <param name="boundary">The completed boundary.</param>
    private void Notify(TransactionJournal journal, TransactionBoundary boundary)
        => _progress?.Invoke(new TransactionProgress(journal.Phase, journal.Cursor, boundary));

    /// <summary>
    /// Computes a conservative budget for staging, backups and rollback scratch copies with overflow checks.
    /// </summary>
    /// <param name="description">The metadata inventories.</param>
    /// <param name="operations">The reviewed file operations.</param>
    /// <returns>The required free byte budget.</returns>
    private static long RequiredBytes(TransactionDescription description, IReadOnlyList<PlannedFileOperation> operations)
    {
        checked
        {
            var files = operations.Sum(operation => (operation.ExpectedBefore?.Length ?? 0) + (operation.ExpectedAfter?.Length ?? 0));
            var git = (description.GitBefore?.Sum(entry => entry.Content?.Length ?? 0) ?? 0) + (description.GitAfter?.Sum(entry => entry.Content?.Length ?? 0) ?? 0);
            return ReserveBytes + 2 * (files + git);
        }
    }

    /// <summary>
    /// Fails before mutation when the volume cannot accommodate preparation and recovery.
    /// </summary>
    /// <param name="root">The destination volume.</param>
    /// <param name="required">The conservative required bytes.</param>
    private void CheckSpace(string root, long required)
    {
        if (_availableSpace(root) < required)
            throw new IOException(UpdaterText.UpdateTransactionThereIsInsufficientFreeSpaceToUpdateAndRetain);
    }

    /// <summary>
    /// Reads current free capacity for the destination volume.
    /// </summary>
    /// <param name="root">The installation directory.</param>
    /// <returns>The currently available bytes.</returns>
    private static long ReadAvailableSpace(string root)
        => new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;

    /// <summary>
    /// Proves the current process can durably write recovery state before applying.
    /// </summary>
    /// <param name="directory">The transaction directory.</param>
    private static void ProbeWrite(string directory)
    {
        var path = PlainPaths.Child(directory, ProbeFile);
        WriteDurable(path, []);
        File.Delete(path);
    }

    /// <summary>
    /// Gets the checked updater state root.
    /// </summary>
    /// <param name="root">The installation directory.</param>
    /// <returns>The protected state directory.</returns>
    private static string StateRoot(string root)
        => PlainPaths.Child(root, InstallationLease.StateDirectory);

    /// <summary>
    /// Gets a transaction directory from a typed identifier rather than a caller-provided relative path.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="id">The transaction UUID.</param>
    /// <returns>The bounded transaction directory.</returns>
    private static string TransactionRoot(string root, Guid id)
        => PlainPaths.Child(StateRoot(root), TransactionsDirectory + '/' + id.ToString(GuidFormat));

    /// <summary>
    /// Resolves an immutable staged or backup file by its validated installation-relative path.
    /// </summary>
    /// <param name="directory">The transaction owner.</param>
    /// <param name="kind">The fixed storage category.</param>
    /// <param name="relative">The managed path.</param>
    /// <returns>The checked stored file path.</returns>
    private static string StoredFile(string directory, string kind, string relative)
        => PlainPaths.Child(directory, kind + '/' + relative);

    /// <summary>
    /// Releases the durable writer blocker only after a verified terminal state is saved.
    /// </summary>
    /// <param name="root">The installation directory.</param>
    private static void ClearActive(string root)
        => File.Delete(PlainPaths.Child(StateRoot(root), InstallationLease.ActiveFile));

    /// <summary>
    /// Stores the stable installation locator separately from any one tracker release.
    /// </summary>
    /// <remarks>
    /// Constructs the locator; it supplements independent authorization rather than replacing it.
    /// </remarks>
    /// <param name="SchemaVersion">The locator schema.</param>
    /// <param name="Id">The installation UUID.</param>
    /// <param name="Root">The canonical installation path.</param>
    /// <param name="RootIdentity">The Windows directory identity.</param>
    private sealed record InstallationRecord(int SchemaVersion, Guid Id, string Root, string RootIdentity);

    /// <summary>
    /// Locates the one unfinished transaction for cooperating writers and standalone recovery.
    /// </summary>
    /// <remarks>
    /// Constructs the pointer; its digest detects mismatches but is not permission to apply.
    /// </remarks>
    /// <param name="SchemaVersion">The pointer schema.</param>
    /// <param name="Id">The active transaction UUID.</param>
    /// <param name="DescriptionHash">The bound descriptor digest.</param>
    private sealed record ActiveTransaction(int SchemaVersion, Guid Id, string DescriptionHash);
}
