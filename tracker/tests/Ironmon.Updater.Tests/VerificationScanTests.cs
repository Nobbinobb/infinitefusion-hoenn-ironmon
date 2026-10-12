using System.Text;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies reduced complete inspections without accepting changed reviewed files or skipping recovery boundaries.
/// </summary>
public sealed class VerificationScanTests
{
    private const string GameExecutable = "InfiniteFusion2.exe";
    private const string GameConfiguration = "Game.ini";
    private const string Configuration = "[Game]\nTitle=infinitefusion-hoenn\n";
    private const string GitHead = ".git/HEAD";
    private const string GitCache = "unused-git";
    private const string Staging = "unused-staging";
    private const string Home = "unused-home";
    private const string AddedFile = "extra.bin";

    /// <summary>
    /// Completes the real same-process Setup workflow with one review and three fresh transaction inspections.
    /// </summary>
    [Fact]
    public async Task SetupUsesFourCompleteScansWithDistinctVerificationStages()
    {
        using var fixture = new TrackerUpdateTestFixture();
        var preparation = Preparation(fixture);
        var progress = new List<InstallationProgress>();
        using var scope = new InstallationProgressScope(progress.Add);
        var review = await preparation.ReviewAsync(fixture.Release.Root, fixture.Release.Evidence, noActiveRun: true);
        var prepared = await preparation.PrepareAsync(review, []);
        var authority = new SignedIronmonAuthority(fixture.Release.Verifier, SignedReleaseFixture.Runtime());
        var engine = new UpdateTransaction(authority, _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(prepared.InstallationRoot, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Equal(4, progress.Count(value => value.Stage == InstallationStage.EnumeratingFiles && value.Completed == 0));
        InstallationStage[] stages = [InstallationStage.ReviewingInstallation, InstallationStage.CheckingPreparedInstallation, InstallationStage.CheckingInstallation, InstallationStage.VerifyingInstallation];
        Assert.Equal(stages, progress.Where(value => stages.Contains(value.Stage) && value.Completed == 0).Select(value => value.Stage));
        Assert.Contains(progress, value => value.Stage == InstallationStage.VerifyingRecovery && value.Completed == value.Total && value.Total > 0);
        Assert.Contains(progress, value => value.Stage == InstallationStage.VerifyingComponents && value.Completed == value.Total && value.Total > 0);
        Assert.Equal(stages.Length, stages.Select(stage => new InstallationProgress(stage).Text).Distinct().Count());
    }

    /// <summary>
    /// Rejects protected, retained and newly added files changed after review while staging only recovery data.
    /// </summary>
    /// <param name="path">The preserved or newly created installation file.</param>
    [Theory]
    [InlineData(SignedReleaseFixture.Save)]
    [InlineData(SignedReleaseFixture.Profile)]
    [InlineData(AddedFile)]
    public async Task ReviewReuseRejectsChangesDuringDownloads(string path)
    {
        using var fixture = new TrackerUpdateTestFixture();
        var preparation = Preparation(fixture);
        var review = await preparation.ReviewAsync(fixture.Release.Root, fixture.Release.Evidence, noActiveRun: true);
        var original = File.ReadAllBytes(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script));
        fixture.DuringArchive = () => fixture.Release.Write(path, [10]);
        await Assert.ThrowsAsync<IOException>(() => preparation.PrepareAsync(review, []));
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script)));
        Assert.Equal(new byte[] { 10 }, File.ReadAllBytes(Path.Combine(fixture.Release.Root, path)));
    }

    /// <summary>
    /// Detects mutations made while backups are staged before any prepared marker or application intent is published.
    /// </summary>
    [Fact]
    public async Task ChangesDuringBackupPreparationCannotPublishAReadyTransaction()
    {
        using var fixture = new TransactionFixture();
        var changed = false;
        using var scope = new InstallationProgressScope(value =>
        {
            if (!changed && value.Stage == InstallationStage.PreparingRecovery && value.Total > 0 && value.Completed == value.Total)
            {
                TransactionFixture.Write(fixture.Root, TransactionFixture.SaveFile, 9);
                changed = true;
            }
        });
        await Assert.ThrowsAsync<IOException>(() => fixture.PrepareAsync());
        Assert.True(changed);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Root));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.ChangedFile)));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(fixture.Root, TransactionFixture.SaveFile)));
    }

    /// <summary>
    /// Keeps independent signed game verification after downloads even when planning reuses the review snapshot.
    /// </summary>
    [Fact]
    public async Task ReviewReuseStillRejectsGameChangesDuringDownloads()
    {
        using var fixture = new TrackerUpdateTestFixture();
        var preparation = Preparation(fixture);
        var review = await preparation.ReviewAsync(fixture.Release.Root, fixture.Release.Evidence, noActiveRun: true);
        var original = File.ReadAllBytes(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script));
        fixture.DuringArchive = () => fixture.Release.Write(SignedReleaseFixture.Game, [10]);
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.PrepareAsync(review, []));
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script)));
    }

    /// <summary>
    /// Keeps the independent preparation entry point's own snapshot when no same-process review is supplied.
    /// </summary>
    [Fact]
    public async Task IndependentPreparationCapturesItsOwnFiles()
    {
        using var fixture = new TrackerUpdateTestFixture();
        var preparation = Preparation(fixture);
        var review = await preparation.ReviewAsync(fixture.Release.Root, fixture.Release.Evidence, noActiveRun: true);
        var progress = new List<InstallationProgress>();
        using var scope = new InstallationProgressScope(progress.Add);
        var update = new IronmonOnlyUpdate(fixture.Release.Verifier, new ReleaseDownloadStore(fixture.Release.Cache, fixture), SignedReleaseFixture.Runtime());
        await update.PrepareInstallationAsync(review.Authorization.Request, review.Authorization.Target, review.Authorization.Current, review.Authorization.Purpose);
        Assert.Equal(2, progress.Count(value => value.Stage == InstallationStage.EnumeratingFiles && value.Completed == 0));
        Assert.Contains(progress, value => value.Stage == InstallationStage.ReviewingInstallation);
        Assert.Contains(progress, value => value.Stage == InstallationStage.CheckingPreparedInstallation);
    }

    /// <summary>
    /// Composes production Setup preparation around disposable signed files and fixture-only version detection.
    /// </summary>
    /// <param name="fixture">The isolated release and download source.</param>
    /// <returns>The real preparation workflow.</returns>
    private static SetupPreparation Preparation(TrackerUpdateTestFixture fixture)
    {
        var release = fixture.Release;
        release.Write(GameExecutable, [77, 90, 0, 0]);
        release.Write(GameConfiguration, Encoding.UTF8.GetBytes(Configuration));
        release.Write(GitHead, Encoding.UTF8.GetBytes(SignedReleaseFixture.Commit));
        var downloads = new ReleaseDownloadStore(release.Cache, fixture);
        var cache = new MinGitCache(Path.Combine(release.Cache, GitCache), fixture);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        var game = new CombinedGamePreparation(cache, policy, Path.Combine(release.Cache, Staging));
        var verification = new CombinedGitVerification(cache, policy, Path.Combine(release.Cache, Home));
        return new SetupPreparation(release.Verifier, downloads, SignedReleaseFixture.Runtime(), game, verification, _ => SignedReleaseFixture.VersionA);
    }
}
