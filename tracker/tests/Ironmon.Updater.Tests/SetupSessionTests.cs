using System.Net;
using Ironmon.Setup.Core;
using Ironmon.Tracker.Connection.Sprites;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies real Setup transactions and optional sprite resumption without touching desktop processes or preferences.
/// </summary>
public sealed class SetupSessionTests
{
    private const string GameExe = "InfiniteFusion2.exe";
    private const string GameIni = "Game.ini";
    private const string Ini = "[Game]\nTitle=infinitefusion-hoenn\n";
    private const string GitHead = ".git/HEAD";
    private const string CustomManifest = "Data/sprites/CUSTOM_SPRITES";
    private const string BaseManifest = "Data/sprites/BASE_SPRITES";
    private const string CustomEntries = "1.2.png\n2.3.png\n";
    private const string GitDirectory = "unused-git";
    private const string Staging = "unused-staging";
    private const string Home = "unused-home";
    private const string FirstSheet = "/1.png";
    private const string Tag = "\"fixture-sheet\"";
    private const string ZipExtension = ".zip";
    private const string NewerEngine = "99.0.0";
    private const string NewerHelper = "Ironmon-Updater-v99.0.0.zip";
    private const string NewInstallation = "new";

    /// <summary>
    /// Keeps optional downloads opt-in and retries a failed shortcut without reinstalling verified core files.
    /// </summary>
    [Fact]
    public async Task CoreCommitIsIndependentOfOptionalShortcutFailure()
    {
        using var fixture = new TrackerUpdateTestFixture();
        PrepareGame(fixture);
        using var handler = new Sheets();
        using var http = new HttpClient(handler);
        using var sprites = new CustomSpriteSheetInstaller(http, _ => false, 1);
        var platform = new Platform { FailShortcut = true };
        using var session = Create(fixture, sprites, platform);
        await session.ReviewAsync(fixture.Release.Root, null, true);
        Assert.Null(session.Error);
        Assert.True(session.CanInstall);
        Assert.Equal(0, handler.Requests);
        await session.InstallAsync(false, true, false);
        Assert.True(session.CoreInstalled, session.Error);
        Assert.True(session.OptionalIncomplete);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(1, platform.Closures);
        platform.FailShortcut = false;
        await session.RetryOptionalAsync();
        Assert.False(session.OptionalIncomplete);
        Assert.Null(session.Error);
        Assert.Equal(1, platform.Closures);
        await session.OpenTrackerAsync();
        Assert.Equal(1, platform.Launches);
    }

