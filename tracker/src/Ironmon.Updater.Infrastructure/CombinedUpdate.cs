using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Prepares game, Ironmon and ordinary Git state as one recoverable update.
/// </summary>
/// <remarks>
/// Constructs the combined workflow with independently configured release and Git trust.
/// </remarks>
/// <param name="verifier">The signed release verifier.</param>
/// <param name="downloads">The verified release cache outside the game.</param>
/// <param name="runtime">The tracker runtime prerequisite checker.</param>
/// <param name="gamePreparation">The isolated exact-commit game preparer.</param>
/// <param name="gameVerification">The independent offline Git verifier.</param>
public sealed class CombinedUpdate(ReleaseVerifier verifier, ReleaseDownloadStore downloads, ITrackerRuntimeCompatibility runtime, CombinedGamePreparation gamePreparation, CombinedGitVerification gameVerification)
{
    private const string ExtractionPrefix = "combined-";
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ITrackerRuntimeCompatibility _runtime = runtime;
    private readonly CombinedGamePreparation _gamePreparation = gamePreparation;
    private readonly CombinedGitVerification _gameVerification = gameVerification;

    /// <summary>
    /// Prepares one complete forward game/mod update without closing the tracker or replacing installed program files.
    /// </summary>
    /// <param name="request">The approved target game and package selection.</param>
    /// <param name="previousGameCommit">The authenticated historical game commit expected in the installation.</param>
    /// <param name="targetRelease">The signed target release.</param>
    /// <param name="currentRelease">The current signed release, or null for explicit legacy adoption.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The durable transaction and verified independent helper identity.</returns>
    public Task<PreparedIronmonUpdate> PrepareAsync(IronmonUpdateRequest request, string previousGameCommit, ReleaseEvidence targetRelease, ReleaseEvidence? currentRelease = null, CancellationToken cancellationToken = default)
        => PrepareInstallationAsync(request, previousGameCommit, targetRelease, currentRelease, InstallationPurpose.Update, cancellationToken);

