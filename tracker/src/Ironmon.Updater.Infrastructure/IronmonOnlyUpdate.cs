using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Prepares an authenticated Ironmon-only transaction without fetching or modifying game Git state.
/// </summary>
/// <remarks>
/// Constructs the download, verification and transaction preparation workflow used by later tracker UI integration.
/// </remarks>
/// <param name="verifier">The independently configured release trust.</param>
/// <param name="downloads">The bounded verified asset cache.</param>
/// <param name="runtime">The selected tracker runtime prerequisite checker.</param>
public sealed class IronmonOnlyUpdate(ReleaseVerifier verifier, ReleaseDownloadStore downloads, ITrackerRuntimeCompatibility runtime)
{
    internal const string ScriptRoot = "Data/Scripts/997_Ironmon/";
    internal const string DataRoot = "Data/Ironmon/";
    internal const string ReceiptPath = "Data/Ironmon/installed-release.json";
    internal const string EvidenceDirectory = ".ironmon-update/authorizations";
    private const string ExtractionPrefix = "extract-";
    private const string EvidenceSuffix = ".json";
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ITrackerRuntimeCompatibility _runtime = runtime;

    /// <summary>
    /// Downloads, authenticates, plans and backs up a complete tracker/scripts/data update before any program replacement.
    /// </summary>
    /// <param name="request">The selected version, flavor, supported game and exact conflict approvals.</param>
    /// <param name="targetRelease">The signed target release from discovery.</param>
    /// <param name="currentRelease">The previous signed release, or null when using the target's explicit legacy inventory.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>The durable prepared transaction and verified independent helper identity.</returns>
    public Task<PreparedIronmonUpdate> PrepareAsync(IronmonUpdateRequest request, ReleaseEvidence targetRelease, ReleaseEvidence? currentRelease = null, CancellationToken cancellationToken = default)
        => PrepareInstallationAsync(request, targetRelease, currentRelease, InstallationPurpose.Update, cancellationToken);

    /// <summary>
    /// Prepares Setup through the same signed file, backup and recovery pipeline as tracker updates.
    /// </summary>
    /// <param name="request">The exact installation selection.</param>
    /// <param name="targetRelease">The signed target release.</param>
    /// <param name="currentRelease">The signed prior release when available.</param>
    /// <param name="purpose">The explicit independently checked installation purpose.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The durable prepared transaction.</returns>
    public async Task<PreparedIronmonUpdate> PrepareInstallationAsync(IronmonUpdateRequest request, ReleaseEvidence targetRelease, ReleaseEvidence? currentRelease, InstallationPurpose purpose, CancellationToken cancellationToken = default)
    {
        var root = PlainPaths.Full(request.InstallationRoot);
        if (_downloads.Root.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.StartsWith(_downloads.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.Equals(_downloads.Root, StringComparison.OrdinalIgnoreCase))
            throw new IOException(UpdaterText.IronmonOnlyUpdateUpdateDownloadsMustRemainOutsideTheInstalledGameAnd);

        var target = _verifier.Verify(targetRelease.Manifest, targetRelease.Signatures);
        ValidateSelection(request, target, purpose);
        var current = currentRelease is null ? null : _verifier.Verify(currentRelease.Manifest, currentRelease.Signatures);
        if (current is not null && (current.IronmonVersion != request.CurrentVersion || current.ReleaseSequence >= target.ReleaseSequence))
            throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateTheCurrentSignedReleaseDoesNotMatchTheSelected);

        var targetEvidence = await CompleteEvidenceAsync(targetRelease, target, cancellationToken).ConfigureAwait(false);
        var currentEvidence = currentRelease is null ? null : await CompleteEvidenceAsync(currentRelease, current!, cancellationToken).ConfigureAwait(false);
        var authorization = new IronmonUpdateAuthorization(1, request with { InstallationRoot = root, ApprovedPaths = [.. request.ApprovedPaths] }, targetEvidence, currentEvidence, Purpose: purpose);
        var authorizationBytes = ReleaseJson.Serialize(authorization);
        if (authorizationBytes.Length > 64 * ReleaseJson.ManifestLimit)
            throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateTheCompleteSignedRecoveryEvidenceExceedsTheSupportedMetadata);

