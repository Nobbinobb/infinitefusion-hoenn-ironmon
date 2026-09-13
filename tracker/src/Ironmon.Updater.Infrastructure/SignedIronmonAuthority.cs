using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reauthenticates signed inventories independently of mutable recovery journals.
/// </summary>
/// <remarks>
/// Constructs the offline authority shared by preparation and the independent helper.
/// </remarks>
/// <param name="verifier">Public keys embedded independently of downloaded documents.</param>
/// <param name="runtime">The runtime prerequisite checker.</param>
/// <param name="gameGit">The independently configured offline Git verifier for combined updates.</param>
public sealed class SignedIronmonAuthority(ReleaseVerifier verifier, ITrackerRuntimeCompatibility runtime, CombinedGitVerification? gameGit = null) : ITransactionAuthority
{
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ITrackerRuntimeCompatibility _runtime = runtime;
    private readonly CombinedGitVerification? _gameGit = gameGit;

    /// <summary>
    /// Checks every managed target and baseline against authenticated release evidence.
    /// </summary>
    /// <param name="description">The proposed transaction.</param>
    /// <param name="exactBytes">Its exact persisted serialization.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A task that rejects unauthorized ownership or incompatible game files.</returns>
    public async Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken)
    {
        if (!exactBytes.Span.SequenceEqual(TransactionStorage.Serialize(description)))
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheTransactionBytesDoNotMatchTheirInterpretedDescription);

        var authorization = Read(description);
        IronmonOnlyUpdate.ValidateConsent(authorization.Request, description.Plan.Local);
        var (Baseline, Target, Game) = Resolve(authorization);
        var baseline = IronmonOnlyUpdate.BaselineFiles(Baseline, description.Plan.Local);
        var target = IronmonOnlyUpdate.TargetFiles(Target, IronmonOnlyUpdate.ReceiptBytes(authorization, Target));
        if (authorization.PreviousGameCommit is not null || authorization.Purpose == InstallationPurpose.InstallGame)
        {
            baseline = [.. baseline.Concat(authorization.Purpose == InstallationPurpose.InstallGame ? [] : GameFiles(ResolvePreviousGame(authorization))).OrderBy(file => file.Path, StringComparer.Ordinal)];
            target = [.. target.Concat(GameFiles(Game)).OrderBy(file => file.Path, StringComparer.Ordinal)];
            await RequireGit().AuthorizeAsync(description, authorization.PreviousGameCommit ?? authorization.Request.GameCommit, authorization.Request.GameCommit, cancellationToken).ConfigureAwait(false);
        }

        if (!baseline.SequenceEqual(description.Plan.Baseline.OrderBy(file => file.Path, StringComparer.Ordinal)) || !target.SequenceEqual(description.Plan.Target.OrderBy(file => file.Path, StringComparer.Ordinal)) || !authorization.Request.ApprovedPaths.Order(StringComparer.Ordinal).SequenceEqual(description.Plan.ApprovedPaths.Order(StringComparer.Ordinal)))
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheTransactionClaimsFilesOrApprovalsOutsideItsAuthenticated);

        if (authorization.Purpose == InstallationPurpose.InstallGame && (description.Plan.Local.Length != 0 || description.GitBefore is not null))
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityANewGameTransactionMustOriginateFromAnEmpty);

        IronmonOnlyUpdate.RequireCompleteTarget(description.Plan.Rebuild());
        if (authorization.PreviousGameCommit is null && authorization.Purpose != InstallationPurpose.InstallGame)
            await VerifyGameAsync(description.InstallationRoot, Game, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the installed tracker, scripts, data and receipt before the transaction commits.
    /// </summary>
    /// <param name="description">The authenticated transaction.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A task that rejects incomplete component sets.</returns>
    public async Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(description, TransactionStorage.Serialize(description), cancellationToken).ConfigureAwait(false);
        var authorization = Read(description);
        var inventories = Resolve(authorization);
        var target = IronmonOnlyUpdate.TargetFiles(inventories.Target, IronmonOnlyUpdate.ReceiptBytes(authorization, inventories.Target));
        foreach (var file in target.Where(file => file.Policy != ManagedFilePolicy.Retain))
        {
            var content = await TransactionStorage.ContentAsync(PlainPaths.Child(description.InstallationRoot, file.Path), cancellationToken).ConfigureAwait(false);
            if (content != file.Content)
                throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheInstalledTrackerScriptsOrDataDoNotMatch);
        }

        await _runtime.EnsureAsync(description.InstallationRoot, authorization.Request.Flavor, cancellationToken).ConfigureAwait(false);
        if (authorization.PreviousGameCommit is not null || authorization.Purpose == InstallationPurpose.InstallGame)
            await RequireGit().VerifyInstalledAsync(description, authorization.Request.GameCommit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves unique signed inventories for the selected target, prior package and installed game.
    /// </summary>
    /// <param name="authorization">The independently reauthenticated evidence.</param>
    /// <returns>The compatible ownership inventories.</returns>
    internal (ReleaseFileInventory Baseline, ReleaseFileInventory Target, ReleaseFileInventory Game) Resolve(IronmonUpdateAuthorization authorization)
    {
        if (authorization.SchemaVersion != 1)
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheReleaseAuthorizationSchemaIsUnsupported);

        var request = authorization.Request;
        var manifest = _verifier.Verify(authorization.Target.Manifest, authorization.Target.Signatures);
        IronmonOnlyUpdate.ValidateSelection(request, manifest, authorization.Purpose);
        var previousCommit = authorization.PreviousGameCommit ?? request.GameCommit;
        if (authorization.PreviousGameCommit is not null || authorization.Purpose == InstallationPurpose.InstallGame)
        {
            ReleaseProtocol.ValidateCommit(previousCommit);
            if ((previousCommit == request.GameCommit && authorization.Purpose == InstallationPurpose.Update) || request.HasActiveRun)
                throw new InvalidOperationException(UpdaterText.SignedIronmonAuthorityFinishTheActiveRunBeforeChangingItsGameVersion);
        }

        var inventories = Inventories(manifest, authorization.Target);
        var target = Select(inventories, manifest.Assets.Where(asset => asset.Role == ReleaseProtocol.InventoryRole).Select(asset => asset.Name), request.Flavor);
        var baselineReference = manifest.AdoptionBaselines.Single(item => item.GameCommit == request.GameCommit);
        var game = inventories[baselineReference.GameFilesAsset];
        ReleaseFileInventory baseline;
        if (authorization.Purpose is InstallationPurpose.AddIronmon or InstallationPurpose.InstallGame)
        {
            if (authorization.Current is not null || (authorization.Purpose == InstallationPurpose.InstallGame && authorization.PreviousGameCommit is not null))
                throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityAFreshInstallationCannotClaimOwnershipFromAPrior);

            baseline = target with { Files = [] };
        }
        else if (authorization.Purpose == InstallationPurpose.Repair)
        {
            if (authorization.Current is not null || authorization.PreviousGameCommit is not null)
                throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityRepairCannotChangeTheGameOrUseADifferent);

            baseline = Select(inventories, manifest.Assets.Where(asset => asset.Role == ReleaseProtocol.InventoryRole).Select(asset => asset.Name), request.CurrentFlavor);
        }
        else if (authorization.Current is { } current)
        {
            var previous = _verifier.Verify(current.Manifest, current.Signatures);
            if (previous.IronmonVersion != request.CurrentVersion || previous.ReleaseSequence >= manifest.ReleaseSequence || !previous.Game.SupportedCommits.Contains(previousCommit, StringComparer.Ordinal))
                throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityThePreviousSignedReleaseIsNotACompatibleForward);

            baseline = Select(Inventories(previous, current), previous.Assets.Where(asset => asset.Role == ReleaseProtocol.InventoryRole).Select(asset => asset.Name), request.CurrentFlavor);
        }
        else
        {
            baselineReference = manifest.AdoptionBaselines.Single(item => item.GameCommit == previousCommit);
            var legacy = baselineReference.LegacyPackages.SingleOrDefault(item => item.Version == request.CurrentVersion && item.Flavor == request.CurrentFlavor) ?? throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityThereIsNoSignedInventoryForThisInstalledLegacy);
            baseline = inventories[legacy.FilesAsset];
        }

        if ((authorization.PreviousGameCommit is not null || authorization.Purpose == InstallationPurpose.InstallGame) && !target.Files.Any(file => file.Path == FileManagementPolicy.BootstrapPath))
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityACombinedReleaseMustContainTheEarlyGameStartup);

        return (baseline, target, game);
    }

    /// <summary>
    /// Reads the bounded evidence at the fixed transaction identity and prohibits game mutations.
    /// </summary>
    /// <param name="description">The transaction being checked.</param>
    /// <returns>The untrusted evidence envelope for independent authentication.</returns>
    internal static IronmonUpdateAuthorization Read(TransactionDescription description)
    {
        var path = IronmonOnlyUpdate.AuthorizationPath(description.InstallationRoot, description.TransactionId);
        var authorization = ReleaseJson.Parse<IronmonUpdateAuthorization>(TransactionStorage.ReadBytes(path), 64 * 1024 * 1024);
        if (!PlainPaths.Full(authorization.Request.InstallationRoot).Equals(description.InstallationRoot, StringComparison.OrdinalIgnoreCase) || (authorization.PreviousGameCommit is null && authorization.Purpose != InstallationPurpose.InstallGame ? description.GitBefore is not null || description.GitAfter is not null : description.GitAfter is null))
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityThisAuthorizationCannotRelocateTheInstallationOrModifyGame);

        return authorization;
    }

    /// <summary>
    /// Resolves a historical game inventory without treating historical adoption support as target compatibility.
    /// </summary>
    /// <param name="authorization">The signed release evidence.</param>
    /// <returns>The independently authenticated previous game inventory.</returns>
    internal ReleaseFileInventory ResolvePreviousGame(IronmonUpdateAuthorization authorization)
    {
        var evidence = authorization.Current ?? authorization.Target;
        var manifest = _verifier.Verify(evidence.Manifest, evidence.Signatures);
        var reference = manifest.AdoptionBaselines.Single(item => item.GameCommit == authorization.PreviousGameCommit);
        return Inventories(manifest, evidence)[reference.GameFilesAsset];
    }

    /// <summary>
    /// Converts signed game ownership while retaining explicitly recognized Windows text representations.
    /// </summary>
    /// <param name="inventory">The signed game inventory.</param>
    /// <returns>The exact managed game files excluding legacy documentation and protected player state.</returns>
    internal static ManagedFile[] GameFiles(ReleaseFileInventory inventory)
        => [.. inventory.Files.Where(file => ReleaseVerifier.IsManagedGamePath(file.Path)).Select(file => new ManagedFile(file.Path, ReleaseProtocol.Content(file.Bytes, file.Sha256), ManagedFileOwner.Game, ManagedFilePolicy.Replace, file.WindowsText is null ? null : ReleaseProtocol.Content(file.WindowsText.Bytes, file.WindowsText.Sha256)))];

    /// <summary>
    /// Requires independent Git verification instead of trusting snapshots in local JSON.
    /// </summary>
    /// <returns>The configured verifier or an explicit closed authorization failure.</returns>
    private CombinedGitVerification RequireGit()
        => _gameGit ?? throw new InvalidOperationException(UpdaterText.SignedIronmonAuthorityCombinedGameVerificationIsNotConfiguredInThisUpdater);

    /// <summary>
    /// Authenticates all evidence and requires one current inventory for each package flavor.
    /// </summary>
    /// <param name="manifest">The authenticated manifest.</param>
    /// <param name="evidence">Its exact inventory documents.</param>
    /// <returns>The unique inventory mapping.</returns>
    private Dictionary<string, ReleaseFileInventory> Inventories(ReleaseManifest manifest, ReleaseEvidence evidence)
    {
        var result = new Dictionary<string, ReleaseFileInventory>(StringComparer.Ordinal);
        foreach (var item in evidence.Inventories)
        {
            if (!result.TryAdd(item.AssetName, _verifier.VerifyInventory(manifest, item)))
                throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheRecoveryEvidenceContainsDuplicateInventoryDocuments);
        }

        var references = manifest.Assets.Where(asset => asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.GameInventoryRole or ReleaseProtocol.LegacyRole).ToArray();
        if (result.Count != references.Length || references.Any(asset => !result.ContainsKey(asset.Name)))
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheRecoveryEvidenceIsMissingAnAuthenticatedInventory);

        var current = references.Where(asset => asset.Role == ReleaseProtocol.InventoryRole).Select(asset => result[asset.Name]).ToArray();
        if (current.Select(item => item.Flavor).Distinct(StringComparer.Ordinal).Count() != 2)
            throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheReleaseMustDescribeBothDistinctTrackerPackageFlavors);

        return result;
    }

    /// <summary>
    /// Selects a unique package inventory by its authenticated flavor.
    /// </summary>
    /// <param name="inventories">The verified documents.</param>
    /// <param name="names">The manifest's current package references.</param>
    /// <param name="flavor">The requested flavor.</param>
    /// <returns>The unique selected inventory.</returns>
    private static ReleaseFileInventory Select(Dictionary<string, ReleaseFileInventory> inventories, IEnumerable<string> names, string flavor)
        => names.Select(name => inventories[name]).Single(item => item.Flavor == flavor);

    /// <summary>
    /// Verifies the already installed supported game directly from signed file hashes without invoking Git.
    /// </summary>
    /// <param name="root">The installed game directory.</param>
    /// <param name="game">The authenticated compatible game inventory.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A task that rejects modified or incomplete required game files.</returns>
    internal static async Task VerifyGameAsync(string root, ReleaseFileInventory game, CancellationToken cancellationToken)
    {
        foreach (var file in game.Files.Where(file => ReleaseVerifier.IsManagedGamePath(file.Path)))
        {
            var content = await TransactionStorage.ContentAsync(PlainPaths.Child(root, file.Path), cancellationToken).ConfigureAwait(false);
            var windowsText = file.WindowsText is null ? null : ReleaseProtocol.Content(file.WindowsText.Bytes, file.WindowsText.Sha256);
            if (content != ReleaseProtocol.Content(file.Bytes, file.Sha256) && (windowsText is null || content != windowsText))
                throw new InvalidDataException(UpdaterText.SignedIronmonAuthorityTheInstalledGameDoesNotMatchASupportedSigned);
        }
    }
}
