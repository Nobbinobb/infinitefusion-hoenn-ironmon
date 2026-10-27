using System.Reflection;
using Ironmon.Tracker.App.Components.Updates;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Tests.Updates;

/// <summary>
/// Exercises the persistent test-update checkbox through the production panel renderer.
/// </summary>
public sealed partial class TrackerUpdatePanelTests
{
    private const string TestReleaseChange = "ChangeTestReleasesAsync";

    /// <summary>
    /// Exposes the opt-in when stable is current and marks the offered test version after the real checkbox callback.
    /// </summary>
    [Fact]
    public async Task CurrentStableTrackerCanOptIntoAnExplicitlyLabeledTestUpdate()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.EnableHistoryAsync();
        fixture.CatalogHistory.LatestReleaseIsTest = true;
        fixture.ObservedVersion = SignedReleaseFixture.VersionB;
        await fixture.Session.OpenFromSettingsAsync();
        Assert.Null(fixture.Session.AvailableVersion);
        await RenderAsync(fixture, async (component, html) =>
        {
            Assert.Contains("Include test releases", html());
            Assert.DoesNotContain("Test release</p>", html());
            var change = typeof(TrackerUpdatePanel).GetMethod(TestReleaseChange, BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)change.Invoke(component, [new ChangeEventArgs { Value = true }])!;
            Assert.True(fixture.Session.IncludeTestReleases);
            Assert.Equal(SignedReleaseFixture.VersionC, fixture.Session.AvailableVersion);
            Assert.Contains("Test release", html());
            await InvokeVersionsAsync(component, ToggleVersions);
            Assert.Contains("Latest test release:", html());
            Assert.DoesNotContain("Latest stable: " + SignedReleaseFixture.VersionC, html());
        });
    }
}