        var authority = new SignedIronmonAuthority(_verifier, _runtime);
        var (Baseline, Target, Game) = authority.Resolve(authorization);
        await SignedIronmonAuthority.VerifyGameAsync(root, Game, cancellationToken).ConfigureAwait(false);
        var archiveAsset = target.Assets.Single(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + request.Flavor);
        var archive = await _downloads.GetAsync(archiveAsset, cancellationToken).ConfigureAwait(false);
        var extracted = PlainPaths.Child(_downloads.Root, ExtractionPrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        try
        {
            await ReleaseArchive.ExtractAsync(archive, ReleaseProtocol.Content(archiveAsset.Bytes, archiveAsset.Sha256), extracted, Target.Files, cancellationToken).ConfigureAwait(false);
            await _runtime.EnsureAsync(extracted, request.Flavor, cancellationToken).ConfigureAwait(false);
            var helper = await RecoveryHelperPackage.StageAsync(root, target, _downloads, request.Flavor, cancellationToken).ConfigureAwait(false);
            var local = await InstallationFileSnapshot.ReadAsync(root, cancellationToken).ConfigureAwait(false);
            var receipt = ReceiptBytes(authorization, Target);
            var baseline = BaselineFiles(Baseline, local);
            var targetFiles = TargetFiles(Target, receipt);
            var plan = new FileUpdatePlanner(new FileManagementPolicy()).Create(baseline, local, targetFiles);
            ValidateConsent(request, local);
            if (request.ApprovedPaths.Length > 0)
                plan = plan.ApproveReplacements(request.ApprovedPaths);

            if (!plan.CanApply)
                throw new IronmonUpdateConflictException(plan);

            RequireCompleteTarget(plan);
            TransactionStorage.WriteDurable(PlainPaths.Child(extracted, ReceiptPath), receipt);
            var description = await UpdateTransaction.DescribeAsync(root, plan, cancellationToken: cancellationToken).ConfigureAwait(false);
            var evidencePath = AuthorizationPath(root, description.TransactionId);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            TransactionStorage.WriteDurable(evidencePath, authorizationBytes);
            var engine = new UpdateTransaction(authority, token => UpdateProcessIdentity.EnsureInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, token));
            await engine.PrepareAsync(description, extracted, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new PreparedIronmonUpdate(root, description.TransactionId, helper.Version, helper.Content, target.IronmonVersion);
        }
        finally
        {
            if (Directory.Exists(extracted))
                PlainPaths.DeleteOwned(_downloads.Root, extracted);
        }
    }

    /// <summary>
    /// Loads hash-bound inventories into portable evidence that a fresh recovery process can reauthenticate offline.
    /// </summary>
    /// <param name="evidence">The exact signed release.</param>
    /// <param name="manifest">The verified manifest.</param>
    /// <param name="cancellationToken">The download token.</param>
    /// <returns>The release plus its verified inventory documents.</returns>
    internal async Task<ReleaseEvidence> CompleteEvidenceAsync(ReleaseEvidence evidence, ReleaseManifest manifest, CancellationToken cancellationToken)
    {
        var inventories = new List<InventoryEvidence>();
        long total = 0;
        foreach (var asset in manifest.Assets.Where(asset => asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.GameInventoryRole or ReleaseProtocol.LegacyRole))
        {
            total = checked(total + asset.Bytes);
            if (asset.Bytes > ReleaseJson.InventoryLimit || total > ReleaseJson.InventoryLimit)
                throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateTheSignedInventoryExceedsTheSupportedMetadataBudget);

            var path = await _downloads.GetAsync(asset, cancellationToken).ConfigureAwait(false);
            var item = new InventoryEvidence(asset.Name, TransactionStorage.ReadBytes(path));
            _verifier.VerifyInventory(manifest, item);
            inventories.Add(item);
        }

