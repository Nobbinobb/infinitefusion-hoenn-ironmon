using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Checks the early package gate against missing, partial, and incompatible registered x64 frameworks.
/// </summary>
public sealed class TrackerRuntimeAvailabilityTests
{
    private const string DesktopFramework = "Microsoft.WindowsDesktop.App";
    private const string BaselineVersion = "10.0.0";
    private const string ServicedVersion = "10.0.12";
    private const string EarlierPatchVersion = "10.0.10";
    private const string OlderMajorVersion = "9.0.12";
    private const string NewerMajorVersion = "11.0.0";
    private const string NewerMinorVersion = "10.1.0";
    private const string LegacyDesktopConfiguration = "{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"},{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"10.0.0\"}]}}";

    /// <summary>
    /// Accepts the core-only runtime and serviced patches without accepting a different major or minor.
    /// </summary>
    /// <param name="core">The registered core version, or null when absent.</param>
    /// <param name="desktop">The registered desktop version, or null when absent.</param>
    /// <param name="available">Whether the smaller tracker package can be selected.</param>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(ServicedVersion, null, true)]
    [InlineData(null, ServicedVersion, false)]
    [InlineData(OlderMajorVersion, OlderMajorVersion, false)]
    [InlineData(NewerMajorVersion, NewerMajorVersion, false)]
    [InlineData(NewerMinorVersion, NewerMinorVersion, false)]
    [InlineData(BaselineVersion, BaselineVersion, true)]
    [InlineData(BaselineVersion, null, true)]
    [InlineData(ServicedVersion, OlderMajorVersion, true)]
    [InlineData(ServicedVersion, EarlierPatchVersion, true)]
    public void RequiresMatchingCoreFrameworkWithoutRequiringDesktop(string? core, string? desktop, bool available)
    {
        var runtime = new TrackerRuntimeCompatibility(name => (name == DesktopFramework ? desktop : core) is { } version ? [Version.Parse(version)] : []);
        Assert.Equal(available, runtime.TrackerRuntimeAvailable);
    }

    /// <summary>
    /// Keeps the installer usable with the bundled runtime when Windows denies prerequisite inspection.
    /// </summary>
    [Fact]
    public void UnreadableRegistrationDisablesTheSmallerPackage()
    {
        var runtime = new TrackerRuntimeCompatibility(_ => throw new UnauthorizedAccessException());
        Assert.False(runtime.TrackerRuntimeAvailable);
    }

    /// <summary>
    /// Retains signed-package prerequisite checks when selecting an older tracker that actually requires WPF.
    /// </summary>
    /// <returns>A task completing after the core-only machine rejects an older Desktop-dependent package.</returns>
    [Fact]
    public async Task OlderPackagesStillEnforceTheirDeclaredDesktopDependency()
    {
        using var fixture = new SignedReleaseFixture(ReleaseProtocol.RuntimeRequired);
        fixture.Write(TrackerRuntimeCompatibility.ConfigurationPath, System.Text.Encoding.UTF8.GetBytes(LegacyDesktopConfiguration));
        var runtime = new TrackerRuntimeCompatibility(name => name == DesktopFramework ? [] : [Version.Parse(ServicedVersion)]);
        Assert.True(runtime.TrackerRuntimeAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.EnsureAsync(fixture.Root, ReleaseProtocol.RuntimeRequired, CancellationToken.None));
    }
}