    /// <summary>
    /// Requires explicit prerequisite consent before downloading packages or closing any application.
    /// </summary>
    [Fact]
    public async Task MissingPrerequisiteDoesNotInstallWithoutConsent()
    {
        using var fixture = new TrackerUpdateTestFixture();
        PrepareGame(fixture);
        using var handler = new Sheets();
        using var http = new HttpClient(handler);
        using var sprites = new CustomSpriteSheetInstaller(http, _ => false, 1);
        var platform = new Platform { WebViewAvailable = false };
        using var session = Create(fixture, sprites, platform);
        await session.ReviewAsync(fixture.Release.Root, null, true);
        await session.InstallAsync(false, false, false);
        Assert.False(session.CoreInstalled);
        Assert.Equal(0, platform.PrerequisiteInstalls);
        Assert.Equal(0, platform.Closures);
        Assert.DoesNotContain(fixture.Release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
        await session.ReviewAsync(fixture.Release.Root, null, true);
        await session.InstallAsync(false, false, true);
        Assert.True(session.CoreInstalled, session.Error);
        Assert.Equal(1, platform.PrerequisiteInstalls);
    }

    /// <summary>
    /// Preserves the detected package even if a caller submits a different choice for an existing installation.
    /// </summary>
    /// <param name="included">Whether the existing tracker includes its runtime.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingInstallationAlwaysRetainsItsPackage(bool included)
    {
        using var fixture = new TrackerUpdateTestFixture(included ? ReleaseProtocol.SelfContained : ReleaseProtocol.RuntimeRequired);
        PrepareGame(fixture);
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = Create(fixture, sprites, new Platform());
        await session.SelectDestinationAsync(fixture.Release.Root);
        var expected = included ? ReleaseProtocol.SelfContained : ReleaseProtocol.RuntimeRequired;
        Assert.Equal(expected, session.Destination?.InstalledFlavor);
        Assert.Empty(fixture.Release.Requests);
        await session.ReviewAsync(fixture.Release.Root, included ? ReleaseProtocol.RuntimeRequired : ReleaseProtocol.SelfContained, true);
        Assert.Null(session.Error);
        Assert.Equal(expected, session.Review?.Authorization.Request.Flavor);
    }

    /// <summary>
    /// Shows run confirmation only when signed compatibility requires it and never permits installation before consent.
    /// </summary>
    /// <param name="requiresFinishedRun">Whether the release forbids active runs.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunConfirmationIsConditionalAndRequiredBeforePreparation(bool requiresFinishedRun)
    {
        using var fixture = new TrackerUpdateTestFixture();
        PrepareGame(fixture);
        if (requiresFinishedRun)
            fixture.Release.RequireCompletedRun();

        using var sprites = new CustomSpriteSheetInstaller();
        using var session = Create(fixture, sprites, new Platform());
        await session.ReviewAsync(fixture.Release.Root, null, false);
        Assert.Equal(requiresFinishedRun, session.NeedsRunConfirmation);
        Assert.Equal(!requiresFinishedRun, session.CanInstall);
        Assert.DoesNotContain(fixture.Release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
        if (requiresFinishedRun)
        {
            Assert.Null(session.Review);
            await session.ReviewAsync(fixture.Release.Root, null, true);
            Assert.Null(session.Error);
            Assert.False(session.NeedsRunConfirmation);
            Assert.True(session.CanInstall);
            Assert.False(session.Review!.Authorization.Request.HasActiveRun);
        }
    }

    /// <summary>
    /// Keeps local folder selection usable while offline, and clears failed network status when returning to choices.
    /// </summary>
    [Fact]
    public async Task FailedReleaseReviewDoesNotLeaveCheckingStatusOnOptions()
    {
        using var fixture = new TrackerUpdateTestFixture { Offline = true };
        using var workspace = new TestWorkspace();
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = Create(fixture, sprites, new Platform());
        var destination = workspace.PathFor(NewInstallation);
        await session.SelectDestinationAsync(destination);
        Assert.Null(session.Error);
        Assert.Null(session.Destination?.InstalledFlavor);
        Assert.False(Directory.Exists(destination));
        await session.ReviewAsync(destination, ReleaseProtocol.SelfContained, false);
        Assert.Equal(UpdaterText.ReleaseDiscoveryNoPubliclyAccessibleIronmonReleaseWasFoundCheckThat, session.Error);
        Assert.DoesNotContain("Checking", session.Status);
        Assert.False(session.NeedsRunConfirmation);
        Assert.False(session.CanInstall);
        Assert.False(Directory.Exists(destination));
        session.ClearReview();
        Assert.Null(session.Error);
        Assert.Null(session.Review);
        Assert.DoesNotContain("Checking", session.Status);
    }

    /// <summary>
    /// Rejects an unrelated nonempty folder during local selection without contacting the release service.
    /// </summary>
    [Fact]
    public async Task UnrelatedFolderCannotAdvanceToInstallationOptions()
    {
        using var fixture = new TrackerUpdateTestFixture();
        using var workspace = new TestWorkspace();
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = Create(fixture, sprites, new Platform());
        var destination = workspace.PathFor(NewInstallation);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, GameIni), Ini);
        await session.SelectDestinationAsync(destination);
        Assert.NotNull(session.Error);
        Assert.Null(session.Destination);
        Assert.Empty(fixture.Release.Requests);
        Assert.Single(Directory.GetFiles(destination));
    }

