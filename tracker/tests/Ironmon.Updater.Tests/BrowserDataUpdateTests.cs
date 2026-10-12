using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises update review while the tracker's browser runtime owns mutable files.
/// </summary>
public sealed class BrowserDataUpdateTests
{
    private const string BrowserRoot = "Ironmon Tracker/Ironmon Tracker.exe.WebView2";
    private const string AlternateBrowserRoot = "Ironmon Tracker/ironmon tracker.exe.webview2";
    private const string BrowserLock = "EBWebView/lockfile";
    private const string ManagedFilePath = "Ironmon Tracker/owned.dll";
    private const string SimilarBrowserRoot = "Ironmon Tracker/Ironmon Tracker.exe.WebView2-other/lockfile";
    private const string InstallationRoot = "installation";
    private const string ZipExtension = ".zip";
    private const string PathSeparator = "/";

    /// <summary>
    /// Reviews and prepares the signed update while browser data remains exclusively locked and changes.
    /// </summary>
    /// <param name="browserRoot">The actual casing of the runtime data directory.</param>
    /// <returns>A task representing update review and preparation while browser data remains locked.</returns>
    [Theory]
    [InlineData(BrowserRoot)]
    [InlineData(AlternateBrowserRoot)]
    public async Task UpdateReviewPreservesLockedBrowserData(string browserRoot)
    {
        using var fixture = new TrackerUpdateTestFixture();
        var relative = browserRoot + '/' + BrowserLock;
        var path = Path.Combine(fixture.Release.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var browser = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        browser.WriteByte(1);
        browser.Flush();
        await fixture.Session.StartAsync();
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.AvailableVersion);
        await fixture.Session.OpenAsync();
        Assert.Null(fixture.Session.Error);
        Assert.True(fixture.Session.CanUpdate);
        var review = Assert.IsType<TrackerUpdateReview>(fixture.Session.Review);
        Assert.DoesNotContain(review.Plan.Entries, entry => entry.Path.StartsWith(browserRoot + '/', StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fixture.Release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
        browser.Position = 0;
        browser.WriteByte(2);
        browser.Flush();
        var prepared = await fixture.Preparation.PrepareAsync(review, [], false);
        Assert.NotEqual(Guid.Empty, prepared.TransactionId);
        browser.Position = 0;
        Assert.Equal(2, browser.ReadByte());
    }

    /// <summary>
    /// Keeps strict file-open checks for program files and unrelated folders with similar names.
    /// </summary>
    /// <param name="relative">The installation file that must remain subject to exact hashing.</param>
    /// <returns>A task representing snapshot rejection for an exclusively locked installation file.</returns>
    [Theory]
    [InlineData(ManagedFilePath)]
    [InlineData(SimilarBrowserRoot)]
    public async Task OtherLockedFilesStillBlockSnapshot(string relative)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(InstallationRoot);
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var locked = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        locked.WriteByte(1);
        locked.Flush();
        await Assert.ThrowsAsync<IOException>(() => InstallationFileSnapshot.ReadAsync(root));
    }

    /// <summary>
    /// Refuses both previous and new package ownership of browser state and its directory boundary.
    /// </summary>
    /// <param name="path">The protected directory or a file inside it.</param>
    [Theory]
    [InlineData(BrowserRoot)]
    [InlineData(BrowserRoot + PathSeparator + BrowserLock)]
    public void PackageOwnershipCannotReplaceBrowserData(string path)
    {
        var planner = new FileUpdatePlanner(new FileManagementPolicy());
        var file = new ManagedFile(path, FilePlannerFixture.Content(1), ManagedFileOwner.Ironmon);
        Assert.Throws<InvalidDataException>(() => planner.Create([file], [], []));
        Assert.Throws<InvalidDataException>(() => planner.Create([], [], [file]));
    }
}
