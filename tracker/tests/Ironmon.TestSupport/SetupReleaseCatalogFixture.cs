using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.TestSupport;

/// <summary>
/// Serves two genuine signed historical releases and mutable upstream availability without external network access.
/// </summary>
internal sealed class SetupReleaseCatalogFixture : IArtifactSource, IDisposable
{
    internal const string LegacyVersion = "0.7.9";
    private const string _tagPrefix = "v";
    private const string _tagSegment = "tag/";
    private const string _ahead = "ahead";
    private const string _zipMediaType = "application/zip";
    private const string _discoveryDirectory = "catalog-discovery";
    private readonly SignedReleaseFixture _release;
    private readonly TestWorkspace _workspace = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReleaseManifest> _manifests = new(StringComparer.Ordinal);
    private readonly ReleaseHttpClient _http;

    /// <summary>
    /// Gets the production catalog restricted to the fixture transport.
    /// </summary>
    internal SetupReleaseCatalog Catalog { get; }

    /// <summary>
    /// Gets independent latest discovery sharing the same real signed releases.
    /// </summary>
    internal ReleaseDiscovery Discovery { get; }

    /// <summary>
    /// Gets all actual metadata and HEAD requests.
    /// </summary>
    internal ConcurrentQueue<Uri> Requests { get; } = new();

    /// <summary>
    /// Gets all installation body downloads, separate from availability checks.
    /// </summary>
    internal List<Uri> Downloads { get; } = [];

    /// <summary>
    /// Gets or sets whether the signed game remains reachable and downloadable.
    /// </summary>
    internal bool GameAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the older release is still published in the public catalog.
    /// </summary>
    internal bool HistoricalReleasePublished { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the game archive remains accessible after ancestry succeeds.
    /// </summary>
    internal bool GameArchiveAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether required release downloads remain accessible.
    /// </summary>
    internal bool DownloadsAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether signature responses retain their authenticated bytes.
    /// </summary>
    internal bool ValidSignatures { get; set; } = true;

    /// <summary>
    /// Gets or sets an upstream response that attempts to identify another base commit.
    /// </summary>
    internal bool WrongGameIdentity { get; set; }

    /// <summary>
    /// Gets or sets whether the server advertises signed download lengths correctly.
    /// </summary>
    internal bool CorrectDownloadLengths { get; set; } = true;

    /// <summary>
    /// Gets or sets an injected pause at the upstream availability boundary.
    /// </summary>
    internal TaskCompletionSource? GameGate { get; set; }

    /// <summary>
    /// Gets the signal that an actual upstream availability request reached the fixture.
    /// </summary>
    internal TaskCompletionSource GameStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Changes a signed observation to test that review consent cannot authorize different manifest bytes.
    /// </summary>
    /// <param name="manifest">The deliberately changed target signed with this fixture's isolated key.</param>
    internal void ReplaceManifest(ReleaseManifest manifest)
    {
        var evidence = _release.Sign(manifest, []);
        _manifests[manifest.IronmonVersion] = manifest;
        _files[ReleaseProtocol.AssetUrl(manifest.IronmonVersion, ReleaseProtocol.ManifestName)] = evidence.Manifest;
        _files[ReleaseProtocol.AssetUrl(manifest.IronmonVersion, ReleaseProtocol.SignatureName)] = evidence.Signatures;
    }

    /// <summary>
    /// Creates the fixed production services before any fixture body is captured.
    /// </summary>
    /// <param name="release">The caller-owned signed release source.</param>
    private SetupReleaseCatalogFixture(SignedReleaseFixture release)
    {
        _release = release;
        _http = new ReleaseHttpClient(new Handler(this), TimeProvider.System);
        Catalog = new SetupReleaseCatalog(_http, release.Verifier);
        Discovery = new ReleaseDiscovery(_http, release.Verifier, _workspace.PathFor(_discoveryDirectory));
    }

    /// <summary>
    /// Captures exact B bytes, advances the real producer fixture, then captures exact C bytes with the same trust key.
    /// </summary>
    /// <param name="release">The caller-owned release that advances to version C.</param>
    /// <returns>The complete two-release fixture.</returns>
    internal static async Task<SetupReleaseCatalogFixture> CreateAsync(SignedReleaseFixture release)
    {
        var fixture = new SetupReleaseCatalogFixture(release);
        await fixture.CaptureAsync();
        release.AdvanceVersion();
        await fixture.CaptureAsync();
        release.Requests.Clear();
        return fixture;
    }

    /// <summary>
    /// Retains an independently signed release and every exact immutable payload before advancing it.
    /// </summary>
    /// <returns>The complete local snapshot.</returns>
    private async Task CaptureAsync()
    {
        var manifest = _release.Manifest;
        _manifests.Add(manifest.IronmonVersion, manifest);
        _files.Add(ReleaseProtocol.AssetUrl(manifest.IronmonVersion, ReleaseProtocol.ManifestName), _release.Evidence.Manifest);
        _files.Add(ReleaseProtocol.AssetUrl(manifest.IronmonVersion, ReleaseProtocol.SignatureName), _release.Evidence.Signatures);
        foreach (var asset in manifest.Assets)
        {
            await using var output = new MemoryStream();
            await _release.CopyToAsync(new Uri(asset.Url), output, asset.Bytes, CancellationToken.None);
            _files[asset.Url] = output.ToArray();
        }
    }

    /// <summary>
    /// Serves captured historical bodies to the real preparation engine.
    /// </summary>
    /// <param name="source">The exact signed release URL.</param>
    /// <param name="destination">The caller-owned output.</param>
    /// <param name="maximumBytes">The signed body size.</param>
    /// <param name="cancellationToken">The current installation token.</param>
    /// <returns>The completed bounded copy.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        Downloads.Add(source);
        var bytes = _files[source.AbsoluteUri];
        if (bytes.LongLength > maximumBytes)
            throw new InvalidDataException();

        await destination.WriteAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// Implements only expected public discovery, pinned game checks and signed download headers.
    /// </summary>
    /// <param name="request">The actual production HTTP request.</param>
    /// <param name="cancellationToken">The production request deadline.</param>
    /// <returns>The owned deterministic response.</returns>
    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Enqueue(uri);
        var url = uri.AbsoluteUri;
        if (url == ReleaseProtocol.ReleaseListUrl)
            return Body(ReleaseJson.Serialize(_manifests.Values.Where(manifest => HistoricalReleasePublished || manifest.IronmonVersion != SignedReleaseFixture.VersionB).Select(Api).Append(Api(null)).ToArray()));

        if (url == ReleaseProtocol.LatestReleaseUrl)
            return Body(ReleaseJson.Serialize(Api(_manifests[SignedReleaseFixture.VersionC])));

        if (url.StartsWith(ReleaseProtocol.ReleaseTagUrl, StringComparison.Ordinal))
            return _manifests.TryGetValue(url[ReleaseProtocol.ReleaseTagUrl.Length..], out var manifest) && (HistoricalReleasePublished || manifest.IronmonVersion != SignedReleaseFixture.VersionB) ? Body(ReleaseJson.Serialize(Api(manifest))) : new(HttpStatusCode.NotFound);

        if (url.StartsWith(ReleaseProtocol.GameCompareUrl, StringComparison.Ordinal))
        {
            GameStarted.TrySetResult();
            if (GameGate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);

            if (!GameAvailable)
                return new(HttpStatusCode.NotFound);

            var commit = WrongGameIdentity ? new string('2', 40) : SignedReleaseFixture.Commit;
            return Body(ReleaseJson.Serialize(new { status = _ahead, base_commit = new { sha = commit }, merge_base_commit = new { sha = commit } }));
        }

        if (url.StartsWith(ReleaseProtocol.GameArchiveUrl, StringComparison.Ordinal))
        {
            AssertHead(request);
            var response = new HttpResponseMessage(GameArchiveAvailable ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(_zipMediaType);
            return response;
        }

        if (request.Method == HttpMethod.Head)
        {
            if (!DownloadsAvailable || !_files.TryGetValue(url, out var payload))
                return new(HttpStatusCode.NotFound);

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = payload.LongLength + (CorrectDownloadLengths ? 0 : 1);
            return response;
        }

        if (!_files.TryGetValue(url, out var bytes))
            return new(HttpStatusCode.NotFound);

        return Body(!ValidSignatures && url.EndsWith(ReleaseProtocol.SignatureName, StringComparison.Ordinal) ? [0] : bytes);
    }

    /// <summary>
    /// Rejects accidental full game downloads during catalog enumeration.
    /// </summary>
    /// <param name="request">The upstream archive request.</param>
    private static void AssertHead(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Head)
            throw new InvalidOperationException();
    }

