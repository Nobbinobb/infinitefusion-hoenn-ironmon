using System.Diagnostics;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Inspects Setup destinations and feeds the existing signed transaction preparation services.
/// </summary>
/// <remarks>
/// Creates a Setup boundary without requiring installed tracker or game binaries.
/// </remarks>
/// <param name="verifier">The independently configured release trust.</param>
/// <param name="downloads">The shared verified artifact cache.</param>
/// <param name="runtime">The selected tracker runtime checker.</param>
/// <param name="gamePreparation">The private game preparer.</param>
/// <param name="gameVerification">The independent game verifier.</param>
/// <param name="protectedUpdates">The optional Windows administrator boundary configured by Setup.</param>
public sealed class SetupPreparation(ReleaseVerifier verifier, ReleaseDownloadStore downloads, ITrackerRuntimeCompatibility runtime, CombinedGamePreparation gamePreparation, CombinedGitVerification gameVerification, ProtectedUpdateClient? protectedUpdates = null)
{
    private const string GitDirectory = ".git";
    private const string CoreRuntime = "Ironmon Tracker/coreclr.dll";
    private readonly Func<string, string> _readVersion = ReadVersion;
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ITrackerRuntimeCompatibility _runtime = runtime;
    private readonly CombinedGamePreparation _gamePreparation = gamePreparation;
    private readonly CombinedGitVerification _gameVerification = gameVerification;
    private readonly ProtectedUpdateClient? _protectedUpdates = protectedUpdates;

    /// <summary>
    /// Creates an isolated version-reader fixture while preserving signed ownership and all filesystem checks.
    /// </summary>
    /// <param name="verifier">The independent release verifier.</param>
    /// <param name="downloads">The isolated download cache.</param>
    /// <param name="runtime">The runtime checker.</param>
    /// <param name="gamePreparation">The isolated game preparer.</param>
    /// <param name="gameVerification">The independent game verifier.</param>
    /// <param name="readVersion">The fixture executable metadata reader.</param>
    internal SetupPreparation(ReleaseVerifier verifier, ReleaseDownloadStore downloads, ITrackerRuntimeCompatibility runtime, CombinedGamePreparation gamePreparation, CombinedGitVerification gameVerification, Func<string, string> readVersion) : this(verifier, downloads, runtime, gamePreparation, gameVerification)
    {
        _readVersion = readVersion;
    }

