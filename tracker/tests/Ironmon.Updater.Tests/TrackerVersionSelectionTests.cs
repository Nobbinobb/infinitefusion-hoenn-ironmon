using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises direct latest updates, explicit intermediate targets and fresh checks after an actual fixture update.
/// </summary>
public sealed class TrackerVersionSelectionTests
{
    private const string _zipExtension = ".zip";
    private const string _localEdit = "A player-approved local script edit.";

    /// <summary>
    /// Installs C directly over A without downloading B or requiring an adjacent-version transaction.
    /// </summary>
    [Fact]
    public async Task LatestDefaultSkipsTheIntermediateRelease()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        await fixture.Session.OpenAsync();
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.AvailableVersion);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.LatestVersion);
        Assert.Null(fixture.Session.SelectedReleaseVersion);
        await fixture.Session.UpdateAsync();
        Assert.Null(fixture.Session.Error);
        var prepared = Assert.IsType<PreparedIronmonUpdate>(fixture.Handoff);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Release.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(fixture.Release.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.NotNull(fixture.Preparation.ReadCurrentRelease(fixture.Release.Root, SignedReleaseFixture.VersionC));
        Assert.DoesNotContain(fixture.CatalogHistory.Downloads, uri => uri.AbsoluteUri.Contains(ReleaseProtocol.DownloadSegment + SignedReleaseFixture.VersionB, StringComparison.Ordinal));
    }

    /// <summary>
    /// Installs exactly the selected B payload over A while C remains the default authenticated latest release.
    /// </summary>
    [Fact]
    public async Task ExplicitIntermediateTargetInstallsTheReviewedVersion()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        await fixture.Session.OpenAsync();
        await fixture.Session.LoadReleaseChoicesAsync();
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionB);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        await fixture.Session.UpdateAsync();
        Assert.Null(fixture.Session.Error);
        var prepared = Assert.IsType<PreparedIronmonUpdate>(fixture.Handoff);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Release.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(fixture.Release.Root, prepared.TransactionId)).Phase);
        Assert.NotNull(fixture.Preparation.ReadCurrentRelease(fixture.Release.Root, SignedReleaseFixture.VersionB));
        Assert.DoesNotContain(fixture.CatalogHistory.Downloads, uri => uri.AbsolutePath.EndsWith(_zipExtension, StringComparison.Ordinal) && uri.AbsoluteUri.Contains(ReleaseProtocol.DownloadSegment + SignedReleaseFixture.VersionC, StringComparison.Ordinal));
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.LatestVersion);
    }

    /// <summary>
    /// Discards file consent on target changes while preserving the authenticated latest-release watermark.
    /// </summary>
    [Fact]
    public async Task ExplicitIntermediateSelectionDoesNotReplaceLatestOrReuseConsent()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script), _localEdit);
        await fixture.Session.OpenAsync();
        fixture.Session.Approve(SignedReleaseFixture.Script, true);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        await fixture.Session.LoadReleaseChoicesAsync();
        Assert.False(fixture.Session.IsApproved(SignedReleaseFixture.Script));
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionB);
        Assert.Null(fixture.Session.Error);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.AvailableVersion);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.Review!.Manifest.IronmonVersion);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.LatestVersion);
        Assert.False(fixture.Session.IsApproved(SignedReleaseFixture.Script));
        var latest = await fixture.CatalogHistory.Discovery.CheckAsync();
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Release.Verifier.Verify(latest.Release!.Manifest, latest.Release.Signatures).IronmonVersion);
        fixture.Session.Approve(SignedReleaseFixture.Script, true);
        await fixture.Session.SelectReleaseVersionAsync(null);
        Assert.Null(fixture.Session.SelectedReleaseVersion);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.Review!.Manifest.IronmonVersion);
        Assert.False(fixture.Session.IsApproved(SignedReleaseFixture.Script));
    }

    /// <summary>
    /// Excludes installed and older versions from the advanced list and prevents a programmatic downgrade selection.
    /// </summary>
    [Fact]
    public async Task AdvancedChoicesContainOnlyForwardTargets()
    {
        using var fixture = new TrackerUpdateTestFixture { ObservedVersion = SignedReleaseFixture.VersionB };
        await fixture.EnableHistoryAsync();
        await fixture.Session.StartAsync();
        await fixture.Session.LoadReleaseChoicesAsync();
        Assert.Equal(SignedReleaseFixture.VersionC, Assert.Single(fixture.Session.ReleaseChoices).Version);
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionB);
        Assert.NotNull(fixture.Session.Error);
        Assert.Null(fixture.Session.SelectedReleaseVersion);
        Assert.Empty(fixture.CatalogHistory.Downloads);
        Assert.Null(fixture.Handoff);
    }

    /// <summary>
    /// Disables signed releases with unavailable downloads before they can be selected.
    /// </summary>
    [Fact]
    public async Task MissingDownloadsDisableIntermediateChoices()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        await fixture.Session.StartAsync();
        fixture.CatalogHistory.DownloadsAvailable = false;
        await fixture.Session.LoadReleaseChoicesAsync();
        Assert.All(fixture.Session.ReleaseChoices, choice => Assert.False(choice.CanInstall));
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionB);
        Assert.NotNull(fixture.Session.Error);
        Assert.Null(fixture.Session.SelectedReleaseVersion);
        Assert.Null(fixture.Handoff);
    }

    /// <summary>
    /// A disappeared exact target stops before staging or handoff and requires an explicit decision to return to latest.
    /// </summary>
    [Fact]
    public async Task DisappearedIntermediateTargetNeverFallsBackDuringUpdate()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        await fixture.Session.OpenAsync();
        await fixture.Session.LoadReleaseChoicesAsync();
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionB);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        fixture.CatalogHistory.HistoricalReleasePublished = false;
        await fixture.Session.UpdateAsync();
        Assert.NotNull(fixture.Session.Error);
        Assert.Null(fixture.Session.Review);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.SelectedReleaseVersion);
        Assert.Null(fixture.Handoff);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.DoesNotContain(fixture.CatalogHistory.Downloads, uri => uri.AbsolutePath.EndsWith(_zipExtension, StringComparison.Ordinal));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
        await fixture.Session.SelectReleaseVersionAsync(null);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.AvailableVersion);
    }

    /// <summary>
    /// New authenticated bytes for an already reviewed version cannot inherit consent or start a transaction.
    /// </summary>
    [Fact]
    public async Task ChangedIntermediateManifestRequiresANewReview()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        await fixture.Session.OpenAsync();
        await fixture.Session.LoadReleaseChoicesAsync();
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionB);
        var reviewed = fixture.Session.Review!.Manifest;
        fixture.CatalogHistory.ReplaceManifest(reviewed with { ReleaseSequence = reviewed.ReleaseSequence + 1 });
        await fixture.Session.UpdateAsync();
        Assert.Equal(UpdaterText.SetupVersionsReleaseChanged, fixture.Session.Error);
        Assert.Null(fixture.Session.Review);
        Assert.Null(fixture.Handoff);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.DoesNotContain(fixture.CatalogHistory.Downloads, uri => uri.AbsolutePath.EndsWith(_zipExtension, StringComparison.Ordinal));
        fixture.Session.Close();
        await fixture.Session.OpenAsync();
        Assert.Null(fixture.Session.Review);
        Assert.Null(fixture.Session.AvailableVersion);
        await fixture.Session.LoadReleaseChoicesAsync();
        Assert.Null(fixture.Session.Error);
        Assert.Equal(reviewed.ReleaseSequence + 1, fixture.Session.Review!.Manifest.ReleaseSequence);
    }

    /// <summary>
    /// A relaunched B installation discovers newly published C despite the fresh B cache from its previous process.
    /// </summary>
    [Fact]
    public async Task RelaunchedTrackerChecksForANewerReleaseImmediately()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.Session.OpenAsync();
        await fixture.Session.UpdateAsync();
        var prepared = Assert.IsType<PreparedIronmonUpdate>(fixture.Handoff);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Release.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(fixture.Release.Root, prepared.TransactionId)).Phase);
        fixture.Release.AdvanceVersion();
        fixture.ObservedVersion = SignedReleaseFixture.VersionB;
        fixture.RestartSession();
        await fixture.Session.StartAsync();
        Assert.Null(fixture.Session.Error);
        Assert.True(fixture.Session.ShowStartupPrompt);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.CurrentVersion);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.AvailableVersion);
    }
}