        return new ReleaseEvidence((byte[])evidence.Manifest.Clone(), (byte[])evidence.Signatures.Clone(), [.. inventories]);
    }

    /// <summary>
    /// Rejects downgrades, unsupported engines and unsafe active-run combinations before downloading program assets.
    /// </summary>
    /// <param name="request">The selected installation.</param>
    /// <param name="target">The authenticated target release.</param>
    /// <param name="purpose">The bounded update or Setup operation.</param>
    internal static void ValidateSelection(IronmonUpdateRequest request, ReleaseManifest target, InstallationPurpose purpose = InstallationPurpose.Update)
    {
        if (request.Flavor is not (ReleaseProtocol.SelfContained or ReleaseProtocol.RuntimeRequired) || request.CurrentFlavor is not (ReleaseProtocol.SelfContained or ReleaseProtocol.RuntimeRequired) || (purpose == InstallationPurpose.Repair ? request.CurrentVersion != target.IronmonVersion : ReleaseProtocol.ParseVersion(request.CurrentVersion) >= ReleaseProtocol.ParseVersion(target.IronmonVersion)))
            throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateTheSelectedUpdateIsNotASupportedForwardVersion);

        if (!Enum.IsDefined(purpose) || (purpose is InstallationPurpose.AddIronmon or InstallationPurpose.InstallGame && (request.CurrentVersion != SetupProtocol.UninstalledVersion || request.HasActiveRun)))
            throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateTheInstallationPurposeDoesNotMatchTheSelectedBaseline);

        if (ReleaseProtocol.ParseVersion(target.MinimumEngineVersion) > ReleaseProtocol.ParseVersion(ReleaseProtocol.EngineVersion))
            throw new InvalidOperationException(UpdaterText.IronmonOnlyUpdateThisReleaseRequiresANewerVerifiedUpdaterUseThe);

        if (!target.Game.SupportedCommits.Contains(request.GameCommit, StringComparer.Ordinal))
            throw new InvalidOperationException(UpdaterText.IronmonOnlyUpdateThisReleaseNeedsACombinedGameUpdateTheIronmon);

        if (request.HasActiveRun && target.Game.ActiveRunPolicy != ReleaseProtocol.PreserveRuns)
            throw new InvalidOperationException(UpdaterText.IronmonOnlyUpdateFinishTheActiveRunBeforeInstallingThisRelease);
    }

    /// <summary>
    /// Includes only signed prior ownership plus the updater's fixed local receipt path in the rollback baseline.
    /// </summary>
    /// <param name="inventory">The authenticated prior package inventory.</param>
    /// <param name="local">The original complete snapshot.</param>
    /// <returns>The exact prior managed files.</returns>
    internal static ManagedFile[] BaselineFiles(ReleaseFileInventory inventory, IEnumerable<LocalFileEntry> local)
    {
        var files = ReleaseVerifier.ManagedFiles(inventory).ToList();
        var receipt = local.SingleOrDefault(entry => entry.Path == ReceiptPath);
        if (receipt?.Content is not null)
            files.Add(new ManagedFile(ReceiptPath, receipt.Content, ManagedFileOwner.Ironmon));

        return [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Includes the deterministic signed-release receipt in the same transaction as tracker, scripts and data.
    /// </summary>
    /// <param name="inventory">The authenticated target inventory.</param>
    /// <param name="receipt">The deterministic local receipt bytes.</param>
    /// <returns>The exact desired managed files.</returns>
    internal static ManagedFile[] TargetFiles(ReleaseFileInventory inventory, byte[] receipt)
    {
        var files = ReleaseVerifier.ManagedFiles(inventory).ToList();
        files.Add(new ManagedFile(ReceiptPath, new GameFileContent(receipt.LongLength, TransactionStorage.Hash(receipt)), ManagedFileOwner.Ironmon));
        return [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Derives a reproducible installed record whose version and content references come from authenticated evidence.
    /// </summary>
    /// <param name="authorization">The reauthenticated transaction inputs.</param>
    /// <param name="inventory">The exact target package inventory.</param>
    /// <returns>The receipt installed and rolled back with program files.</returns>
    internal static byte[] ReceiptBytes(IronmonUpdateAuthorization authorization, ReleaseFileInventory inventory)
        => ReleaseJson.Serialize(new IronmonInstalledReceipt(1, inventory.Version, inventory.Flavor!, authorization.Request.GameCommit, TransactionStorage.Hash(authorization.Target.Manifest), TransactionStorage.Hash(ReleaseJson.Serialize(inventory))));

    /// <summary>
    /// Resolves recovery evidence by typed transaction identity, never by a path supplied in a journal.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="id">The transaction UUID.</param>
    /// <returns>The fixed evidence pathname.</returns>
    internal static string AuthorizationPath(string root, Guid id)
        => PlainPaths.Child(root, EvidenceDirectory + '/' + id.ToString(TransactionStorage.GuidFormat) + EvidenceSuffix);

    /// <summary>
    /// Rejects preserved local edits to required unchanged program files before preparing an inevitably incompatible update.
    /// </summary>
    /// <param name="plan">The resolved exact file plan.</param>
    internal static void RequireCompleteTarget(FileUpdatePlan plan)
    {
        foreach (var entry in plan.Entries)
        {
            if (entry.Action == FilePlanAction.Keep && entry.Target is { Owner: ManagedFileOwner.Ironmon, Policy: ManagedFilePolicy.Replace } target && entry.Local?.Content != target.Content)
                throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateTheRequiredProgramFileWasChangedOrRemovedLocally(entry.Path));
        }
    }

    /// <summary>
    /// Binds tracker consent to the exact bytes reviewed before downloads rather than a pathname alone.
    /// </summary>
    /// <param name="request">The captured replacement consent.</param>
    /// <param name="local">The fresh installation snapshot.</param>
    internal static void ValidateConsent(IronmonUpdateRequest request, IEnumerable<LocalFileEntry> local)
    {
        if (request.ApprovedFiles is not { } approved)
            return;

        if (!approved.Select(file => file.Path).Order(StringComparer.Ordinal).SequenceEqual(request.ApprovedPaths.Order(StringComparer.Ordinal)) || approved.Any(file => local.SingleOrDefault(entry => entry.Path == file.Path) != file.Local))
            throw new InvalidDataException(UpdaterText.IronmonOnlyUpdateAFileChangedAfterYouReviewedItsReplacementCheck);
    }
}

/// <summary>
/// Captures the selected forward update and exact reviewed conflict paths.
/// </summary>
/// <remarks>
/// Constructs a selection; local version hints do not authenticate ownership.
/// </remarks>
/// <param name="InstallationRoot">The target game directory.</param>
/// <param name="CurrentVersion">The prior version selected for signed baseline lookup.</param>
/// <param name="CurrentFlavor">The prior package flavor.</param>
/// <param name="Flavor">The selected target package flavor.</param>
/// <param name="GameCommit">The already installed supported game commit.</param>
/// <param name="HasActiveRun">Whether a run is in progress.</param>
/// <param name="ApprovedPaths">The exact consented replacement conflicts.</param>
/// <param name="ApprovedFiles">The exact reviewed local fingerprints when consent was collected before downloads.</param>
public sealed record IronmonUpdateRequest(string InstallationRoot, string CurrentVersion, string CurrentFlavor, string Flavor, string GameCommit, bool HasActiveRun, string[] ApprovedPaths, ReviewedFileConsent[]? ApprovedFiles = null);

/// <summary>
/// Binds a replacement decision to reviewed local bytes or an explicitly missing file.
/// </summary>
/// <remarks>
/// Constructs exact consent captured before potentially lengthy preparation.
/// </remarks>
/// <param name="Path">The exact reviewed conflict pathname.</param>
/// <param name="Local">The reviewed file or directory identity, or null for a deleted file.</param>
public sealed record ReviewedFileConsent(string Path, LocalFileEntry? Local);

/// <summary>
/// Stores signed release evidence separately from mutable transaction progress.
/// </summary>
/// <remarks>
/// Constructs offline recovery evidence; the helper reauthenticates every embedded document.
/// </remarks>
/// <param name="SchemaVersion">The authorization envelope schema.</param>
/// <param name="Request">The exact update selection.</param>
/// <param name="Target">The authenticated target release documents.</param>
/// <param name="Current">The authenticated previous release or null for explicit signed legacy adoption.</param>
/// <param name="PreviousGameCommit">The signed historical game baseline for a combined update, or null for an Ironmon-only update.</param>
/// <param name="Purpose">The independently validated installation or update purpose.</param>
public sealed record IronmonUpdateAuthorization(int SchemaVersion, IronmonUpdateRequest Request, ReleaseEvidence Target, ReleaseEvidence? Current, string? PreviousGameCommit = null, InstallationPurpose Purpose = InstallationPurpose.Update);

/// <summary>
/// Identifies a fully backed-up transaction and its independently staged helper.
/// </summary>
/// <remarks>
/// Constructs the handoff result without closing the game or tracker during preparation.
/// </remarks>
/// <param name="InstallationRoot">The bound installation.</param>
/// <param name="TransactionId">The prepared transaction UUID.</param>
/// <param name="HelperVersion">The immutable independent helper directory version.</param>
/// <param name="HelperContent">The authenticated extracted helper identity.</param>
/// <param name="Version">The selected target Ironmon version.</param>
/// <param name="Elevation">The live administrator capability, or null for ordinary writable installations.</param>
public sealed record PreparedIronmonUpdate(string InstallationRoot, Guid TransactionId, string HelperVersion, GameFileContent HelperContent, string Version, ProtectedUpdateTicket? Elevation = null);

/// <summary>
/// Records the installed component version and authenticated content references without becoming an ownership authority.
/// </summary>
/// <remarks>
/// Constructs the deterministic receipt included in the core transaction.
/// </remarks>
/// <param name="SchemaVersion">The receipt schema.</param>
/// <param name="Version">The matching tracker/script/data version.</param>
/// <param name="Flavor">The installed package flavor.</param>
/// <param name="GameCommit">The verified supported game.</param>
/// <param name="ManifestSha256">The exact signed manifest digest.</param>
/// <param name="InventorySha256">The normalized authenticated inventory digest.</param>
public sealed record IronmonInstalledReceipt(int SchemaVersion, string Version, string Flavor, string GameCommit, string ManifestSha256, string InventorySha256);

/// <summary>
/// Returns unresolved exact file conflicts to the later review UI without applying any program changes.
/// </summary>
public sealed class IronmonUpdateConflictException : IOException
{
    /// <summary>
    /// Gets the immutable reviewed plan containing the conflicts.
    /// </summary>
    public FileUpdatePlan Plan { get; }

    /// <summary>
    /// Captures the plan that requires explicit replacement decisions.
    /// </summary>
    /// <param name="plan">The unchanged unresolved plan.</param>
    public IronmonUpdateConflictException(FileUpdatePlan plan) : base(UpdaterText.IronmonOnlyUpdateSomeInstalledFilesNeedAnExplicitReplacementDecisionBefore) => Plan = plan;
}