    /// <summary>
    /// Reviews authenticated metadata and exact conflicts without creating the destination or downloading program archives.
    /// </summary>
    /// <param name="destination">The user-selected absolute local directory.</param>
    /// <param name="release">The signed discovery result.</param>
    /// <param name="flavor">The package for a new installation; an existing installation always retains its current flavor.</param>
    /// <param name="noActiveRun">The user's explicit confirmation that no Ironmon run is in progress.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    /// <returns>The complete review needed before Install.</returns>
    public async Task<SetupReview> ReviewAsync(string destination, ReleaseEvidence release, string? flavor = null, bool noActiveRun = false, CancellationToken cancellationToken = default)
    {
        var root = PlainPaths.Full(destination);
        if (Path.GetPathRoot(root) == root || File.Exists(root))
            throw new IOException(UpdaterText.SetupPreparationChooseAnInstallationFolderNotADriveRootOr);

        if (Directory.Exists(root) && UpdateTransaction.ReadActiveId(root) is not null)
            throw new InvalidOperationException(UpdaterText.SetupPreparationAnInterruptedInstallationNeedsRecoveryBeforeSetupCanContinue);

        var manifest = _verifier.Verify(release.Manifest, release.Signatures);
        if (ReleaseProtocol.ParseVersion(manifest.MinimumEngineVersion) > ReleaseProtocol.ParseVersion(ReleaseProtocol.EngineVersion))
            throw new InvalidOperationException(UpdaterText.SetupPreparationThisReleaseNeedsANewerIronmonSetupDownloadIts);

        var content = new IronmonOnlyUpdate(_verifier, _downloads, _runtime);
        var target = await content.CompleteEvidenceAsync(release, manifest, cancellationToken).ConfigureAwait(false);
        var local = Directory.Exists(root) ? await InstallationFileSnapshot.ReadAsync(root, cancellationToken).ConfigureAwait(false) : [];
        var empty = local.Count == 0 && !Path.Exists(PlainPaths.Child(root, GitDirectory));
        var tracker = PlainPaths.Child(root, UpdaterHandoff.TrackerRelativePath);
        var installed = File.Exists(tracker);
        var version = installed ? _readVersion(tracker) : SetupProtocol.UninstalledVersion;
        var currentFlavor = installed && !File.Exists(PlainPaths.Child(root, CoreRuntime)) ? ReleaseProtocol.RuntimeRequired : ReleaseProtocol.SelfContained;
        flavor = installed ? currentFlavor : flavor ?? ReleaseProtocol.SelfContained;
        var purpose = empty ? InstallationPurpose.InstallGame : !installed ? InstallationPurpose.AddIronmon : version == manifest.IronmonVersion ? InstallationPurpose.Repair : InstallationPurpose.Update;
        string? previous = null;
        if (!empty)
        {
            GameInstallationLocator.ValidateCandidate(root);
            previous = GameCompatibilityCheck.ReadHead(root);
            if (previous is null)
            {
                var matches = new List<string>();
                foreach (var reference in manifest.AdoptionBaselines)
                {
                    var game = _verifier.VerifyInventory(manifest, target.Inventories.Single(item => item.AssetName == reference.GameFilesAsset));
                    if (Matches(game, local))
                        matches.Add(reference.GameCommit);
                }

                if (matches.Count != 1)
                    throw new InvalidDataException(UpdaterText.SetupPreparationThisGameFolderDoesNotMatchOneSupportedGame);

                previous = matches[0];
            }
        }

        var gameCommit = previous is not null && manifest.Game.SupportedCommits.Contains(previous, StringComparer.Ordinal) ? previous : manifest.Game.PreferredCommit;
        var changeGame = previous != gameCommit;
        var includeGit = empty || changeGame || (!installed && !Directory.Exists(PlainPaths.Child(root, GitDirectory)));
        if (purpose == InstallationPurpose.Repair && changeGame)
            throw new InvalidOperationException(UpdaterText.SetupPreparationThisIronmonVersionIsAlreadyInstalledWithADifferent);

        if (includeGit)
            CombinedGitVerification.EnsureSupportedRoot(root);

        var request = new IronmonUpdateRequest(root, version, currentFlavor, flavor, gameCommit, installed && !noActiveRun, []);
        if (request.HasActiveRun && (includeGit || manifest.Game.ActiveRunPolicy != ReleaseProtocol.PreserveRuns))
            throw new SetupRunConfirmationRequiredException(includeGit ? UpdaterText.SetupPreparationThisReleaseChangesTheGameVersionFinishYourIronmon : UpdaterText.SetupPreparationThisReleaseRequiresAFinishedIronmonRunFinishYour);

        IronmonOnlyUpdate.ValidateSelection(request, manifest, purpose);

        var trackerPreparation = new TrackerUpdatePreparation(_verifier, _downloads, _runtime, _gamePreparation, _gameVerification);
        var current = purpose == InstallationPurpose.Update ? trackerPreparation.ReadCurrentRelease(root, version) : null;
        if (current is not null)
            current = await content.CompleteEvidenceAsync(current, _verifier.Verify(current.Manifest, current.Signatures), cancellationToken).ConfigureAwait(false);

        var authorization = new IronmonUpdateAuthorization(1, request, target, current, includeGit && !empty ? previous : null, purpose);
        var authority = new SignedIronmonAuthority(_verifier, _runtime, _gameVerification);
        var (Baseline, Target, Game) = authority.Resolve(authorization);
        IEnumerable<ManagedFile> baseline = IronmonOnlyUpdate.BaselineFiles(Baseline, local);
        IEnumerable<ManagedFile> desired = IronmonOnlyUpdate.TargetFiles(Target, IronmonOnlyUpdate.ReceiptBytes(authorization, Target));
        if (includeGit)
        {
            if (!empty)
                baseline = baseline.Concat(SignedIronmonAuthority.GameFiles(authority.ResolvePreviousGame(authorization)));

            desired = desired.Concat(SignedIronmonAuthority.GameFiles(Game));
        }
        else
        {
            await SignedIronmonAuthority.VerifyGameAsync(root, Game, cancellationToken).ConfigureAwait(false);
        }

        var plan = new FileUpdatePlanner(new FileManagementPolicy()).Create(baseline, local, desired);
        IronmonOnlyUpdate.RequireCompleteTarget(plan);
        var bytes = manifest.Assets.Where(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + flavor || asset.Role == ReleaseProtocol.UpdaterRole).Sum(asset => asset.Bytes);
        return new SetupReview(authorization, manifest, plan, bytes, includeGit, purpose == InstallationPurpose.Repair && plan.Entries.All(entry => entry.Action == FilePlanAction.Keep));
    }

