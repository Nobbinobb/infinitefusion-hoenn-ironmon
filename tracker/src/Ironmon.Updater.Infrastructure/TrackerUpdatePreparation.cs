using System.Text;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Connects tracker review and recovery to authenticated inventories and the shared transaction engine.
/// </summary>
/// <remarks>
/// Constructs a metadata-first workflow; reviewing never downloads program archives or replaces installed files.
/// </remarks>
/// <param name="verifier">The independently configured release trust.</param>
/// <param name="downloads">The bounded asset cache outside the game.</param>
/// <param name="runtime">The tracker prerequisite checker.</param>
/// <param name="gamePreparation">The isolated game preparer.</param>
/// <param name="gameVerification">The independent game verifier.</param>
/// <param name="protectedUpdates">The optional Windows administrator boundary configured by the application.</param>
public sealed class TrackerUpdatePreparation(ReleaseVerifier verifier, ReleaseDownloadStore downloads, ITrackerRuntimeCompatibility runtime, CombinedGamePreparation gamePreparation, CombinedGitVerification gameVerification, ProtectedUpdateClient? protectedUpdates = null)
{
    private const int NotesLimit = 256 * 1024;
    private const string GitDirectory = ".git";
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ITrackerRuntimeCompatibility _runtime = runtime;
    private readonly CombinedGamePreparation _gamePreparation = gamePreparation;
    private readonly CombinedGitVerification _gameVerification = gameVerification;
    private readonly ProtectedUpdateClient? _protectedUpdates = protectedUpdates;

    /// <summary>
    /// Resolves signed ownership and exact conflicts before any large package download.
    /// </summary>
    /// <param name="request">The current installation and live run observation.</param>
    /// <param name="release">The authenticated discovery result.</param>
    /// <param name="currentRelease">The prior signed release, or null for signed legacy adoption.</param>
    /// <param name="cancellationToken">The review cancellation token.</param>
    /// <returns>The immutable review with exact local conflict bytes.</returns>
    public async Task<TrackerUpdateReview> ReviewAsync(IronmonUpdateRequest request, ReleaseEvidence release, ReleaseEvidence? currentRelease = null, CancellationToken cancellationToken = default)
    {
        var manifest = _verifier.Verify(release.Manifest, release.Signatures);
        var combined = !manifest.Game.SupportedCommits.Contains(request.GameCommit, StringComparer.Ordinal);
        var selection = request with { GameCommit = combined ? manifest.Game.PreferredCommit : request.GameCommit, ApprovedPaths = [], ApprovedFiles = null };
        IronmonOnlyUpdate.ValidateSelection(selection, manifest);
        if (combined && request.HasActiveRun)
            throw new InvalidOperationException(UpdaterText.TrackerUpdatePreparationFinishTheActiveRunBeforeUpdatingTheGameConnect);

        var content = new IronmonOnlyUpdate(_verifier, _downloads, _runtime);
        var target = await content.CompleteEvidenceAsync(release, manifest, cancellationToken).ConfigureAwait(false);
        var current = currentRelease is null ? null : await content.CompleteEvidenceAsync(currentRelease, _verifier.Verify(currentRelease.Manifest, currentRelease.Signatures), cancellationToken).ConfigureAwait(false);
        var authorization = new IronmonUpdateAuthorization(1, selection, target, current, combined ? request.GameCommit : null);
        var authority = Authority();
        var inventories = authority.Resolve(authorization);
        var local = await InstallationFileSnapshot.ReadAsync(request.InstallationRoot, cancellationToken).ConfigureAwait(false);
        IEnumerable<ManagedFile> baseline = IronmonOnlyUpdate.BaselineFiles(inventories.Baseline, local);
        IEnumerable<ManagedFile> desired = IronmonOnlyUpdate.TargetFiles(inventories.Target, IronmonOnlyUpdate.ReceiptBytes(authorization, inventories.Target));
        if (combined)
        {
            baseline = baseline.Concat(SignedIronmonAuthority.GameFiles(authority.ResolvePreviousGame(authorization)));
            desired = desired.Concat(SignedIronmonAuthority.GameFiles(inventories.Game));
        }
        else
        {
            await SignedIronmonAuthority.VerifyGameAsync(request.InstallationRoot, inventories.Game, cancellationToken).ConfigureAwait(false);
        }

        var plan = new FileUpdatePlanner(new FileManagementPolicy()).Create(baseline, local, desired);
        IronmonOnlyUpdate.RequireCompleteTarget(plan);
        var notesAsset = manifest.Assets.Single(asset => asset.Name == manifest.ReleaseNotesAsset);
        if (notesAsset.Bytes > NotesLimit)
            throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheReleaseNotesExceedTheSupportedSize);

