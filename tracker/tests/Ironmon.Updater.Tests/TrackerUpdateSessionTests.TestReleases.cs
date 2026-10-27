using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises opt-in, signed update handoff, and removal of test selections through the production session.
/// </summary>
public sealed partial class TrackerUpdateSessionTests
{
    /// <summary>
    /// Uses the normal signed review, download, preparation, and helper handoff for an explicitly enabled test update.
    /// </summary>
    [Fact]
    public async Task OptedInTestReleaseUsesTheNormalTrackerUpdateHandoff()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        fixture.CatalogHistory.LatestReleaseIsTest = true;
        await fixture.Session.StartAsync();
        Assert.False(fixture.Session.IncludeTestReleases);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.AvailableVersion);
        await fixture.Session.OpenAsync();
        await fixture.Session.SetIncludeTestReleasesAsync(true);
        Assert.Null(fixture.Session.Error);
        Assert.True(fixture.Session.IncludeTestReleases);
        Assert.True(fixture.Session.AvailableIsTestRelease);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.AvailableVersion);
        Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.Review!.Manifest.IronmonVersion);
        Assert.True(fixture.Session.CanUpdate);
        await fixture.Session.UpdateAsync();
        Assert.Null(fixture.Session.Error);
        Assert.NotNull(fixture.Handoff);
        Assert.False(fixture.RecoveryHandoff);
    }

    /// <summary>
    /// Clears test selections and their reviews when the player returns to the stable channel.
    /// </summary>
    [Fact]
    public async Task OptingOutReplacesTheTestReviewWithAStableReview()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        fixture.CatalogHistory.LatestReleaseIsTest = true;
        await fixture.Session.OpenAsync();
        await fixture.Session.SetIncludeTestReleasesAsync(true);
        var preview = fixture.Session.Review;
        await fixture.Session.LoadReleaseChoicesAsync();
        await fixture.Session.SelectReleaseVersionAsync(SignedReleaseFixture.VersionC);
        await fixture.Session.SetIncludeTestReleasesAsync(false);
        Assert.Null(fixture.Session.Error);
        Assert.False(fixture.Session.IncludeTestReleases);
        Assert.False(fixture.Session.AvailableIsTestRelease);
        Assert.Null(fixture.Session.SelectedReleaseVersion);
        Assert.Empty(fixture.Session.ReleaseChoices);
        Assert.NotSame(preview, fixture.Session.Review);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.Review!.Manifest.IronmonVersion);
    }

    /// <summary>
    /// Keeps intermediate prereleases out of the advanced selector until testing is explicitly enabled.
    /// </summary>
    [Fact]
    public async Task StableAdvancedSelectionDoesNotOfferIntermediateTestReleases()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        fixture.CatalogHistory.HistoricalReleaseIsTest = true;
        await fixture.Session.OpenAsync();
        await fixture.Session.LoadReleaseChoicesAsync();
        Assert.DoesNotContain(fixture.Session.ReleaseChoices, choice => choice.IsTestRelease);
        await fixture.Session.SetIncludeTestReleasesAsync(true);
        await fixture.Session.LoadReleaseChoicesAsync();
        Assert.Contains(fixture.Session.ReleaseChoices, choice => choice.Version == SignedReleaseFixture.VersionB && choice.IsTestRelease);
    }
}