    /// <summary>
    /// Prepares the selected installation through the shared updater engine, binding consent to reviewed bytes.
    /// </summary>
    /// <param name="review">The exact displayed review.</param>
    /// <param name="approvedPaths">The exact approved file conflicts.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The backed-up recoverable installation.</returns>
    public Task<PreparedIronmonUpdate> PrepareAsync(SetupReview review, string[] approvedPaths, CancellationToken cancellationToken = default)
    {
        var approved = approvedPaths.Length == 0 ? review.Plan : review.Plan.ApproveReplacements(approvedPaths);
        if (!approved.CanApply)
            throw new IronmonUpdateConflictException(approved);

        var selected = review.Authorization;
        var request = selected.Request with { ApprovedPaths = [.. approvedPaths], ApprovedFiles = [.. approvedPaths.Select(path => new ReviewedFileConsent(path, review.Plan.Entries.Single(entry => entry.Path == path).Local))] };
        if (_protectedUpdates is not null && ProtectedUpdateClient.RequiresElevation(request.InstallationRoot))
            return _protectedUpdates.PrepareAsync(selected with { Request = request }, cancellationToken);

        Directory.CreateDirectory(PlainPaths.Full(request.InstallationRoot));
        VerifyWritable(request.InstallationRoot);
        if (review.IncludesGame)
            return new CombinedUpdate(_verifier, _downloads, _runtime, _gamePreparation, _gameVerification).PrepareInstallationAsync(request, selected.PreviousGameCommit, selected.Target, selected.Current, selected.Purpose, cancellationToken);

        return new IronmonOnlyUpdate(_verifier, _downloads, _runtime).PrepareInstallationAsync(request, selected.Target, selected.Current, selected.Purpose, cancellationToken);
    }

    /// <summary>
    /// Probes installation write permission and writer contention before downloading program packages.
    /// </summary>
    /// <param name="root">The selected installation directory.</param>
    private static void VerifyWritable(string root)
    {
        using var lease = InstallationLease.Acquire(root);
    }