        var notesPath = await _downloads.GetAsync(notesAsset, cancellationToken).ConfigureAwait(false);
        var notes = await File.ReadAllTextAsync(notesPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var bytes = manifest.Assets.Where(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + request.Flavor || asset.Role == ReleaseProtocol.UpdaterRole).Sum(asset => asset.Bytes);
        return new TrackerUpdateReview(authorization, manifest, plan, notes, bytes, combined && !Directory.Exists(Path.Combine(request.InstallationRoot, GitDirectory)));
    }

    /// <summary>
    /// Captures exact consent and prepares the selected complete transaction through the existing engine.
    /// </summary>
    /// <param name="review">The previously displayed immutable review.</param>
    /// <param name="approvedPaths">Only the replacement conflicts selected by the player.</param>
    /// <param name="hasActiveRun">A fresh conservative run observation immediately before preparation.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>The backed-up transaction ready for the independent helper.</returns>
    public Task<PreparedIronmonUpdate> PrepareAsync(TrackerUpdateReview review, string[] approvedPaths, bool hasActiveRun, CancellationToken cancellationToken = default)
    {
        var approved = approvedPaths.Length == 0 ? review.Plan : review.Plan.ApproveReplacements(approvedPaths);
        if (!approved.CanApply)
            throw new IronmonUpdateConflictException(approved);

        ReviewedFileConsent[] files = [.. approvedPaths.Select(path => new ReviewedFileConsent(path, review.Plan.Entries.Single(entry => entry.Path == path).Local))];
        var request = review.Authorization.Request with { ApprovedPaths = [.. approvedPaths], ApprovedFiles = files, HasActiveRun = hasActiveRun };
        if (_protectedUpdates is not null && ProtectedUpdateClient.RequiresElevation(request.InstallationRoot))
            return _protectedUpdates.PrepareAsync(review.Authorization with { Request = request }, cancellationToken);

        if (review.Authorization.PreviousGameCommit is { } previous)
            return new CombinedUpdate(_verifier, _downloads, _runtime, _gamePreparation, _gameVerification).PrepareAsync(request, previous, review.Authorization.Target, review.Authorization.Current, cancellationToken);

        return new IronmonOnlyUpdate(_verifier, _downloads, _runtime).PrepareAsync(request, review.Authorization.Target, review.Authorization.Current, cancellationToken);
    }

    /// <summary>
    /// Finds a prior signed release from fixed authorization records without trusting the installed receipt as ownership.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="version">The running tracker version.</param>
    /// <returns>The matching authenticated evidence, or null for first adoption.</returns>
    public ReleaseEvidence? ReadCurrentRelease(string root, string version)
    {
        var receiptPath = PlainPaths.Child(root, IronmonOnlyUpdate.ReceiptPath);
        if (!File.Exists(receiptPath))
            return null;

        var receipt = ReleaseJson.Parse<IronmonInstalledReceipt>(TransactionStorage.ReadBytes(receiptPath), ReleaseJson.ManifestLimit);
        if (receipt.SchemaVersion != 1 || receipt.Version != version)
            throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheInstalledReleaseRecordDoesNotMatchThisTracker);

