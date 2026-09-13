using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises authenticated discovery, bounded transport, conditional caching and failure behavior.
/// </summary>
public sealed class ReleaseTransportTests
{
    private const string EntityTag = "\"fixture-release\"";
    private const string CacheName = "discovery";
    private const string TagPrefix = "v";
    private const string TagSegment = "tag/";
    private const string EmptyObject = "{}";
    private const string HostileUrl = "https://example.com/payload.zip";
    private const string AuthorizedCdn = "https://release-assets.githubusercontent.com/fixture/payload";
    private const string ArchiveName = "fixture.zip";
    private const string Missing = "missing";

    /// <summary>
    /// Reuses the signed observation across startup calls and processes, and uses ETags for manual checks.
    /// </summary>
    [Fact]
    public async Task SignedCacheAndConditionalChecksAvoidRepeatedStartupTraffic()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var apiCalls = 0;
        var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == ReleaseProtocol.LatestReleaseUrl)
            {
                apiCalls++;
                if (apiCalls > 1)
                {
                    Assert.Equal(EntityTag, Assert.Single(request.Headers.IfNoneMatch).ToString());
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                var response = Body(Api(fixture));
                response.Headers.ETag = EntityTagHeaderValue.Parse(EntityTag);
                return response;
            }

            return Body(request.RequestUri.AbsoluteUri.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? fixture.Evidence.Manifest : fixture.Evidence.Signatures);
        });
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        using (var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName)))
        {
            Assert.NotNull((await discovery.CheckAsync()).Release);
            Assert.NotNull((await discovery.CheckAsync()).Release);
            Assert.Equal(3, handler.Calls);
            Assert.NotNull((await discovery.CheckAsync(manual: true)).Release);
            Assert.Equal(4, handler.Calls);
        }

        using var restarted = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        Assert.NotNull((await restarted.CheckAsync()).Release);
        Assert.Equal(4, handler.Calls);
    }

    /// <summary>
    /// Treats malformed API responses as non-fatal update-check failures and avoids automatic retry loops.
    /// </summary>
    [Fact]
    public async Task MalformedDiscoveryDoesNotBlockStartup()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var handler = new Handler(_ => Body(Encoding.UTF8.GetBytes(EmptyObject)));
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        Assert.NotNull((await discovery.CheckAsync()).Error);
        Assert.Null(await discovery.GetLatestAsync(CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// Explains inaccessible private or missing releases without retrying permanent failures or exposing raw HTTP errors.
    /// </summary>
    [Fact]
    public async Task MissingPublicReleaseIsActionableAndUnauthenticated()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        var result = await discovery.CheckAsync(true);
        Assert.Null(result.Release);
        Assert.Equal(UpdaterText.ReleaseDiscoveryNoPubliclyAccessibleIronmonReleaseWasFoundCheckThat, result.Error);
        Assert.DoesNotContain("rehearsal", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Response status code", result.Error);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// Persists rate-limit timing and honors it even when the user requests another manual check.
    /// </summary>
    [Fact]
    public async Task RateLimitPreventsRepeatedRequestsAcrossProcesses()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        using (var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName)))
        {
            Assert.NotNull((await discovery.CheckAsync()).RetryAt);
            Assert.NotNull((await discovery.CheckAsync(manual: true)).RetryAt);
        }

        using var restarted = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        Assert.NotNull((await restarted.CheckAsync(manual: true)).RetryAt);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// Rejects a validly signed replacement of an already observed release sequence.
    /// </summary>
    [Fact]
    public async Task SignedSequenceCannotBeReplacedAfterObservation()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var evidence = fixture.Evidence;
        var handler = new Handler(request => request.RequestUri!.AbsoluteUri == ReleaseProtocol.LatestReleaseUrl ? Body(Api(fixture)) : Body(request.RequestUri.AbsoluteUri.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? evidence.Manifest : evidence.Signatures));
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        Assert.NotNull((await discovery.CheckAsync()).Release);
        evidence = fixture.Sign(fixture.Manifest with { Game = fixture.Manifest.Game with { ActiveRunPolicy = ReleaseProtocol.FinishRun } }, fixture.Evidence.Inventories);
        var changed = await discovery.CheckAsync(manual: true);
        Assert.Null(changed.Release);
        Assert.NotNull(changed.Error);
    }

    /// <summary>
    /// Retries transient server failures with a bounded budget while leaving permanent failures at one request.
    /// </summary>
    /// <param name="transient">Whether the response can succeed after a retry.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyTransientResponsesAreRetried(bool transient)
    {
        var calls = 0;
        var handler = new Handler(_ => ++calls < 3 ? new HttpResponseMessage(transient ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NotFound) : Body([1]));
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        if (transient)
        {
            Assert.Equal(new byte[] { 1 }, (await http.ReadAsync(new Uri(ReleaseProtocol.LatestReleaseUrl), 10)).Bytes);
            Assert.Equal(3, calls);
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => http.ReadAsync(new Uri(ReleaseProtocol.LatestReleaseUrl), 10));
            Assert.Equal(1, calls);
        }
    }

    /// <summary>
    /// Rejects untrusted redirect targets and oversized bodies before exposing artifact bytes.
    /// </summary>
    [Fact]
    public async Task RedirectsAndBodiesRemainBounded()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(HostileUrl) } });
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        var asset = new Uri(ReleaseProtocol.AssetUrl(SignedReleaseFixture.VersionB, ArchiveName));
        await Assert.ThrowsAsync<InvalidDataException>(() => http.ReadAsync(asset, 100));
        Assert.Equal(1, handler.Calls);
        using var oversized = new ReleaseHttpClient(new Handler(_ => Body([1, 2, 3])), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(() => oversized.ReadAsync(asset, 2));
    }

    /// <summary>
    /// Allows the repository's CDN handoff without forwarding credentials or conditional API headers.
    /// </summary>
    [Fact]
    public async Task PublicCdnRedirectCarriesNoCredentials()
    {
        var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Empty(request.Headers.IfNoneMatch);
            return request.RequestUri!.Host == new Uri(AuthorizedCdn).Host ? Body([1]) : new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(AuthorizedCdn) } };
        });
        using var http = new ReleaseHttpClient(handler, TimeProvider.System);
        Assert.Equal(new byte[] { 1 }, (await http.ReadAsync(new Uri(ReleaseProtocol.AssetUrl(SignedReleaseFixture.VersionB, ArchiveName)), 10)).Bytes);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>
    /// Propagates caller cancellation instead of converting it into an absent release.
    /// </summary>
    [Fact]
    public async Task CallerCancellationRemainsObservable()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        using var http = new ReleaseHttpClient(new Handler(_ => throw new HttpRequestException(Missing)), TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.CheckAsync(cancellationToken: cancellation.Token));
    }

    /// <summary>
    /// Builds only the public API fields used by discovery.
    /// </summary>
    /// <param name="fixture">The signed release being advertised.</param>
    /// <returns>The bounded API response bytes.</returns>
    private static byte[] Api(SignedReleaseFixture fixture)
        => ReleaseJson.Serialize(new { tag_name = TagPrefix + fixture.Manifest.IronmonVersion, draft = false, prerelease = false, html_url = ReleaseProtocol.ReleaseBaseUrl + TagSegment + TagPrefix + fixture.Manifest.IronmonVersion, assets = new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName }.Select(name => new { name, browser_download_url = ReleaseProtocol.AssetUrl(fixture.Manifest.IronmonVersion, name) }) });

    /// <summary>
    /// Creates an owned successful response body.
    /// </summary>
    /// <param name="bytes">The response bytes.</param>
    /// <returns>The response to dispose after reading.</returns>
    private static HttpResponseMessage Body(byte[] bytes)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    /// <summary>
    /// Supplies deterministic responses while exercising the production HTTP policy.
    /// </summary>
    /// <remarks>
    /// Constructs a transport callback; no actual network connection is made.
    /// </remarks>
    /// <param name="respond">The deterministic response factory.</param>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the number of requested responses.
        /// </summary>
        internal int Calls { get; private set; }

        /// <summary>
        /// Returns the fixture response after checking caller cancellation.
        /// </summary>
        /// <param name="request">The actual production request.</param>
        /// <param name="cancellationToken">The request token.</param>
        /// <returns>The owned fixture response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