    /// <summary>
    /// Distinguishes game content from the updater's retained workspace after a cancelled fresh installation.
    /// </summary>
    /// <param name="root">The existing destination directory.</param>
    /// <returns>Whether the directory contains anything outside the updater's ordinary state directory.</returns>
    private static bool HasInstallationContent(string root)
    {
        var state = PlainPaths.Child(root, InstallationLease.StateDirectory);
        if (File.Exists(state))
            throw new InvalidDataException(UpdaterText.InstallationFileSnapshotGitAndUpdaterMetadataMustBeOrdinaryDirectories);

        return Directory.EnumerateFileSystemEntries(root).Any(path => !path.Equals(state, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Identifies the selected folder and installed package without downloading releases or changing any files.
    /// </summary>
    /// <param name="destination">The selected absolute game directory or new empty directory.</param>
    /// <returns>The normalized destination and observed package flavor, if a tracker is installed.</returns>
    public static SetupDestination InspectDestination(string destination)
    {
        var root = PlainPaths.Full(destination);
        if (Path.GetPathRoot(root) == root || File.Exists(root))
            throw new IOException(UpdaterText.SetupPreparationChooseAnInstallationFolderNotADriveRootOr);

        if (Directory.Exists(root) && UpdateTransaction.ReadActiveId(root) is null && HasInstallationContent(root))
            GameInstallationLocator.ValidateCandidate(root);

        var installed = File.Exists(PlainPaths.Child(root, UpdaterHandoff.TrackerRelativePath));
        return new SetupDestination(root, installed ? File.Exists(PlainPaths.Child(root, CoreRuntime)) ? ReleaseProtocol.SelfContained : ReleaseProtocol.RuntimeRequired : null);
    }

    /// <summary>
    /// Reads a package version as an observation; signed inventories still decide all ownership.
    /// </summary>
    /// <param name="executable">The selected existing tracker.</param>
    /// <returns>The canonical observed three-part package version.</returns>
    private static string ReadVersion(string executable)
    {
        var file = FileVersionInfo.GetVersionInfo(executable);
        if (file.FileMajorPart == 0 && file.FileMinorPart == 0 && file.FileBuildPart == 0)
            throw new InvalidDataException(UpdaterText.SetupPreparationTheInstalledTrackerVersionCouldNotBeIdentifiedRestore);

        return new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart).ToString(3);
    }

    /// <summary>
    /// Matches signed ZIP fingerprints without assuming that mutable version labels prove a baseline.
    /// </summary>
    /// <param name="game">The signed game inventory.</param>
    /// <param name="local">The observed local files.</param>
    /// <returns>Whether every required signed game file matches.</returns>
    private static bool Matches(ReleaseFileInventory game, IReadOnlyList<LocalFileEntry> local)
    {
        var files = local.ToDictionary(file => file.Path, StringComparer.Ordinal);
        return game.Files.Where(file => !ReleaseVerifier.IsLegacyDocument(file.Path)).All(file => files.TryGetValue(file.Path, out var entry) && (entry.Content == ReleaseProtocol.Content(file.Bytes, file.Sha256) || file.WindowsText is { } windows && entry.Content == ReleaseProtocol.Content(windows.Bytes, windows.Sha256)));
    }
}

/// <summary>
/// Holds the exact signed Setup selection and its independently reconstructable file review.
/// </summary>
/// <remarks>
/// Constructs immutable review data without granting filesystem mutation authority.
/// </remarks>
/// <param name="Authorization">The signed release evidence and selected purpose.</param>
/// <param name="Manifest">The authenticated target manifest.</param>
/// <param name="Plan">The observed local conflicts and file operations.</param>
/// <param name="PackageBytes">The known tracker and helper download bytes.</param>
/// <param name="IncludesGame">Whether ordinary game Git metadata participates.</param>
/// <param name="AlreadyCurrent">Whether no program operation is necessary.</param>
public sealed record SetupReview(IronmonUpdateAuthorization Authorization, ReleaseManifest Manifest, FileUpdatePlan Plan, long PackageBytes, bool IncludesGame, bool AlreadyCurrent);

/// <summary>
/// Describes a local folder observation used to present applicable installer choices.
/// </summary>
/// <remarks>
/// Constructs presentation data; signed review still independently verifies the installation before any changes.
/// </remarks>
/// <param name="Root">The normalized game directory.</param>
/// <param name="InstalledFlavor">The observed installed package, or null for a new Ironmon installation.</param>
public sealed record SetupDestination(string Root, string? InstalledFlavor);

/// <summary>
/// Requests run-completion confirmation only when the selected release requires it.
/// </summary>
/// <remarks>
/// Constructs an actionable review interruption without permitting preparation or filesystem mutation.
/// </remarks>
/// <param name="message">The release-specific reason a finished run is required.</param>
public sealed class SetupRunConfirmationRequiredException(string message) : InvalidOperationException(message);
