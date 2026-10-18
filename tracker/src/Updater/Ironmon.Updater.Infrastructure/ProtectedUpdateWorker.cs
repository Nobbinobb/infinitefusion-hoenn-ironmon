using Ironmon.Updater.Core;
using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reauthenticates one reviewed selection and confines administrator work to its durable transaction.
/// </summary>
/// <remarks>
/// Constructs the same signed preparation and transaction services used by ordinary installations.
/// </remarks>
/// <param name="verifier">The helper's embedded release trust.</param>
/// <param name="downloads">An administrator-owned cache.</param>
/// <param name="runtime">The independent runtime prerequisite checker.</param>
/// <param name="gamePreparation">The private exact-revision game preparer.</param>
/// <param name="gameVerification">The independent game verifier.</param>
/// <param name="sprites">The fixed optional sprite service, with no caller-selected URL or executable.</param>
/// <param name="protectState">An optional isolated-test storage boundary; production uses the Windows administrator ACL policy.</param>
/// <param name="ensureIdle">An optional isolated-test process boundary; production checks the real installation and official launcher.</param>
[SupportedOSPlatform("windows")]
internal sealed class ProtectedUpdateWorker(ReleaseVerifier verifier, ReleaseDownloadStore downloads, ITrackerRuntimeCompatibility runtime, CombinedGamePreparation gamePreparation, CombinedGitVerification gameVerification, Func<string, bool, CancellationToken, Task<ProtectedSpriteResult>> sprites, Action<string>? protectState = null, Func<string, CancellationToken, Task>? ensureIdle = null) : IDisposable
{
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ITrackerRuntimeCompatibility _runtime = runtime;
    private readonly CombinedGamePreparation _gamePreparation = gamePreparation;
    private readonly CombinedGitVerification _gameVerification = gameVerification;
    private readonly Func<string, bool, CancellationToken, Task<ProtectedSpriteResult>> _sprites = sprites;
    private readonly Action<string> _protectState = protectState ?? WindowsUpdateAccess.ProtectState;
    private readonly Func<string, CancellationToken, Task> _ensureIdle = ensureIdle ?? ((root, token) => UpdateProcessIdentity.EnsureInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, token));
    private readonly List<SafeFileHandle> _handles = [];
    private bool _recover;
    private bool _committed;
    private bool _initialized;
    private bool _optionalOnly;

    /// <summary>
    /// Gets the single prepared transaction accepted by this worker.
    /// </summary>
    internal PreparedIronmonUpdate? Prepared { get; private set; }

    /// <summary>
    /// Authenticates every component and rechecks the pre-UAC directory identity before any installation writes.
    /// </summary>
    /// <param name="message">The initial exact selection.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The complete durable preparation.</returns>
    internal async Task<ProtectedUpdateReply> InitializeAsync(ProtectedUpdateMessage message, CancellationToken cancellationToken)
    {
        if (_initialized || message.Operation is not (ProtectedUpdateOperation.Prepare or ProtectedUpdateOperation.OpenRecovery or ProtectedUpdateOperation.OpenInstalled))
            throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerAnAdministratorSessionAcceptsOnlyOneInstallationSelection);

        _initialized = true;
        var authorization = message.Authorization ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerSignedInstallationEvidenceIsRequired);
        var root = PlainPaths.Full(message.Root);
        var ancestor = PlainPaths.Full(message.Ancestor ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheReviewedInstallationParentIsMissing));
        if (root == Path.GetPathRoot(root) || !root.Equals(PlainPaths.Full(authorization.Request.InstallationRoot), StringComparison.OrdinalIgnoreCase) || !(root.Equals(ancestor, StringComparison.OrdinalIgnoreCase) || root.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheAdministratorInstallationRootDiffersFromTheReviewedSelection);

        _handles.AddRange(WindowsUpdateAccess.LockAncestors(Path.Combine(ancestor, UpdaterHandoff.HelperFileName)));
        if (InstallationIdentity.Read(ancestor) != message.AncestorIdentity || (!root.Equals(ancestor, StringComparison.OrdinalIgnoreCase) && Path.Exists(root)))
            throw new IOException(UpdaterText.ProtectedUpdateWorkerTheInstallationDirectoryChangedWhileWindowsRequestedPermissionReview);

        var manifest = _verifier.Verify(authorization.Target.Manifest, authorization.Target.Signatures);
        IronmonOnlyUpdate.ValidateSelection(authorization.Request, manifest, authorization.Purpose);
        var authority = new SignedIronmonAuthority(_verifier, _runtime, _gameVerification);
        var inventories = authority.Resolve(authorization);
        var combined = authorization.PreviousGameCommit is not null || authorization.Purpose == InstallationPurpose.InstallGame;
        if (combined && authorization.Request.HasActiveRun)
            throw new InvalidOperationException(UpdaterText.ProtectedUpdateWorkerFinishTheActiveRunBeforeChangingTheGame);

        var helper = RecoveryHelperPackage.ResolveIdentity(manifest, inventories.Target) ?? await RecoveryHelperPackage.StageAsync(_downloads.Root, manifest, _downloads, authorization.Request.Flavor, cancellationToken).ConfigureAwait(false);
        if (await TransactionStorage.ContentAsync(Environment.ProcessPath ?? throw new IOException(UpdaterText.ProtectedUpdateWorkerTheRunningHelperIsUnavailable), cancellationToken).ConfigureAwait(false) != helper.Content)
            throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheElevatedExecutableDoesNotMatchTheAuthenticatedHelper);

        Directory.CreateDirectory(root);
        _handles.AddRange(WindowsUpdateAccess.LockAncestors(Path.Combine(root, UpdaterHandoff.HelperFileName)));
        if (message.Operation == ProtectedUpdateOperation.OpenInstalled)
        {
            if (message.TransactionId != Guid.Empty || UpdateTransaction.ReadActiveId(root) is not null)
                throw new InvalidOperationException(UpdaterText.ProtectedUpdateWorkerFinishRecoveryBeforeDownloadingOptionalSprites);

            var receiptBytes = IronmonOnlyUpdate.ReceiptBytes(authorization, inventories.Target);
            if (!TransactionStorage.ReadBytes(PlainPaths.Child(root, IronmonOnlyUpdate.ReceiptPath)).AsSpan().SequenceEqual(receiptBytes))
                throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheInstalledReleaseChangedBeforeAdministratorAccessWasGranted);

            foreach (var file in inventories.Target.Files)
            {
                if (await TransactionStorage.ContentAsync(PlainPaths.Child(root, file.Path), cancellationToken).ConfigureAwait(false) != ReleaseProtocol.Content(file.Bytes, file.Sha256))
                    throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerRepairTheInstalledIronmonPackageBeforeDownloadingSpritesWith);
            }

            await SignedIronmonAuthority.VerifyGameAsync(root, inventories.Game, cancellationToken).ConfigureAwait(false);
            ProtectState(root);
            _optionalOnly = true;
            _committed = true;
            Prepared = new PreparedIronmonUpdate(root, Guid.NewGuid(), helper.Version, helper.Content, manifest.IronmonVersion);
        }
        else if (message.Operation == ProtectedUpdateOperation.OpenRecovery)
        {
            if (message.TransactionId == Guid.Empty)
                throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheRecoveryTransactionIsMissing);

            var persisted = TransactionStorage.ReadBytes(IronmonOnlyUpdate.AuthorizationPath(root, message.TransactionId));
            if (!persisted.AsSpan().SequenceEqual(ReleaseJson.Serialize(authorization)))
                throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheRecoveryAuthorizationChangedWhileWindowsRequestedPermission);

            await Engine(root).ValidateRecoveryAsync(root, message.TransactionId, cancellationToken).ConfigureAwait(false);
            ProtectState(root);
            _recover = true;
            Prepared = new PreparedIronmonUpdate(root, message.TransactionId, helper.Version, helper.Content, manifest.IronmonVersion);
        }
        else
        {
            if (message.TransactionId != Guid.Empty)
                throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerANewPreparationCannotSelectAnExistingTransaction);

            ProtectState(root);
            Prepared = combined
                ? await new CombinedUpdate(_verifier, _downloads, _runtime, _gamePreparation, _gameVerification).PrepareInstallationAsync(authorization.Request, authorization.PreviousGameCommit, authorization.Target, authorization.Current, authorization.Purpose, cancellationToken).ConfigureAwait(false)
                : await new IronmonOnlyUpdate(_verifier, _downloads, _runtime).PrepareInstallationAsync(authorization.Request, authorization.Target, authorization.Current, authorization.Purpose, cancellationToken).ConfigureAwait(false);
        }

        return new ProtectedUpdateReply(Prepared);
    }

    /// <summary>
    /// Executes only a closed operation on the previously bound transaction.
    /// </summary>
    /// <param name="message">The selected operation without replacement authority.</param>
    /// <param name="cancellationToken">The safe cancellation token.</param>
    /// <returns>The durable result.</returns>
    internal async Task<ProtectedUpdateReply> ExecuteAsync(ProtectedUpdateMessage message, CancellationToken cancellationToken)
    {
        var prepared = Prepared ?? throw new InvalidOperationException(UpdaterText.ProtectedUpdateWorkerTheAdministratorInstallationHasNotBeenPrepared);
        ValidateBound(message, prepared);
        if (_optionalOnly && message.Operation is not (ProtectedUpdateOperation.Sprites or ProtectedUpdateOperation.Close))
            throw new InvalidOperationException(UpdaterText.ProtectedUpdateWorkerThisAdministratorSessionIsRestrictedToOptionalSpriteWork);

        var engine = Engine(prepared.InstallationRoot);
        if (message.Operation == ProtectedUpdateOperation.Validate)
        {
            if (_recover)
            {
                await engine.ValidateRecoveryAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await engine.ValidatePreparedAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (message.Operation == ProtectedUpdateOperation.Apply)
        {
            if (_recover)
                throw new InvalidOperationException(UpdaterText.ProtectedUpdateWorkerARecoverySessionCannotApplyANewInstallation);

            var result = await engine.ApplyAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken: cancellationToken).ConfigureAwait(false);
            _committed = result.Phase == TransactionPhase.Committed;
            _recover = result.Phase == TransactionPhase.RecoveryRequired;
            return new ProtectedUpdateReply(Result: result);
        }
        else if (message.Operation == ProtectedUpdateOperation.Recover)
        {
            var result = await engine.RecoverAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken).ConfigureAwait(false);
            _recover = result.Phase == TransactionPhase.RecoveryRequired;
            return new ProtectedUpdateReply(Result: result);
        }
        else if (message.Operation == ProtectedUpdateOperation.Discard)
        {
            await engine.DiscardPreparedAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Operation == ProtectedUpdateOperation.PreserveNavigation)
        {
            TrackerRelaunch.PreserveNavigation(prepared.InstallationRoot, prepared.TransactionId, message.Navigation ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheTrackerNavigationContextIsMissing));
        }
        else if (message.Operation == ProtectedUpdateOperation.Sprites)
        {
            if (!_committed)
                throw new InvalidOperationException(UpdaterText.ProtectedUpdateWorkerOptionalSpriteWorkRequiresACommittedCoreInstallation);

            return new ProtectedUpdateReply(Sprites: await _sprites(prepared.InstallationRoot, message.IncludeUnavailable, cancellationToken).ConfigureAwait(false));
        }
        else if (message.Operation != ProtectedUpdateOperation.Close)
        {
            throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerThisAdministratorOperationIsNotSupported);
        }

        return new ProtectedUpdateReply();
    }

    /// <summary>
    /// Rejects attempts to change the root, transaction, release or operation-specific input after initialization.
    /// </summary>
    /// <param name="message">The later operation.</param>
    /// <param name="prepared">The original preparation.</param>
    internal static void ValidateBound(ProtectedUpdateMessage message, PreparedIronmonUpdate prepared)
    {
        if (message.Authorization is not null || message.Ancestor is not null || message.AncestorIdentity is not null || message.Root != prepared.InstallationRoot || message.TransactionId != prepared.TransactionId || !Enum.IsDefined(message.Operation) || (message.IncludeUnavailable && message.Operation != ProtectedUpdateOperation.Sprites) || (message.Navigation is not null && (message.Operation != ProtectedUpdateOperation.PreserveNavigation || message.Navigation.Length > 4096)))
            throw new InvalidDataException(UpdaterText.ProtectedUpdateWorkerTheAdministratorRequestDiffersFromTheReviewedTransaction);
    }

    /// <summary>
    /// Discards an untouched abandoned preparation while retaining any interrupted application for recovery.
    /// </summary>
    /// <returns>The bounded cleanup attempt.</returns>
    internal async Task AbandonAsync()
    {
        if (Prepared is not { } prepared || _committed || _recover)
            return;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Engine(prepared.InstallationRoot).DiscardPreparedAsync(prepared.InstallationRoot, prepared.TransactionId, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            _recover = true;
        }
    }

    /// <summary>
    /// Constructs the independently authenticated engine with a fresh installation-idle check.
    /// </summary>
    /// <param name="root">The bound installation root.</param>
    /// <returns>The shared transaction engine.</returns>
    private UpdateTransaction Engine(string root)
        => new(new SignedIronmonAuthority(_verifier, _runtime, _gameVerification), token => _ensureIdle(root, token));

    /// <summary>
    /// Protects durable state and prevents its directory from being replaced through a writable installation parent.
    /// </summary>
    /// <param name="root">The independently authenticated installation.</param>
    private void ProtectState(string root)
    {
        _protectState(root);
        var state = PlainPaths.Child(root, InstallationLease.StateDirectory);
        _handles.AddRange(WindowsUpdateAccess.LockAncestors(Path.Combine(state, UpdaterHandoff.HelperFileName)));
    }

    /// <summary>
    /// Releases directory identity locks after the administrator session ends.
    /// </summary>
    public void Dispose()
    {
        foreach (var handle in _handles)
            handle.Dispose();
    }
}
