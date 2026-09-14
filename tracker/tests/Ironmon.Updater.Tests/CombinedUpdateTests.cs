using System.Text;
using System.Diagnostics;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises signed combined updates and subsequent official launcher commands using disposable A/B/C Git histories.
/// </summary>
/// <remarks>
/// Constructs the fixture suite with independently verified private Git.
/// </remarks>
/// <param name="tool">The pinned private Git fixture.</param>
public sealed class CombinedUpdateTests(PinnedGitFixture tool) : IClassFixture<PinnedGitFixture>
{
    private const string SetupDestination = "setup";
    private const string OtherDestination = "other";
    private const string DisposableSetup = "downloaded-setup.exe";
    private const string Home = "combined-home";
    private const string ZipExtension = ".zip";
    private const string GitDirectory = ".git";
    private const string Init = "init";
    private const string InitialBranch = "--initial-branch=releases";
    private const string EmptyTemplate = "--template=";
    private const string Remote = "remote";
    private const string Add = "add";
    private const string Origin = "origin";
    private const string Fetch = "fetch";
    private const string Depth = "--depth=1";
    private const string Reset = "reset";
    private const string Mixed = "--mixed";
    private const string Hard = "--hard";
    private const string Config = "config";
    private const string BranchRemote = "branch.releases.remote";
    private const string BranchMerge = "branch.releases.merge";
    private const string Heads = "refs/heads/releases";
    private const string RemoteRef = "origin/releases";
    private const string Branch = "releases";
    private const string Revision = "rev-parse";
    private const string Head = "HEAD";
    private const string ListFiles = "ls-files";
    private const string Tree = "ls-tree";
    private const string Recursive = "-r";
    private const string Names = "--name-only";
    private const string Null = "-z";
    private const string Show = "show";
    private const string Pull = "pull";
    private const string FastForward = "--ff-only";
    private const string PlayerEdit = "preserved local edit";
    private const string PreservedPath = "personal-mod.txt";
    private const string FixtureMarker = ".transaction-fixture";
    private const string ApplyMode = "signed-game-apply";
    private const string RecoverMode = "signed-game-recover";
    private const string Ready = "BOUNDARY";
    private const string ExtraBranch = "personal-work";
    private const string BranchCommand = "branch";
    private const string LocalExclusion = ".git/info/exclude";
    private const string WitnessHead = "/.git/HEAD";
    private const string HistoricalAsset = "old-download.bin";
    private const string Commit = "commit";
    private const string CommitMessage = "-m";
    private const string AuthorName = "user.name=Installer fixture";
    private const string AuthorEmail = "user.email=installer@example.invalid";
    private const string Configuration = "-c";
    private const string AddAll = "--all";
    private const string ShallowFile = "shallow";
    private const string CountCommits = "rev-list";
    private const string Count = "--count";
    private const string CatFile = "cat-file";
    private const string Exists = "-e";
    private const string ObjectsDirectory = "objects";
    private const string PackDirectory = "pack";
    private const string PackPattern = "*.pack";
    private const string OneCommit = "1";

