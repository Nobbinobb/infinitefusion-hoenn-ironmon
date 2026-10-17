using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises signed historical selection, unavailable upstream inputs and separation from automatic update watermarks.
/// </summary>
public sealed class SetupReleaseCatalogTests
{
    private const string _unsupportedEngineVersion = "1.0.1";
    /// <summary>
    /// Lists genuine signed releases in numeric order with exact game mappings and disables unsigned legacy downloads.
    /// </summary>
    [Fact]
    public async Task SignedChoicesMapExactGameWithoutDownloadingPackages()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        var choices = await fixture.Catalog.ListAsync(CancellationToken.None);
        Assert.Equal(new[] { SignedReleaseFixture.VersionC, SignedReleaseFixture.VersionB, SetupReleaseCatalogFixture.LegacyVersion }, choices.Select(choice => choice.Version));
        Assert.All(choices.Where(choice => choice.CanInstall), choice => Assert.Equal(SignedReleaseFixture.Commit, choice.GameCommit));
        Assert.Equal(2, choices.Count(choice => choice.CanInstall));
        Assert.Equal(UpdaterText.SetupVersionsMissingMetadata, choices[^1].UnavailableReason);
        Assert.Empty(fixture.Downloads);
        Assert.Single(fixture.Requests, uri => uri.AbsoluteUri.StartsWith(ReleaseProtocol.GameCompareUrl, StringComparison.Ordinal));
    }

    /// <summary>
    /// Disables signed releases whose required upstream history, archive or signed payload sizes cannot be verified.
    /// </summary>
    /// <param name="missingHistory">Whether upstream no longer resolves the signed game.</param>
    /// <param name="missingArchive">Whether only the archive endpoint disappeared.</param>
    /// <param name="wrongIdentity">Whether upstream returned another game identity.</param>
    /// <param name="missingDownloads">Whether an Ironmon payload disappeared.</param>
    /// <param name="wrongLength">Whether headers disagree with a signed payload size.</param>
    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public async Task UnavailableInputsDisableChoices(bool missingHistory, bool missingArchive, bool wrongIdentity, bool missingDownloads, bool wrongLength)
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        fixture.GameAvailable = !missingHistory;
        fixture.GameArchiveAvailable = !missingArchive;
        fixture.WrongGameIdentity = wrongIdentity;
        fixture.DownloadsAvailable = !missingDownloads;
        fixture.CorrectDownloadLengths = !wrongLength;
        var choices = await fixture.Catalog.ListAsync(CancellationToken.None);
        Assert.All(choices, choice => Assert.False(choice.CanInstall));
        Assert.Equal(missingHistory || missingArchive || wrongIdentity ? UpdaterText.SetupVersionsGameUnavailable : UpdaterText.SetupVersionsDownloadsUnavailable, choices[0].UnavailableReason);
    }

    /// <summary>
    /// Refuses unverified signatures even when the public release entry and game endpoint are available.
    /// </summary>
    [Fact]
    public async Task InvalidSignatureNeverEnablesAnOldRelease()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        fixture.ValidSignatures = false;
        Assert.All(await fixture.Catalog.ListAsync(CancellationToken.None), choice => Assert.False(choice.CanInstall));
        Assert.DoesNotContain(fixture.Requests, uri => uri.AbsoluteUri.StartsWith(ReleaseProtocol.GameCompareUrl, StringComparison.Ordinal));
    }

    /// <summary>
    /// Keeps explicit older installation separate from the authenticated newest-release observation.
    /// </summary>
    [Fact]
    public async Task ExplicitTargetDoesNotRollBackLatestDiscovery()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        var latest = await fixture.Discovery.CheckAsync(true);
        Assert.Equal(SignedReleaseFixture.VersionC, release.Verifier.Verify(latest.Release!.Manifest, latest.Release.Signatures).IronmonVersion);
        var selected = await fixture.Catalog.ReadAsync(SignedReleaseFixture.VersionB, CancellationToken.None);
        Assert.Equal(SignedReleaseFixture.VersionB, release.Verifier.Verify(selected.Manifest, selected.Signatures).IronmonVersion);
        var retained = await fixture.Discovery.CheckAsync();
        Assert.Equal(latest.Release.Manifest, retained.Release!.Manifest);
    }

    /// <summary>
    /// Rechecks availability after enumeration and never silently replaces the requested version with latest.
    /// </summary>
    [Fact]
    public async Task DisappearedGameInvalidatesPreviouslyAvailableSelection()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        Assert.Contains(await fixture.Catalog.ListAsync(CancellationToken.None), choice => choice.Version == SignedReleaseFixture.VersionB && choice.CanInstall);
        fixture.GameAvailable = false;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Catalog.ReadAsync(SignedReleaseFixture.VersionB, CancellationToken.None));
        Assert.Equal(UpdaterText.SetupVersionsGameUnavailable, error.Message);
        Assert.DoesNotContain(fixture.Requests, uri => uri.AbsoluteUri == ReleaseProtocol.LatestReleaseUrl);
        fixture.GameAvailable = true;
        Assert.Contains(await fixture.Catalog.ListAsync(CancellationToken.None), choice => choice.Version == SignedReleaseFixture.VersionB && choice.CanInstall);
    }

    /// <summary>
    /// Propagates user cancellation while catalog entries are checking the shared game revision.
    /// </summary>
    [Fact]
    public async Task AvailabilityChecksRemainCancellable()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        fixture.GameGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Catalog.ListAsync(cancellation.Token);
        await fixture.GameStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    /// <summary>
    /// Disables a signed target that the current installer engine cannot consume.
    /// </summary>
    [Fact]
    public async Task NewerEngineRequirementsDisableTheAffectedVersion()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        fixture.ReplaceManifest(release.Manifest with { MinimumEngineVersion = _unsupportedEngineVersion });
        var choices = await fixture.Catalog.ListAsync(CancellationToken.None);
        Assert.Equal(UpdaterText.SetupVersionsNewerSetupRequired, choices.Single(choice => choice.Version == SignedReleaseFixture.VersionC).UnavailableReason);
        Assert.True(choices.Single(choice => choice.Version == SignedReleaseFixture.VersionB).CanInstall);
    }

    /// <summary>
    /// Historical discovery does not grant permission to overwrite a newer installed tracker with an older one.
    /// </summary>
    [Fact]
    public async Task ExplicitHistoricalTargetRetainsForwardUpdatePolicy()
    {
        using var release = new SignedReleaseFixture();
        using var fixture = await SetupReleaseCatalogFixture.CreateAsync(release);
        var evidence = await fixture.Catalog.ReadAsync(SignedReleaseFixture.VersionB, CancellationToken.None);
        var manifest = release.Verifier.Verify(evidence.Manifest, evidence.Signatures);
        var request = new IronmonUpdateRequest(release.Root, SignedReleaseFixture.VersionC, ReleaseProtocol.RuntimeRequired, ReleaseProtocol.RuntimeRequired, SignedReleaseFixture.Commit, false, []);
        Assert.Throws<InvalidDataException>(() => IronmonOnlyUpdate.ValidateSelection(request, manifest, InstallationPurpose.Update));
    }
}