    /// <summary>
    /// Refuses a newer engine requirement before creating an installation or downloading any release asset.
    /// </summary>
    [Fact]
    public async Task OldSetupRequestsCurrentInstallerBeforeWritingFiles()
    {
        using var release = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var cache = new MinGitCache(release.Cache, release);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        var game = new CombinedGamePreparation(cache, policy, Path.Combine(release.Cache, Staging));
        var verification = new CombinedGitVerification(cache, policy, Path.Combine(release.Cache, Home));
        var preparation = new SetupPreparation(release.Verifier, new ReleaseDownloadStore(release.Cache, release), SignedReleaseFixture.Runtime(), game, verification);
        var assets = release.Manifest.Assets.Select(asset => asset.Role == ReleaseProtocol.UpdaterRole ? asset with { Name = NewerHelper, Url = ReleaseProtocol.AssetUrl(release.Manifest.IronmonVersion, NewerHelper) } : asset);
        var evidence = release.Sign(release.Manifest with { MinimumEngineVersion = NewerEngine, Assets = [.. assets] }, release.Evidence.Inventories);
        var root = workspace.PathFor(NewInstallation);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.ReviewAsync(root, evidence));
        Assert.Contains("newer Ironmon Setup", error.Message);
        Assert.False(Directory.Exists(root));
        Assert.Empty(release.Requests);
    }

    /// <summary>
    /// Rejects paths that cannot hold durable private Git evidence without creating their directories.
    /// </summary>
    [Fact]
    public void LongGamePathsAreRejectedBeforeCreatingRecoveryState()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(new string('a', 200));
        Assert.Throws<IOException>(() => CombinedGitVerification.EnsureSupportedRoot(root));
        Assert.False(Directory.Exists(root));
    }

    /// <summary>
    /// Checks writer contention before downloading program archives or closing applications.
    /// </summary>
    [Fact]
    public async Task BusyInstallationFailsBeforeProgramDownload()
    {
        using var fixture = new TrackerUpdateTestFixture();
        PrepareGame(fixture);
        using var handler = new Sheets();
        using var http = new HttpClient(handler);
        using var sprites = new CustomSpriteSheetInstaller(http, _ => false, 1);
        var platform = new Platform();
        using var session = Create(fixture, sprites, platform);
        await session.ReviewAsync(fixture.Release.Root, null, true);
        Assert.Null(session.Error);
        using var lease = InstallationLease.Acquire(fixture.Release.Root);
        await session.InstallAsync(false, false, false);
        Assert.NotNull(session.Error);
        Assert.False(session.CoreInstalled);
        Assert.Equal(0, platform.Closures);
        Assert.DoesNotContain(fixture.Release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
    }

    /// <summary>
    /// Preserves core success and completed sheets when Setup is cancelled and discarded, then resumes through the tracker's shared service.
    /// </summary>
    [Fact]
    public async Task DiscardedSetupLeavesSpriteProgressForTheTracker()
    {
        using var fixture = new TrackerUpdateTestFixture();
        PrepareGame(fixture);
        using var handler = new Sheets { PauseSecond = true };
        using var http = new HttpClient(handler);
        using var sprites = new CustomSpriteSheetInstaller(http, _ => false, 1);
        using (var session = Create(fixture, sprites, new Platform()))
        {
            await session.ReviewAsync(fixture.Release.Root, null, true);
            Assert.Null(session.Error);
            var install = session.InstallAsync(true, false, false);
            await handler.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            session.Cancel();
            await install.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(session.CoreInstalled, session.Error);
            Assert.True(session.OptionalIncomplete);
            Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        }

        handler.PauseSecond = false;
        using var trackerSprites = new CustomSpriteSheetInstaller(http, _ => false, 1);
        var plan = trackerSprites.CreatePlan(fixture.Release.Root);
        Assert.Equal(1, plan.ExistingSheetCount);
        var result = await trackerSprites.InstallAsync(plan);
        Assert.Equal(1, result.DownloadedSheetCount);
        Assert.Equal(1, result.UnchangedSheetCount);
        Assert.Equal(0, result.FailedSheetCount);
    }

    /// <summary>
    /// Composes the real session with fixture-only runtime and process boundaries.
    /// </summary>
    /// <param name="fixture">The signed release and discovery fixture.</param>
    /// <param name="sprites">The shared real sprite service.</param>
    /// <param name="platform">The recorded platform effects.</param>
    /// <returns>The production Setup session.</returns>
    private static SetupSession Create(TrackerUpdateTestFixture fixture, CustomSpriteSheetInstaller sprites, Platform platform)
    {
        var release = fixture.Release;
        var downloads = new ReleaseDownloadStore(release.Cache, fixture);
        var cache = new MinGitCache(Path.Combine(release.Cache, GitDirectory), fixture);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        var game = new CombinedGamePreparation(cache, policy, Path.Combine(release.Cache, Staging));
        var verification = new CombinedGitVerification(cache, policy, Path.Combine(release.Cache, Home));
        var preparation = new SetupPreparation(release.Verifier, downloads, SignedReleaseFixture.Runtime(), game, verification, _ => SignedReleaseFixture.VersionA);
        return new SetupSession(fixture.Discovery, preparation, (_, progress) => new UpdateTransaction(new SignedIronmonAuthority(release.Verifier, SignedReleaseFixture.Runtime(), verification), _ => Task.CompletedTask, progress), sprites, platform, downloads);
    }

    /// <summary>
    /// Supplies ordinary detection metadata and two optional sprite entries in the disposable game only.
    /// </summary>
    /// <param name="fixture">The owned release fixture.</param>
    private static void PrepareGame(TrackerUpdateTestFixture fixture)
    {
        fixture.Release.Write(GameExe, [77, 90, 0, 0]);
        fixture.Release.Write(GameIni, System.Text.Encoding.UTF8.GetBytes(Ini));
        fixture.Release.Write(GitHead, System.Text.Encoding.UTF8.GetBytes(SignedReleaseFixture.Commit));
        fixture.Release.Write(CustomManifest, System.Text.Encoding.UTF8.GetBytes(CustomEntries));
        fixture.Release.Write(BaseManifest, []);
    }

    /// <summary>
    /// Records explicit platform actions without closing or launching any user application.
    /// </summary>
    private sealed class Platform : ISetupPlatform
    {
        /// <summary>
        /// Gets or sets the isolated runtime observation.
        /// </summary>
        public bool WebViewAvailable { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the shortcut action fails.
        /// </summary>
        internal bool FailShortcut { get; set; }

        /// <summary>
        /// Gets the recorded game and tracker closure count.
        /// </summary>
        internal int Closures { get; private set; }

        /// <summary>
        /// Gets the recorded prerequisite installation count.
        /// </summary>
        internal int PrerequisiteInstalls { get; private set; }

        /// <summary>
        /// Gets the recorded tracker launch count.
        /// </summary>
        internal int Launches { get; private set; }

        /// <summary>
        /// Accepts the fixture's supported Windows platform.
        /// </summary>
        public void EnsureSupported() { }

        /// <summary>
        /// Records the consented prerequisite installation and changes the independent detection result.
        /// </summary>
        /// <param name="cancellationToken">The operation token.</param>
        /// <returns>The completed fixture action.</returns>
        public Task InstallWebViewAsync(CancellationToken cancellationToken)
        {
            PrerequisiteInstalls++;
            WebViewAvailable = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Records closure without operating any native process.
        /// </summary>
        /// <param name="root">The disposable installation.</param>
        /// <param name="cancellationToken">The operation token.</param>
        /// <returns>The completed fixture action.</returns>
        public Task CloseInstallationAsync(string root, CancellationToken cancellationToken)
        {
            Closures++;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Simulates a separate optional shortcut failure.
        /// </summary>
        /// <param name="root">The disposable installation.</param>
        public void CreateShortcut(string root)
        {
            if (FailShortcut)
                throw new IOException("A different shortcut already exists.");
        }

        /// <summary>
        /// Records a launch after core success.
        /// </summary>
        /// <param name="root">The installed fixture root.</param>
        public void OpenTracker(string root)
            => Launches++;
    }

    /// <summary>
    /// Supplies deterministic conditional PNG responses and a cancellable second-sheet boundary.
    /// </summary>
    private sealed class Sheets : HttpMessageHandler
    {
        /// <summary>
        /// Gets or sets whether the second sheet remains pending until cancellation.
        /// </summary>
        internal bool PauseSecond { get; set; }

        /// <summary>
        /// Gets the second download boundary signal.
        /// </summary>
        internal TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets the total request count.
        /// </summary>
        internal int Requests { get; private set; }

        /// <summary>
        /// Responds using the same conditional HTTP semantics consumed by the production sprite service.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The request token.</param>
        /// <returns>The controlled sprite response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (PauseSecond && !request.RequestUri!.AbsolutePath.EndsWith(FirstSheet, StringComparison.Ordinal))
            {
                SecondStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var response = request.Headers.IfNoneMatch.Count > 0 ? new HttpResponseMessage(HttpStatusCode.NotModified) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0]) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Tag);
            return response;
        }
    }
}