    /// <summary>
    /// Prepares the game while the tracker download is held, then reports the remaining download or drains cancellation.
    /// </summary>
    /// <param name="cancel">Whether to cancel while the independent package is still downloading.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GamePreparationOverlapsTheTrackerDownload(bool cancel)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, false);
        var request = release.Request() with { GameCommit = game.Target };
        var manifest = release.Verifier.Verify(release.Evidence.Manifest, release.Evidence.Signatures);
        var asset = manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + request.Flavor);
        var source = new PackageGate(release, new Uri(asset.Url));
        var downloads = new ReleaseDownloadStore(release.Cache, source);
        var coordinator = new CombinedUpdate(release.Verifier, downloads, SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release));
        var remainingDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var progress = new InstallationProgressScope(value =>
        {
            if (value.Stage == InstallationStage.DownloadingPackage && value.Total == asset.Bytes)
                remainingDownload.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var preparing = coordinator.PrepareAsync(request, game.Baseline.Commit, release.Evidence, cancellationToken: cancellation.Token);
        try
        {
            await remainingDownload.Task.WaitAsync(cancellation.Token);
            Assert.False(preparing.IsCompleted);
            if (cancel)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
                Assert.Null(UpdateTransaction.ReadActiveId(release.Root));
            }
            else
            {
                source.Release.TrySetResult();
                var prepared = await preparing;
                var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
                await engine.DiscardPreparedAsync(release.Root, prepared.TransactionId);
            }

            Assert.True(source.Finished);
            const string PartialPattern = "*.partial";
            Assert.Empty(Directory.EnumerateFiles(release.Cache, PartialPattern));
            Assert.Empty(Directory.EnumerateFileSystemEntries(game.Staging));
        }
        finally
        {
            await cancellation.CancelAsync();
            source.Release.TrySetResult();
            try
            {
                await preparing;
            }
            catch
            {
                // Drain the owned operation before fixture cleanup, including when an assertion fails.
            }
        }
    }

    /// <summary>
    /// Holds only the selected package while allowing signed metadata and independent game work to finish.
    /// </summary>
    /// <remarks>
    /// Wraps the fixture transport with one explicitly owned asynchronous gate.
    /// </remarks>
    /// <param name="source">The signed fixture transport.</param>
    /// <param name="package">The package URI to delay.</param>
    private sealed class PackageGate(IArtifactSource source, Uri package) : IArtifactSource
    {
        /// <summary>
        /// Gets the test-controlled release of the package transfer.
        /// </summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets whether the held transfer has completed or observed cancellation.
        /// </summary>
        internal bool Finished { get; private set; }

        /// <summary>
        /// Delays only the package and records its complete lifetime, including cancellation.
        /// </summary>
        /// <param name="uri">The requested fixture artifact.</param>
        /// <param name="destination">The owned download stream.</param>
        /// <param name="maximumBytes">The signed size limit.</param>
        /// <param name="cancellationToken">The shared preparation token.</param>
        public async Task CopyToAsync(Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken)
        {
            if (uri != package)
            {
                await source.CopyToAsync(uri, destination, maximumBytes, cancellationToken);
                return;
            }

            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                await source.CopyToAsync(uri, destination, maximumBytes, cancellationToken);
            }
            finally
            {
                Finished = true;
            }
        }
    }

    /// <summary>
    /// Excludes retired binary history from fresh downloads while preserving subsequent launcher pulls and signed preparation.
    /// </summary>
    [Fact]
    public async Task FreshGameOmitsHistoricalAssetsAndStillAcceptsLauncherUpdates()
    {
        using var game = new ZipGameFixture(tool);
        using var workspace = new TestWorkspace();
        await game.InitializeAsync();
        var oldFile = Path.Combine(game.Upstream, HistoricalAsset);
        await File.WriteAllBytesAsync(oldFile, System.Security.Cryptography.RandomNumberGenerator.GetBytes(2 * 1024 * 1024));
        await CommitFixtureAsync(game);
        var retiredCommit = await game.RunAsync(game.Upstream, Revision, Head);
        File.Delete(oldFile);
        await CommitFixtureAsync(game);
        var current = await game.RunAsync(game.Upstream, Revision, Head);
        var inventory = await InventoryAsync(game, current);
        var values = new List<InstallationProgress>();
        using var progress = new InstallationProgressScope(values.Add);
        var root = workspace.PathFor(SetupDestination);
        Directory.CreateDirectory(root);
        using var prepared = await new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging).PrepareAsync(root, null, inventory);
        var checkout = Path.GetDirectoryName(prepared.Metadata)!;
        Assert.True(File.Exists(Path.Combine(prepared.Metadata, ShallowFile)));
        Assert.Equal(OneCommit, await game.RunAsync(checkout, CountCommits, Count, Head));
        Assert.NotEqual(0, (await game.Git.RunAsync(checkout, [CatFile, Exists, retiredCommit + ':' + HistoricalAsset])).ExitCode);
        Assert.InRange(Directory.EnumerateFiles(Path.Combine(prepared.Metadata, ObjectsDirectory, PackDirectory), PackPattern).Sum(path => new FileInfo(path).Length), 1, 1024 * 1024);
        Assert.Contains(values, value => value.Stage == InstallationStage.ExtractingFiles && value.Completed == inventory.Files.Length);
        await game.RunAsync(checkout, Reset, Hard, Head);
        await File.AppendAllTextAsync(Path.Combine(game.Upstream, ZipGameFixture.TextFile), PlayerEdit);
        await CommitFixtureAsync(game);
        await game.RunAsync(checkout, Pull, FastForward);
        Assert.Equal(await game.RunAsync(game.Upstream, Revision, Head), await game.RunAsync(checkout, Revision, Head));
    }

    /// <summary>
    /// Commits disposable upstream changes using an explicit fixture identity.
    /// </summary>
    /// <param name="game">The fixture repository.</param>
    /// <returns>The completed fixture commit.</returns>
    private static async Task CommitFixtureAsync(ZipGameFixture game)
    {
        await game.RunAsync(game.Upstream, Add, AddAll);
        await game.RunAsync(game.Upstream, Configuration, AuthorName, Configuration, AuthorEmail, Commit, CommitMessage, PlayerEdit);
    }

    /// <summary>
    /// Preserves player edits and root distribution documents when signed game and package content changes.
    /// </summary>
    /// <param name="gitCheckout">Whether the original installation already has ordinary Git metadata.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedTrackedFilesSurviveCombinedUpdates(bool gitCheckout)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture(distributionDocuments: true);
        await game.InitializeAsync(includeProtectedFile: true);
        await ConfigureAsync(game, release, gitCheckout);
        release.Write(ZipGameFixture.ProtectedFile, Encoding.UTF8.GetBytes(PlayerEdit));
        release.Write(SignedReleaseFixture.ReadmeDocument, Encoding.UTF8.GetBytes(PlayerEdit));
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(release.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Equal(PlayerEdit, await File.ReadAllTextAsync(Path.Combine(release.Root, ZipGameFixture.ProtectedFile)));
        foreach (var document in SignedReleaseFixture.DistributionDocuments)
            Assert.Equal(document == SignedReleaseFixture.ReadmeDocument ? PlayerEdit : SignedReleaseFixture.VersionA, await File.ReadAllTextAsync(Path.Combine(release.Root, document)));

        Assert.Equal(game.Target, await game.RunAsync(release.Root, Revision, Head));
        Assert.Equal(ZipGameFixture.TextB, await File.ReadAllTextAsync(Path.Combine(release.Root, ZipGameFixture.TextFile)));
    }

    /// <summary>
    /// Installs into the resolved child and preserves parent files through commit, rollback and subsequent updates.
    /// </summary>
    /// <param name="failFinal">Whether final validation must roll back the entire fresh installation.</param>
    /// <param name="occupiedParent">Whether the selected parent already contains unrelated content.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SetupInstallsNamedChildThroughTheSharedTransaction(bool failFinal, bool occupiedParent)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture(distributionDocuments: true);
        using var workspace = new TestWorkspace();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, false);
        var parent = workspace.PathFor(SetupDestination);
        Directory.CreateDirectory(parent);
        var unrelated = Path.Combine(parent, PreservedPath);
        if (occupiedParent)
            await File.WriteAllTextAsync(unrelated, PlayerEdit);

        var root = SetupPreparation.ResolveDestination(parent).Root;
        Assert.Equal(Path.Combine(parent, SetupPreparation.InstallationDirectoryName), root);
        var downloads = new ReleaseDownloadStore(release.Cache, release);
        var preparation = new SetupPreparation(release.Verifier, downloads, SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release), _ => SignedReleaseFixture.VersionB);
        var review = await preparation.ReviewAsync(root, release.Evidence, failFinal ? ReleaseProtocol.RuntimeRequired : null);
        Assert.Equal(InstallationPurpose.InstallGame, review.Authorization.Purpose);
        Assert.False(Directory.Exists(root));
        Assert.DoesNotContain(release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
        var prepared = await preparation.PrepareAsync(review, []);
        var setup = workspace.PathFor(DisposableSetup);
        await File.WriteAllTextAsync(setup, DisposableSetup);
        File.Delete(setup);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(failFinal), Verification(game, release)), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(root, prepared.TransactionId);
        Assert.Equal(failFinal ? TransactionPhase.RolledBack : TransactionPhase.Committed, result.Phase);
        Assert.Null(UpdateTransaction.ReadActiveId(root));
        Assert.Equal(root, SetupPreparation.ResolveDestination(parent).Root);
        Assert.Equal(root, SetupPreparation.ResolveDestination(root).Root);
        if (occupiedParent)
            Assert.Equal(PlayerEdit, await File.ReadAllTextAsync(unrelated));

        Assert.False(File.Exists(Path.Combine(parent, SignedReleaseFixture.Script)));
        foreach (var document in SignedReleaseFixture.DistributionDocuments)
            Assert.False(File.Exists(Path.Combine(root, document)));

        if (failFinal)
        {
            Assert.Empty(await InstallationFileSnapshot.ReadAsync(root));
            Assert.False(Directory.Exists(Path.Combine(root, GitDirectory)));
        }
        else
        {
            Assert.Equal(game.Target, GameCompatibilityCheck.ReadHead(root));
            Assert.Equal(SignedReleaseFixture.VersionB, await File.ReadAllTextAsync(Path.Combine(root, SignedReleaseFixture.Script)));
            var current = new TrackerUpdatePreparation(release.Verifier, downloads, SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release)).ReadCurrentRelease(root, SignedReleaseFixture.VersionB);
            Assert.NotNull(current);
            var repair = await preparation.ReviewAsync(root, release.Evidence, noActiveRun: true);
            Assert.True(repair.AlreadyCurrent);
            Assert.Equal(InstallationPurpose.Repair, repair.Authorization.Purpose);
            var again = await preparation.PrepareAsync(repair, []);
            Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(root, again.TransactionId)).Phase);
            release.AdvanceVersion();
            var tracker = new TrackerUpdatePreparation(release.Verifier, downloads, SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release));
            var request = new IronmonUpdateRequest(root, SignedReleaseFixture.VersionB, ReleaseProtocol.SelfContained, ReleaseProtocol.SelfContained, game.Target, false, []);
            var nextReview = await tracker.ReviewAsync(request, release.Evidence, current);
            var next = await tracker.PrepareAsync(nextReview, [], false);
            Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(root, next.TransactionId)).Phase);
            Assert.Equal(SignedReleaseFixture.VersionC, await File.ReadAllTextAsync(Path.Combine(root, SignedReleaseFixture.Script)));
            Assert.NotNull(tracker.ReadCurrentRelease(root, SignedReleaseFixture.VersionC));
        }
    }

    /// <summary>
    /// Adds Ironmon to a recognized existing game and rejects files arriving after an empty-folder review.
    /// </summary>
    [Fact]
    public async Task SetupAdoptsGameOnlyAndRejectsConcurrentFreshFolderWrites()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, false);
        var root = workspace.PathFor(SetupDestination);
        Directory.CreateDirectory(root);
        foreach (var file in (await InventoryAsync(game, game.Baseline.Commit)).Files)
        {
            var destination = Path.Combine(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(Path.Combine(game.Installation, file.Path)));
        }

        var preparation = new SetupPreparation(release.Verifier, new ReleaseDownloadStore(release.Cache, release), SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release));
        var review = await preparation.ReviewAsync(root, release.Evidence);
        Assert.Equal(InstallationPurpose.AddIronmon, review.Authorization.Purpose);
        var prepared = await preparation.PrepareAsync(review, []);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(root, prepared.TransactionId)).Phase);
        Assert.Equal(game.Target, GameCompatibilityCheck.ReadHead(root));
        var empty = workspace.PathFor(OtherDestination);
        var fresh = await preparation.ReviewAsync(empty, release.Evidence);
        Directory.CreateDirectory(empty);
        await File.WriteAllTextAsync(Path.Combine(empty, PreservedPath), PlayerEdit);
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.PrepareAsync(fresh, []));
        Assert.Equal(PlayerEdit, await File.ReadAllTextAsync(Path.Combine(empty, PreservedPath)));
    }

    /// <summary>
    /// Uses metadata review before the real combined game preparation and keeps unknown active-run state blocked.
    /// </summary>
    /// <param name="gitCheckout">Whether an ordinary official checkout is already present.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrackerReviewSelectsCombinedUpdateBeforePreparingPrograms(bool gitCheckout)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, gitCheckout);
        var preparation = new TrackerUpdatePreparation(release.Verifier, new ReleaseDownloadStore(release.Cache, release), SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release));
        var current = release.Request() with { GameCommit = game.Baseline.Commit };
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.ReviewAsync(current with { HasActiveRun = true }, release.Evidence));
        Assert.Empty(release.Requests);
        var review = await preparation.ReviewAsync(current, release.Evidence);
        Assert.Equal(game.Baseline.Commit, review.Authorization.PreviousGameCommit);
        Assert.Equal(game.Target, review.Authorization.Request.GameCommit);
        Assert.Equal(!gitCheckout, review.FirstPreparation);
        Assert.DoesNotContain(release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.PrepareAsync(review, [], true));
        var prepared = await preparation.PrepareAsync(review, [], false);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(release.Root, prepared.TransactionId)).Phase);
    }


    /// <summary>
    /// Rejects launcher metadata changes after preparation before touching a single program file.
    /// </summary>
    /// <returns>The completed concurrent-change check.</returns>
    [Fact]
    public async Task ConcurrentLauncherChangeBlocksApplication()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, true);
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        var before = await InstallationFileSnapshot.ReadAsync(release.Root);
        release.Write(LocalExclusion, Encoding.UTF8.GetBytes(PlayerEdit));
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => engine.ApplyAsync(release.Root, prepared.TransactionId));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(release.Root));
        Assert.Equal(PlayerEdit, await File.ReadAllTextAsync(Path.Combine(release.Root, LocalExclusion)));
    }

    /// <summary>
    /// Refuses a substituted recovery Git witness despite otherwise valid signed release evidence.
    /// </summary>
    /// <returns>The completed independent authentication check.</returns>
    [Fact]
    public async Task TamperedGitWitnessCannotAuthorizeReplacement()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, false);
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        var before = await InstallationFileSnapshot.ReadAsync(release.Root);
        release.Write(IronmonOnlyUpdate.EvidenceDirectory + '/' + prepared.TransactionId.ToString(TransactionStorage.GuidFormat) + WitnessHead, Encoding.UTF8.GetBytes(PlayerEdit));
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.ApplyAsync(release.Root, prepared.TransactionId));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(release.Root));
    }

    /// <summary>
    /// Requires exact-path consent before replacing a locally edited game file in an ordinary checkout.
    /// </summary>
    /// <returns>The completed conflict and consent fixture.</returns>
    [Fact]
    public async Task EditedGameFileRequiresReviewedReplacement()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, true);
        release.Write(ZipGameFixture.TextFile, Encoding.UTF8.GetBytes(PlayerEdit));
        var request = release.Request() with { GameCommit = game.Target };
        await Assert.ThrowsAsync<IronmonUpdateConflictException>(() => Coordinator(game, release).PrepareAsync(request, game.Baseline.Commit, release.Evidence));
        Assert.Equal(PlayerEdit, await File.ReadAllTextAsync(Path.Combine(release.Root, ZipGameFixture.TextFile)));
        var prepared = await Coordinator(game, release).PrepareAsync(request with { ApprovedPaths = [ZipGameFixture.TextFile] }, game.Baseline.Commit, release.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(release.Root, prepared.TransactionId)).Phase);
        Assert.Equal(ZipGameFixture.TextB, await File.ReadAllTextAsync(Path.Combine(release.Root, ZipGameFixture.TextFile)));
    }

    /// <summary>
    /// Recovers signed game and mod files with another process after termination between the Git directory renames.
    /// </summary>
    /// <param name="gitCheckout">Whether the original installation has Git metadata.</param>
    /// <returns>The completed interruption and recovery fixture.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshSignedHelperRecoversCombinedInterruption(bool gitCheckout)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, gitCheckout);
        release.Write(FixtureMarker, []);
        var before = await InstallationFileSnapshot.ReadAsync(release.Root);
        var originalGit = gitCheckout ? await TransactionStorage.TreeAsync(Path.Combine(release.Root, GitDirectory), CancellationToken.None) : null;
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        using (var child = StartHelper(game, release, prepared.TransactionId, ApplyMode))
        {
            try
            {
                Assert.Equal(Ready, await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)));
                Assert.True(File.Exists(Path.Combine(release.Root, FileManagementPolicy.BootstrapPath)));
            }
            finally
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);

                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
        }

        using var recovery = StartHelper(game, release, prepared.TransactionId, RecoverMode);
        try
        {
            var error = recovery.StandardError.ReadToEndAsync();
            var output = await recovery.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(60));
            await recovery.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(recovery.ExitCode == 0, await error);
            Assert.Contains(nameof(TransactionPhase.RolledBack), output, StringComparison.Ordinal);
        }
        finally
        {
            if (!recovery.HasExited)
                recovery.Kill(entireProcessTree: true);

            await recovery.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(release.Root));
        Assert.True(await CombinedGamePreparation.SameGitAsync(release.Root, originalGit, CancellationToken.None));
    }

    /// <summary>
    /// Preserves additional local branches while advancing only the installation release branch.
    /// </summary>
    /// <returns>The completed metadata preservation check.</returns>
    [Fact]
    public async Task LocalBranchesAreNotDiscarded()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, true);
        await game.RunAsync(release.Root, BranchCommand, ExtraBranch);
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(release.Root, prepared.TransactionId)).Phase);
        Assert.Equal(game.Baseline.Commit, await game.RunAsync(release.Root, Revision, ExtraBranch));
        Assert.Equal(game.Target, await game.RunAsync(release.Root, Revision, Head));
    }

    /// <summary>
    /// Launches an owned hidden fixture helper with its key and fixed upstream supplied independently of the journal.
    /// </summary>
    /// <param name="game">The disposable upstream.</param>
    /// <param name="release">The fixture authority and installation.</param>
    /// <param name="transaction">The prepared transaction.</param>
    /// <param name="mode">The fixture operation.</param>
    /// <returns>The exact process owned by the caller.</returns>
    private Process StartHelper(ZipGameFixture game, SignedReleaseFixture release, Guid transaction, string mode)
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, SignedReleaseFixture.HostRelativePath + SignedReleaseFixture.BuildConfiguration + SignedReleaseFixture.HostSuffix));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = release.Root };
        foreach (var argument in new[] { mode, release.Root, transaction.ToString(TransactionStorage.GuidFormat), release.PublicKey, tool.Cache.Root, game.Policy.Remote, Path.Combine(release.Cache, Home) })
            start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new IOException("The signed combined fixture host did not start.");
    }

    /// <summary>
    /// Updates game and Ironmon together, then proves the launcher can advance from the correct index to a later release.
    /// </summary>
    /// <param name="gitCheckout">Whether the installation begins with a shallow launcher checkout.</param>
    /// <param name="pull">Whether the subsequent launcher uses its older pull flow.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SignedCombinedUpdateAndFutureLauncherRemainCoherent(bool gitCheckout, bool pull)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, gitCheckout);
        release.Write(PreservedPath, Encoding.UTF8.GetBytes(PlayerEdit));
        var before = await InstallationFileSnapshot.ReadAsync(release.Root);
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(release.Root));
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), Verification(game, release)), _ => Task.CompletedTask, progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Operation == 0 && progress.Boundary == TransactionBoundary.Mutation)
            {
                Assert.True(File.Exists(Path.Combine(release.Root, FileManagementPolicy.BootstrapPath)));
                Assert.Equal(ZipGameFixture.TextA, File.ReadAllText(Path.Combine(release.Root, ZipGameFixture.TextFile)));
            }
        });
        var result = await engine.ApplyAsync(release.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Equal(game.Target, await game.RunAsync(release.Root, Revision, Head));
        Assert.DoesNotContain(ZipGameFixture.ObsoleteFile, await game.RunAsync(release.Root, ListFiles), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(release.Root, ZipGameFixture.ObsoleteFile)));
        Assert.Equal(SignedReleaseFixture.VersionB, await File.ReadAllTextAsync(Path.Combine(release.Root, SignedReleaseFixture.Script)));
        if (pull)
        {
            await game.RunAsync(release.Root, Pull, FastForward, Origin, Branch);
        }
        else
        {
            await game.RunAsync(release.Root, Fetch, Depth, Origin, Branch);
            await game.RunAsync(release.Root, Reset, Hard, RemoteRef);
        }

        Assert.Equal(await game.RunAsync(game.Upstream, Revision, Head), await game.RunAsync(release.Root, Revision, Head));
        Assert.False(File.Exists(Path.Combine(release.Root, ZipGameFixture.ObsoleteFile)));
        Assert.Equal(PlayerEdit, await File.ReadAllTextAsync(Path.Combine(release.Root, PreservedPath)));
        Assert.Equal(SignedReleaseFixture.VersionB, await File.ReadAllTextAsync(Path.Combine(release.Root, SignedReleaseFixture.Script)));
    }

    /// <summary>
    /// Rolls back actual game files, Ironmon, branch and index when final component verification fails.
    /// </summary>
    /// <param name="gitCheckout">Whether the original installation has Git metadata to restore.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalFailureRestoresEntireGameAndMod(bool gitCheckout)
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, gitCheckout);
        var before = await InstallationFileSnapshot.ReadAsync(release.Root);
        var originalGit = gitCheckout ? await TransactionStorage.TreeAsync(Path.Combine(release.Root, GitDirectory), CancellationToken.None) : null;
        var prepared = await Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(true), Verification(game, release)), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(release.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.RolledBack, result.Phase);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(release.Root));
        Assert.True(await CombinedGamePreparation.SameGitAsync(release.Root, originalGit, CancellationToken.None));
    }

    /// <summary>
    /// Rejects a checkout advanced by an external launcher instead of silently downgrading it.
    /// </summary>
    [Fact]
    public async Task NewerExternalCheckoutIsNeverDowngraded()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, true);
        await game.RunAsync(release.Root, Fetch, Origin, Branch);
        await game.RunAsync(release.Root, Reset, Hard, RemoteRef);
        var before = await InstallationFileSnapshot.ReadAsync(release.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target }, game.Baseline.Commit, release.Evidence));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(release.Root));
    }

    /// <summary>
    /// Requires ending an active run even when the publisher retains its generation profile.
    /// </summary>
    [Fact]
    public async Task ActiveRunBlocksGameReplacementBeforeDownloads()
    {
        using var game = new ZipGameFixture(tool);
        using var release = new SignedReleaseFixture();
        await game.InitializeAsync();
        await ConfigureAsync(game, release, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(game, release).PrepareAsync(release.Request() with { GameCommit = game.Target, HasActiveRun = true }, game.Baseline.Commit, release.Evidence));
        Assert.Empty(release.Requests);
    }

    /// <summary>
    /// Creates a production coordinator using only the test's fixed local upstream and download sources.
    /// </summary>
    /// <param name="game">The disposable Git history.</param>
    /// <param name="release">The independently signed release.</param>
    /// <returns>The combined workflow.</returns>
    private CombinedUpdate Coordinator(ZipGameFixture game, SignedReleaseFixture release)
        => new(release.Verifier, new ReleaseDownloadStore(release.Cache, release), SignedReleaseFixture.Runtime(), new CombinedGamePreparation(tool.Cache, game.Policy, game.Staging), Verification(game, release));

    /// <summary>
    /// Creates independent offline Git verification for fresh authority instances.
    /// </summary>
    /// <param name="game">The trusted fixture upstream.</param>
    /// <param name="release">The private release cache.</param>
    /// <returns>The fixed-policy Git verifier.</returns>
    private CombinedGitVerification Verification(ZipGameFixture game, SignedReleaseFixture release)
        => new(tool.Cache, game.Policy, Path.Combine(release.Cache, Home));

    /// <summary>
    /// Authenticates real Git file bytes and optionally creates a shallow original launcher checkout.
    /// </summary>
    /// <param name="game">The real A/B/C history.</param>
    /// <param name="release">The synthetic signed mod release.</param>
    /// <param name="gitCheckout">Whether to initialize a shallow checkout.</param>
    /// <returns>The completed installation fixture.</returns>
    private static async Task ConfigureAsync(ZipGameFixture game, SignedReleaseFixture release, bool gitCheckout)
    {
        var before = await InventoryAsync(game, game.Baseline.Commit);
        var target = await InventoryAsync(game, game.Target);
        release.UseGameHistory(before, target);
        foreach (var file in before.Files)
            release.Write(file.Path, await File.ReadAllBytesAsync(Path.Combine(game.Installation, file.Path)));

        if (!gitCheckout)
            return;

        await game.RunAsync(release.Root, Init, InitialBranch, EmptyTemplate);
        await game.RunAsync(release.Root, Remote, Add, Origin, game.Policy.Remote);
        await game.RunAsync(release.Root, Fetch, Depth, Origin, game.Baseline.Commit);
        await game.RunAsync(release.Root, Reset, Mixed, game.Baseline.Commit);
        await game.RunAsync(release.Root, Config, BranchRemote, Origin);
        await game.RunAsync(release.Root, Config, BranchMerge, Heads);
    }

    /// <summary>
    /// Produces a complete signed inventory input from exact fixture commit bytes.
    /// </summary>
    /// <param name="game">The real Git history.</param>
    /// <param name="commit">The exact fixture commit.</param>
    /// <returns>The complete game inventory.</returns>
    private static async Task<ReleaseFileInventory> InventoryAsync(ZipGameFixture game, string commit)
    {
        var paths = await game.RunAsync(game.Upstream, Tree, Recursive, Names, Null, commit);
        var files = new List<ReleaseFile>();
        foreach (var path in paths.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var content = await game.Git.RunAsync(game.Upstream, [Show, commit + ':' + path]);
            Assert.Equal(0, content.ExitCode);
            files.Add(SignedReleaseFixture.Entry(path, Encoding.UTF8.GetBytes(content.Output), ReleaseProtocol.GameScope));
        }

        return new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.GameScope, SignedReleaseFixture.VersionA, commit, null, [.. files]);
    }
}