        var directory = PlainPaths.Child(root, IronmonOnlyUpdate.EvidenceDirectory);
        if (!Directory.Exists(directory))
            throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordsAreMissingUse);

        foreach (var path in Directory.EnumerateFiles(directory).Take(1024))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), TransactionStorage.GuidFormat, out var id))
                continue;

            var evidence = ReleaseJson.Parse<IronmonUpdateAuthorization>(TransactionStorage.ReadBytes(IronmonOnlyUpdate.AuthorizationPath(root, id)), 64 * ReleaseJson.ManifestLimit).Target;
            if (TransactionStorage.Hash(evidence.Manifest) != receipt.ManifestSha256)
                continue;

            var manifest = _verifier.Verify(evidence.Manifest, evidence.Signatures);
            if (manifest.IronmonVersion != version)
                throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordNamesAnotherVersion);

            return evidence;
        }

        throw new InvalidDataException(UpdaterText.TrackerUpdatePreparationTheInstalledReleaseSVerificationRecordCouldNotBe);
    }

    /// <summary>
    /// Reauthenticates an interrupted transaction and its helper before offering recovery in the tracker.
    /// </summary>
    /// <param name="root">The installed game root.</param>
    /// <param name="cancellationToken">The recovery inspection token.</param>
    /// <returns>The verified recovery handoff, or null when no transaction is active.</returns>
    public async Task<PreparedIronmonUpdate?> ReadRecoveryAsync(string root, CancellationToken cancellationToken = default)
    {
        var id = UpdateTransaction.ReadActiveId(root);
        if (id is null)
            return null;

        var engine = new UpdateTransaction(Authority(), token => UpdateProcessIdentity.EnsureInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, token));
        if (_protectedUpdates is null || !ProtectedUpdateClient.RequiresElevation(root))
            await engine.ValidateRecoveryAsync(root, id.Value, cancellationToken).ConfigureAwait(false);
        var authorization = ReleaseJson.Parse<IronmonUpdateAuthorization>(TransactionStorage.ReadBytes(IronmonOnlyUpdate.AuthorizationPath(root, id.Value)), 64 * ReleaseJson.ManifestLimit);
        var manifest = _verifier.Verify(authorization.Target.Manifest, authorization.Target.Signatures);
        var helper = await RecoveryHelperPackage.StageAsync(root, manifest, _downloads, authorization.Request.Flavor, cancellationToken).ConfigureAwait(false);
        return new PreparedIronmonUpdate(root, id.Value, helper.Version, helper.Content, manifest.IronmonVersion);
    }

    /// <summary>
    /// Releases a preparation cancelled before handoff without touching any installed program file.
    /// </summary>
    /// <param name="prepared">The exact preparation owned by this workflow.</param>
    /// <param name="cancellationToken">The validation cancellation token.</param>
    /// <returns>A task that refuses to discard a transaction whose application has begun.</returns>
    public async Task DiscardAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
    {
        if (prepared.Elevation is not null)
        {
            try
            {
                await ProtectedUpdateClient.DiscardAsync(prepared, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await ProtectedUpdateClient.EndSessionAsync(prepared).ConfigureAwait(false);
            }
        }
        else
        {
            await new UpdateTransaction(Authority(), _ => Task.CompletedTask).DiscardPreparedAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Requests administrator recovery only after the player selects the recovery action.
    /// </summary>
    /// <param name="prepared">The inspected recovery identity.</param>
    /// <param name="cancellationToken">The user-selected recovery token.</param>
    /// <returns>The independently validated administrator or ordinary recovery.</returns>
    public Task<PreparedIronmonUpdate> PrepareRecoveryAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
        => _protectedUpdates is not null && ProtectedUpdateClient.RequiresElevation(prepared.InstallationRoot) ? _protectedUpdates.OpenRecoveryAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken) : Task.FromResult(prepared);

    /// <summary>
    /// Constructs independent signature and game verification for preparation and recovery.
    /// </summary>
    /// <returns>The shared release authority.</returns>
    private SignedIronmonAuthority Authority()
        => new(_verifier, _runtime, _gameVerification);
}

/// <summary>
/// Holds the signed release details and exact local files displayed before downloading program packages.
/// </summary>
/// <remarks>
/// Constructs review data that must be revalidated before preparation and again before application.
/// </remarks>
/// <param name="Authorization">The authenticated selected component combination.</param>
/// <param name="Manifest">The verified target release.</param>
/// <param name="Plan">The captured local file decisions.</param>
/// <param name="Notes">Bounded authenticated release notes rendered as plain text.</param>
/// <param name="PackageBytes">The known tracker and helper download sizes, excluding unknown game traffic.</param>
/// <param name="FirstPreparation">Whether a combined update must prepare an ordinary repository for a ZIP installation.</param>
public sealed record TrackerUpdateReview(IronmonUpdateAuthorization Authorization, ReleaseManifest Manifest, FileUpdatePlan Plan, string Notes, long PackageBytes, bool FirstPreparation);