    /// <summary>
    /// Prepares Setup through the same signed file, backup and recovery pipeline as tracker updates.
    /// </summary>
    /// <param name="request">The exact installation selection.</param>
    /// <param name="previousGameCommit">The prior game, or null for an empty installation.</param>
    /// <param name="targetRelease">The signed target release.</param>
    /// <param name="currentRelease">The signed prior release when available.</param>
    /// <param name="purpose">The explicit independently checked installation purpose.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The durable prepared transaction.</returns>
    public async Task<PreparedIronmonUpdate> PrepareInstallationAsync(IronmonUpdateRequest request, string? previousGameCommit, ReleaseEvidence targetRelease, ReleaseEvidence? currentRelease, InstallationPurpose purpose, CancellationToken cancellationToken = default)
    {
        var root = PlainPaths.Full(request.InstallationRoot);
        if (_downloads.Root.Equals(root, StringComparison.OrdinalIgnoreCase) || _downloads.Root.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.StartsWith(_downloads.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(UpdaterText.CombinedUpdateCombinedUpdateDownloadsMustRemainOutsideTheInstalledGame);

        var manifest = _verifier.Verify(targetRelease.Manifest, targetRelease.Signatures);
        IronmonOnlyUpdate.ValidateSelection(request, manifest, purpose);
        if (request.HasActiveRun)
            throw new InvalidOperationException(UpdaterText.CombinedUpdateFinishTheActiveRunBeforeUpdatingTheGameRetained);

        var content = new IronmonOnlyUpdate(_verifier, _downloads, _runtime);
        var target = await content.CompleteEvidenceAsync(targetRelease, manifest, cancellationToken).ConfigureAwait(false);
        var current = currentRelease is null ? null : await content.CompleteEvidenceAsync(currentRelease, _verifier.Verify(currentRelease.Manifest, currentRelease.Signatures), cancellationToken).ConfigureAwait(false);
        var authorization = new IronmonUpdateAuthorization(1, request with { InstallationRoot = root, ApprovedPaths = [.. request.ApprovedPaths] }, target, current, previousGameCommit, purpose);
        var evidence = ReleaseJson.Serialize(authorization);
        if (evidence.Length > 64 * ReleaseJson.ManifestLimit)
            throw new InvalidDataException(UpdaterText.CombinedUpdateTheCombinedRecoveryEvidenceExceedsItsSupportedSize);

        var authority = new SignedIronmonAuthority(_verifier, _runtime, _gameVerification);
        var inventories = authority.Resolve(authorization);
        var previousGame = purpose == InstallationPurpose.InstallGame ? null : authority.ResolvePreviousGame(authorization);
        InstallationProgressScope.Report(new(InstallationStage.PreparingDownload));
        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + request.Flavor);
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var packageProgress = new PackageProgress(InstallationProgressScope.CaptureReporter());
        var packageTask = DownloadPackageAsync(asset, packageProgress, preparation);
        PreparedCombinedGame game;
        try
        {
            game = await _gamePreparation.PrepareAsync(root, previousGame, inventories.Game, preparation.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await preparation.CancelAsync().ConfigureAwait(false);
            try
            {
                await packageTask.ConfigureAwait(false);
            }
            catch when (error is not OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                // Observe the companion operation before its cancellation source and cache owner are disposed.
            }

            throw;
        }

        using var preparedGame = game;
        if (!packageTask.IsCompleted)
            packageProgress.Show();

        var archive = await packageTask.ConfigureAwait(false);
        var extracted = PlainPaths.Child(_downloads.Root, ExtractionPrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        try
        {
            await ReleaseArchive.ExtractAsync(archive, ReleaseProtocol.Content(asset.Bytes, asset.Sha256), extracted, inventories.Target.Files, cancellationToken).ConfigureAwait(false);
            await _runtime.EnsureAsync(extracted, request.Flavor, cancellationToken).ConfigureAwait(false);
            foreach (var file in inventories.Target.Files.Where(file => ReleaseVerifier.IsLegacyDocument(file.Path)))
                File.Delete(PlainPaths.Child(extracted, file.Path));

            foreach (var file in SignedIronmonAuthority.GameFiles(inventories.Game))
            {
                var path = PlainPaths.Child(extracted, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(PlainPaths.Child(game.Payload, file.Path), path);
            }

            InstallationProgressScope.Report(new(InstallationStage.PreparingRecovery));
            var helper = await RecoveryHelperPackage.StageAsync(root, manifest, _downloads, request.Flavor, cancellationToken).ConfigureAwait(false);
            var local = await InstallationFileSnapshot.ReadAsync(root, cancellationToken).ConfigureAwait(false);
            var receipt = IronmonOnlyUpdate.ReceiptBytes(authorization, inventories.Target);
            var baseline = IronmonOnlyUpdate.BaselineFiles(inventories.Baseline, local).Concat(previousGame is null ? [] : SignedIronmonAuthority.GameFiles(previousGame));
            var desired = IronmonOnlyUpdate.TargetFiles(inventories.Target, receipt).Concat(SignedIronmonAuthority.GameFiles(inventories.Game));
            var plan = new FileUpdatePlanner(new FileManagementPolicy()).Create(baseline, local, desired);
            IronmonOnlyUpdate.ValidateConsent(request, local);
            if (request.ApprovedPaths.Length > 0)
                plan = plan.ApproveReplacements(request.ApprovedPaths);

            if (!plan.CanApply)
                throw new IronmonUpdateConflictException(plan);

            IronmonOnlyUpdate.RequireCompleteTarget(plan);
            TransactionStorage.WriteDurable(PlainPaths.Child(extracted, IronmonOnlyUpdate.ReceiptPath), receipt);
            if (!await CombinedGamePreparation.SameGitAsync(root, game.OriginalGit, cancellationToken).ConfigureAwait(false))
                throw new IOException(UpdaterText.CombinedUpdateAnotherLauncherChangedTheGameRepositoryDuringPreparation);

            var description = await UpdateTransaction.DescribeAsync(root, plan, game.Metadata, cancellationToken).ConfigureAwait(false);
            var evidencePath = IronmonOnlyUpdate.AuthorizationPath(root, description.TransactionId);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            TransactionStorage.WriteDurable(evidencePath, evidence);
            await CombinedGitVerification.PreserveAsync(description, game.Metadata, cancellationToken).ConfigureAwait(false);
            var engine = new UpdateTransaction(authority, token => UpdateProcessIdentity.EnsureInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, token));
            await engine.PrepareOwnedAsync(description, extracted, game.Metadata, cancellationToken).ConfigureAwait(false);
            return new PreparedIronmonUpdate(root, description.TransactionId, helper.Version, helper.Content, manifest.IronmonVersion);
        }
        finally
        {
            if (Directory.Exists(extracted))
                PlainPaths.DeleteOwned(_downloads.Root, extracted);
        }
    }

    /// <summary>
    /// Downloads the independent tracker package while the game is prepared, cancelling its companion on failure.
    /// </summary>
    /// <param name="asset">The authenticated tracker package.</param>
    /// <param name="progress">The companion progress shown if the package outlasts game preparation.</param>
    /// <param name="preparation">The lifetime shared with game preparation.</param>
    /// <returns>The verified cache file.</returns>
    private async Task<string> DownloadPackageAsync(ReleaseAsset asset, PackageProgress progress, CancellationTokenSource preparation)
    {
        using var packageScope = new InstallationProgressScope(progress.Report);
        try
        {
            return await _downloads.GetAsync(asset, preparation.Token).ConfigureAwait(false);
        }
        catch
        {
            await preparation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Keeps game preparation visible until the independent package download becomes the remaining work.
    /// </summary>
    /// <remarks>
    /// Binds notifications to the original installation scope across both asynchronous operations.
    /// </remarks>
    /// <param name="observer">The originating installation reporter.</param>
    private sealed class PackageProgress(Action<InstallationProgress> observer)
    {
        private readonly Lock _gate = new();
        private InstallationProgress _latest = new(InstallationStage.DownloadingPackage);
        private bool _visible;

        /// <summary>
        /// Retains the latest measurement and forwards it once the game work has finished.
        /// </summary>
        /// <param name="value">The package download measurement.</param>
        internal void Report(InstallationProgress value)
        {
            lock (_gate)
            {
                _latest = value;
                if (_visible)
                    observer(value);
            }
        }

        /// <summary>
        /// Shows the pending package download and enables subsequent byte measurements.
        /// </summary>
        internal void Show()
        {
            lock (_gate)
            {
                _visible = true;
                observer(_latest);
            }
        }
    }
}
