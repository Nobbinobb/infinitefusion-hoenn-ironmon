using System.Text.Json;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies explicit prerelease discovery with real signed metadata and persistent channel changes.
/// </summary>
public sealed partial class ReleaseTransportTests
{
    private const string PreferencesFile = "discovery-preferences.json";
    private const string BrokenPreferences = "{";

    /// <summary>
    /// Keeps stable discovery unchanged by default, persists opt-in across replacements, and restores the independent stable cache on opt-out.
    /// </summary>
    [Fact]
    public async Task TestUpdateOptInPersistsAndDoesNotPoisonStableDiscovery()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var stableApi = Api(fixture);
        var stableEvidence = fixture.Evidence;
        fixture.AdvanceVersion();
        var testApi = Api(fixture, testRelease: true);
        var endpoints = new List<string>();
        var handler = new Handler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            endpoints.Add(url);
            if (url == ReleaseProtocol.LatestReleaseUrl)
                return Body(stableApi);

            if (url == ReleaseProtocol.ReleaseListUrl)
                return Body(ReleaseList(stableApi, testApi));

            var evidence = url.Contains(SignedReleaseFixture.VersionC, StringComparison.Ordinal) ? fixture.Evidence : stableEvidence;
            return Body(url.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? evidence.Manifest : evidence.Signatures);
        });
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        var cache = workspace.PathFor(CacheName);
        using (var discovery = new ReleaseDiscovery(http, fixture.Verifier, cache))
        {
            Assert.False(discovery.IncludeTestReleases);
            Assert.Equal(ReleaseProtocol.ParseVersion(SignedReleaseFixture.VersionB), (await discovery.GetLatestAsync(CancellationToken.None))?.Version);
            Assert.DoesNotContain(ReleaseProtocol.ReleaseListUrl, endpoints);
            await discovery.SetIncludeTestReleasesAsync(true);
            var preview = await discovery.CheckAsync();
            Assert.Null(preview.Error);
            Assert.True(preview.IsTestRelease);
            Assert.Equal(SignedReleaseFixture.VersionC, fixture.Verifier.Verify(preview.Release!.Manifest, preview.Release.Signatures).IronmonVersion);
        }

        using (var replacement = new ReleaseDiscovery(http, fixture.Verifier, cache))
        {
            Assert.True(replacement.IncludeTestReleases);
            Assert.Equal(ReleaseProtocol.ParseVersion(SignedReleaseFixture.VersionC), (await replacement.GetLatestAsync(CancellationToken.None))?.Version);
            await replacement.SetIncludeTestReleasesAsync(false);
            var stable = await replacement.CheckAsync();
            Assert.Null(stable.Error);
            Assert.False(stable.IsTestRelease);
            Assert.Equal(SignedReleaseFixture.VersionB, fixture.Verifier.Verify(stable.Release!.Manifest, stable.Release.Signatures).IronmonVersion);
        }

        using var restarted = new ReleaseDiscovery(http, fixture.Verifier, cache);
        Assert.False(restarted.IncludeTestReleases);
    }

    /// <summary>
    /// Ignores drafts and chooses a newer stable target over an older test release without relying on API ordering.
    /// </summary>
    /// <param name="draft">Whether the newer release is still an unpublished draft.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestDiscoveryChoosesHighestPublishedNumericVersion(bool draft)
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var olderApi = Api(fixture, testRelease: true);
        var olderEvidence = fixture.Evidence;
        fixture.AdvanceVersion();
        var newerApi = Api(fixture, draft: draft);
        using var http = new ReleaseHttpClient(new Handler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == ReleaseProtocol.ReleaseListUrl)
                return Body(ReleaseList(olderApi, newerApi));

            var evidence = draft ? olderEvidence : fixture.Evidence;
            return Body(url.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? evidence.Manifest : evidence.Signatures);
        }), TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        await discovery.SetIncludeTestReleasesAsync(true);
        var result = await discovery.CheckAsync();
        Assert.Null(result.Error);
        Assert.Equal(draft, result.IsTestRelease);
        Assert.Equal(draft ? SignedReleaseFixture.VersionB : SignedReleaseFixture.VersionC, fixture.Verifier.Verify(result.Release!.Manifest, result.Release.Signatures).IronmonVersion);
    }

    /// <summary>
    /// Keeps independent signature verification mandatory after a user opts into test updates.
    /// </summary>
    [Fact]
    public async Task TestDiscoveryRejectsInvalidSignaturesWithoutFallingBack()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        using var http = new ReleaseHttpClient(new Handler(request => request.RequestUri!.AbsoluteUri == ReleaseProtocol.ReleaseListUrl ? Body(ReleaseList(Api(fixture, testRelease: true))) : Body(request.RequestUri.AbsoluteUri.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? fixture.Evidence.Manifest : [0])), TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        await discovery.SetIncludeTestReleasesAsync(true);
        var result = await discovery.CheckAsync();
        Assert.Null(result.Release);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// Refuses an older authenticated target even while prereleases are enabled.
    /// </summary>
    [Fact]
    public async Task TestDiscoveryRetainsAuthenticatedRollbackProtection()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var original = fixture.Evidence;
        var useOlder = false;
        fixture.AdvanceVersion();
        using var http = new ReleaseHttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == ReleaseProtocol.ReleaseListUrl)
                return Body(ReleaseList(useOlder ? ReleaseApi(original, fixture) : Api(fixture, testRelease: true)));

            var evidence = useOlder ? original : fixture.Evidence;
            return Body(request.RequestUri.AbsoluteUri.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? evidence.Manifest : evidence.Signatures);
        }), TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        await discovery.SetIncludeTestReleasesAsync(true);
        Assert.NotNull((await discovery.CheckAsync()).Release);
        useOlder = true;
        var result = await discovery.CheckAsync(manual: true);
        Assert.Null(result.Release);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// Defaults damaged optional preference storage to stable discovery.
    /// </summary>
    [Fact]
    public async Task DamagedTestUpdatePreferenceDoesNotOptIntoTesting()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var cache = workspace.PathFor(CacheName);
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, PreferencesFile), BrokenPreferences);
        using var http = new ReleaseHttpClient(new Handler(_ => throw new InvalidOperationException()), TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, cache);
        Assert.False(discovery.IncludeTestReleases);
    }

    /// <summary>
    /// Advertises one previously captured signed target as a published test release.
    /// </summary>
    /// <param name="evidence">The unchanged captured signed release.</param>
    /// <param name="fixture">The independently configured verifier.</param>
    /// <returns>The repository-bound discovery identity for that version.</returns>
    private static byte[] ReleaseApi(ReleaseEvidence evidence, SignedReleaseFixture fixture)
    {
        var version = fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures).IronmonVersion;
        return ReleaseJson.Serialize(new { tag_name = TagPrefix + version, draft = false, prerelease = true, html_url = ReleaseProtocol.ReleaseBaseUrl + TagSegment + TagPrefix + version, assets = new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName }.Select(name => new { name, browser_download_url = ReleaseProtocol.AssetUrl(version, name) }) });
    }

    /// <summary>
    /// Builds a deterministic release list from independently captured API projections.
    /// </summary>
    /// <param name="releases">The exact release identities in arbitrary order.</param>
    /// <returns>The serialized public release list.</returns>
    private static byte[] ReleaseList(params byte[][] releases)
        => ReleaseJson.Serialize(releases.Select(bytes => JsonSerializer.Deserialize<JsonElement>(bytes)).ToArray());
}