    /// <summary>
    /// Builds the minimal stable public API identity without making it signature authority.
    /// </summary>
    /// <param name="manifest">The signed target, or null for an unsupported legacy release.</param>
    /// <returns>The permissive public API projection.</returns>
    private static object Api(ReleaseManifest? manifest)
    {
        var version = manifest?.IronmonVersion ?? LegacyVersion;
        return new { tag_name = _tagPrefix + version, draft = false, prerelease = false, html_url = ReleaseProtocol.ReleaseBaseUrl + _tagSegment + _tagPrefix + version, assets = (manifest is null ? Array.Empty<string>() : new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName }).Select(name => new { name, browser_download_url = ReleaseProtocol.AssetUrl(version, name) }) };
    }

    /// <summary>
    /// Wraps bounded fixture metadata in an owned response.
    /// </summary>
    /// <param name="bytes">The exact metadata.</param>
    /// <returns>The deterministic HTTP body.</returns>
    private static HttpResponseMessage Body(byte[] bytes)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    /// <summary>
    /// Releases discovery and transport before deleting only this fixture's cache.
    /// </summary>
    public void Dispose()
    {
        Discovery.Dispose();
        _http.Dispose();
        _workspace.Dispose();
    }

    /// <summary>
    /// Routes real transport requests into the deterministic release boundary.
    /// </summary>
    /// <remarks>
    /// Constructs one fixture handler without exposing a network path.
    /// </remarks>
    /// <param name="fixture">The fixed two-release responder.</param>
    private sealed class Handler(SetupReleaseCatalogFixture fixture) : HttpMessageHandler
    {
        /// <summary>
        /// Executes one cancellable fixture response.
        /// </summary>
        /// <param name="request">The bounded production request.</param>
        /// <param name="cancellationToken">The active request token.</param>
        /// <returns>The owned response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => fixture.RespondAsync(request, cancellationToken);
    }
}
