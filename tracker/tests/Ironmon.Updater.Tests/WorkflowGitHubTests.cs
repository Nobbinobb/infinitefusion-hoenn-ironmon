using Ironmon.SpriteSizeTool;
using Ironmon.Updater.Infrastructure;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises workflow-only credentials, server-directed retries and signed release discovery without network traffic.
/// </summary>
public sealed class WorkflowGitHubTests
{
    private const string Token = "fixture-workflow-token";
    private const string Bearer = "Bearer";
    private const string MetadataUrl = "https://api.github.com/repos/infinitefusion/pif-downloadables/git/ref/heads/master";
    private const string CdnUrl = "https://release-assets.githubusercontent.com/fixture/manifest.json";
    private const string RemainingHeader = "X-RateLimit-Remaining";
    private const string ResetHeader = "X-RateLimit-Reset";
    private const string Zero = "0";
    private const string CacheName = "workflow-discovery";
    private const string TagPrefix = "v";
    private const string TagSegment = "tag/";

    /// <summary>
    /// Sends the token only to the two exact HTTPS API GET endpoints, even if a caller supplies an authorization header.
    /// </summary>
    /// <param name="url">The outgoing URL.</param>
    /// <param name="authorized">Whether the endpoint may receive the workflow token.</param>
    [Theory]
    [InlineData(ReleaseProtocol.LatestReleaseUrl, true)]
    [InlineData(MetadataUrl, true)]
    [InlineData(CdnUrl, false)]
    [InlineData("https://raw.githubusercontent.com/infinitefusion/pif-downloadables/master/BASE_SPRITES", false)]
    [InlineData("https://api.github.com/repos/other/private/releases/latest", false)]
    [InlineData("http://api.github.com/repos/infinitefusion/pif-downloadables/git/ref/heads/master", false)]
    [InlineData("https://api.github.com/repos/infinitefusion/pif-downloadables/git/ref/heads/master?extra=1", false)]
    [InlineData("https://api.github.com.example.com/repos/infinitefusion/pif-downloadables/git/ref/heads/master", false)]
    public async Task CredentialsAreRestrictedToExactApiLookups(string url, bool authorized)
    {
        using var client = new HttpClient(new WorkflowGitHubHandler(Token, new Handler(request =>
        {
            Assert.Equal(authorized ? Token : null, request.Headers.Authorization?.Parameter);
            Assert.Equal(authorized ? Bearer : null, request.Headers.Authorization?.Scheme);
            return new(HttpStatusCode.OK);
        }), TimeProvider.System));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, Token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Leaves local runs without credentials and withholds credentials on non-GET requests.
    /// </summary>
    /// <param name="token">The optional credential.</param>
    /// <param name="head">Whether the request is HEAD instead of GET.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(Token, true)]
    public async Task LocalRunsAndNonGetRequestsRemainUnauthenticated(string? token, bool head)
    {
        using var client = new HttpClient(new WorkflowGitHubHandler(token, new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.OK);
        }), TimeProvider.System));

        using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get, MetadataUrl);
        using var response = await client.SendAsync(request);
    }

    /// <summary>
    /// Release redirects cannot leak the workflow token to asset hosts, and API redirects remain forbidden.
    /// </summary>
    [Fact]
    public async Task ReleaseRedirectsNeverReceiveCredentials()
    {
        var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return request.RequestUri!.AbsoluteUri == CdnUrl
                ? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }
                : new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(CdnUrl) } };
        });

        using var http = new ReleaseHttpClient(new WorkflowGitHubHandler(Token, handler, TimeProvider.System), TimeProvider.System);
        var document = await http.ReadAsync(new Uri(ReleaseProtocol.AssetUrl(SignedReleaseFixture.VersionB, ReleaseProtocol.ManifestName)), 10);
        Assert.Equal(new byte[] { 1 }, document.Bytes);
        Assert.Equal(2, handler.Calls);

        var apiHandler = new Handler(request =>
        {
            Assert.Equal(Token, request.Headers.Authorization?.Parameter);
            return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(CdnUrl) } };
        });

        using var apiHttp = new ReleaseHttpClient(new WorkflowGitHubHandler(Token, apiHandler, TimeProvider.System), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(() => apiHttp.ReadAsync(new Uri(ReleaseProtocol.LatestReleaseUrl), 10));
        Assert.Equal(1, apiHandler.Calls);
    }

    /// <summary>
    /// Honors the later of Retry-After and an exhausted primary-quota reset, including HTTP date headers.
    /// </summary>
    /// <param name="dateHeader">Whether Retry-After uses an absolute date.</param>
    /// <param name="resetSeconds">The exhausted quota's reset offset.</param>
    /// <param name="expectedSeconds">The earliest allowed retry offset.</param>
    [Theory]
    [InlineData(false, 90, 90)]
    [InlineData(true, 10, 30)]
    public async Task BothServerDeadlinesAreRespected(bool dateHeader, int resetSeconds, int expectedSeconds)
    {
        var clock = new Clock();
        var content = new ByteArrayContent([1]);
        using var client = new HttpClient(new WorkflowGitHubHandler(Token, new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content };
            response.Headers.Add(RemainingHeader, Zero);
            response.Headers.Add(ResetHeader, (clock.GetUtcNow().ToUnixTimeSeconds() + resetSeconds).ToString(CultureInfo.InvariantCulture));
            response.Headers.RetryAfter = dateHeader ? new(clock.GetUtcNow() + TimeSpan.FromSeconds(30)) : new(TimeSpan.FromSeconds(30));
            return response;
        }), clock));

        var error = await Assert.ThrowsAsync<ReleaseRateLimitException>(() => client.GetAsync(MetadataUrl));
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(expectedSeconds), error.RetryAt);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// Uses a conservative delay when a primary reset is invalid or a secondary limit has no timing header.
    /// </summary>
    /// <param name="primary">Whether the response exhausts the primary quota.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrInvalidTimingHasABoundedFallback(bool primary)
    {
        var clock = new Clock();
        using var client = new HttpClient(new WorkflowGitHubHandler(Token, new Handler(_ =>
        {
            var response = new HttpResponseMessage(primary ? HttpStatusCode.Forbidden : HttpStatusCode.TooManyRequests);
            if (primary)
            {
                response.Headers.Add(RemainingHeader, Zero);
                response.Headers.Add(ResetHeader, long.MaxValue.ToString(CultureInfo.InvariantCulture));
            }

            return response;
        }), clock));

        var error = await Assert.ThrowsAsync<ReleaseRateLimitException>(() => client.GetAsync(MetadataUrl));
        Assert.Equal(clock.GetUtcNow() + (primary ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1)), error.RetryAt);
    }

    /// <summary>
    /// Does not retry authentication or permission failures as though they were transient rate limits.
    /// </summary>
    /// <param name="status">The permanent rejection status.</param>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PermissionFailuresAreNotRetried(HttpStatusCode status)
    {
        var handler = new Handler(_ => new(status));
        using var client = new HttpClient(new WorkflowGitHubHandler(Token, handler, TimeProvider.System));
        var requests = new WorkflowGitHubRequests();
        await Assert.ThrowsAsync<HttpRequestException>(() => requests.RetryAsync(() => client.GetByteArrayAsync(MetadataUrl), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// Retries a secondary-limit JSON response without timing headers, even when the primary quota has time remaining.
    /// </summary>
    [Fact]
    public async Task UpstreamMetadataRecoversFromSecondaryLimitWithoutRetryHeader()
    {
        var clock = new Clock();
        var calls = 0;
        var handler = new Handler(request =>
        {
            Assert.Equal(Token, request.Headers.Authorization?.Parameter);
            if (++calls > 1)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) };

            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"You have exceeded a secondary rate limit.\"}") };
            response.Headers.Add(RemainingHeader, "900");
            response.Headers.Add(ResetHeader, (clock.GetUtcNow().ToUnixTimeSeconds() + 3600).ToString(CultureInfo.InvariantCulture));
            return response;
        });

        using var client = new HttpClient(new WorkflowGitHubHandler(Token, handler, clock));
        var requests = new WorkflowGitHubRequests(clock, clock.Delay, _ => { });
        var result = await requests.RetryAsync(() => client.GetByteArrayAsync(MetadataUrl), CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2 }, result);
        Assert.Equal(TimeSpan.FromSeconds(61), Assert.Single(clock.Waits));
        Assert.Equal(2, calls);
    }

    /// <summary>
    /// Recovers signed discovery after its cached rate-limit deadline without passing credentials to release assets.
    /// </summary>
    [Fact]
    public async Task SignedReleaseDiscoveryRecoversAfterRateLimit()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var clock = new Clock();
        var apiCalls = 0;
        var handler = new Handler(request =>
        {
            byte[] bytes;
            if (request.RequestUri!.AbsoluteUri == ReleaseProtocol.LatestReleaseUrl)
            {
                Assert.Equal(Token, request.Headers.Authorization?.Parameter);
                if (++apiCalls == 1)
                    return new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(20)) } };

                bytes = ReleaseJson.Serialize(new { tag_name = TagPrefix + fixture.Manifest.IronmonVersion, draft = false, prerelease = false, html_url = ReleaseProtocol.ReleaseBaseUrl + TagSegment + TagPrefix + fixture.Manifest.IronmonVersion, assets = new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName }.Select(name => new { name, browser_download_url = ReleaseProtocol.AssetUrl(fixture.Manifest.IronmonVersion, name) }) });
            }
            else
            {
                Assert.Null(request.Headers.Authorization);
                bytes = request.RequestUri.AbsoluteUri.EndsWith(ReleaseProtocol.ManifestName, StringComparison.Ordinal) ? fixture.Evidence.Manifest : fixture.Evidence.Signatures;
            }

            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });

        using var http = new ReleaseHttpClient(new WorkflowGitHubHandler(Token, handler, clock), TimeProvider.System);
        using var discovery = new ReleaseDiscovery(http, fixture.Verifier, workspace.PathFor(CacheName), clock);
        var logs = new List<string>();
        var requests = new WorkflowGitHubRequests(clock, clock.Delay, logs.Add);
        var result = await requests.CheckReleaseAsync(discovery, CancellationToken.None);
        Assert.NotNull(result.Release);
        Assert.Equal(fixture.Manifest.IronmonVersion, fixture.Verifier.Verify(result.Release.Manifest, result.Release.Signatures).IronmonVersion);
        Assert.Equal(2, apiCalls);
        Assert.Equal(TimeSpan.FromSeconds(21), Assert.Single(clock.Waits));
        Assert.DoesNotContain(Token, Assert.Single(logs));
    }

    /// <summary>
    /// Stops immediately on a long reset window and reports retry timing without exposing the credential.
    /// </summary>
    [Fact]
    public async Task LongResetFailsWithoutWaitingOrRetrying()
    {
        var clock = new Clock();
        var calls = 0;
        var requests = new WorkflowGitHubRequests(clock, clock.Delay, _ => { });
        var error = await Assert.ThrowsAsync<IOException>(() => requests.RetryAsync<int>(() =>
        {
            calls++;
            throw new ReleaseRateLimitException(clock.GetUtcNow() + TimeSpan.FromHours(1));
        }, CancellationToken.None));

        Assert.Contains((clock.GetUtcNow() + TimeSpan.FromHours(1)).ToString("O"), error.Message);
        Assert.DoesNotContain(Token, error.Message);
        Assert.Equal(1, calls);
        Assert.Empty(clock.Waits);
    }

    /// <summary>
    /// Caps repeated short limits at three attempts instead of looping indefinitely.
    /// </summary>
    [Fact]
    public async Task RepeatedLimitsStopAfterThreeAttempts()
    {
        var clock = new Clock();
        var calls = 0;
        var requests = new WorkflowGitHubRequests(clock, clock.Delay, _ => { });
        await Assert.ThrowsAsync<IOException>(() => requests.RetryAsync<int>(() =>
        {
            calls++;
            throw new ReleaseRateLimitException(clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        }, CancellationToken.None));

        Assert.Equal(3, calls);
        Assert.Equal(2, clock.Waits.Count);
    }

    /// <summary>
    /// Shares the total wait budget across release discovery and upstream metadata lookups.
    /// </summary>
    [Fact]
    public async Task WaitBudgetIsSharedAcrossLookups()
    {
        var clock = new Clock();
        var requests = new WorkflowGitHubRequests(clock, clock.Delay, _ => { });
        var firstCalls = 0;
        Assert.Equal(1, await requests.RetryAsync(() => ++firstCalls == 1
            ? throw new ReleaseRateLimitException(clock.GetUtcNow() + TimeSpan.FromSeconds(70))
            : Task.FromResult(1), CancellationToken.None));

        var secondCalls = 0;
        await Assert.ThrowsAsync<IOException>(() => requests.RetryAsync<int>(() =>
        {
            secondCalls++;
            throw new ReleaseRateLimitException(clock.GetUtcNow() + TimeSpan.FromSeconds(50));
        }, CancellationToken.None));

        Assert.Equal(1, secondCalls);
        Assert.Single(clock.Waits);
    }

    /// <summary>
    /// Cancelling a rate-limit wait prevents another API request.
    /// </summary>
    [Fact]
    public async Task CancellationDuringWaitStopsRetrying()
    {
        var clock = new Clock();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var requests = new WorkflowGitHubRequests(clock, (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, _ => { });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requests.RetryAsync<int>(() =>
        {
            calls++;
            throw new ReleaseRateLimitException(clock.GetUtcNow() + TimeSpan.FromSeconds(20));
        }, cancellation.Token));

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Counts fixture requests without opening network connections.
    /// </summary>
    /// <remarks>Constructs a response callback for each HTTP request.</remarks>
    /// <param name="respond">The deterministic response factory.</param>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the number of dispatched requests.
        /// </summary>
        internal int Calls { get; private set; }

        /// <summary>
        /// Checks cancellation and supplies the next fixture response.
        /// </summary>
        /// <param name="request">The request being verified.</param>
        /// <param name="cancellationToken">The caller cancellation token.</param>
        /// <returns>The owned response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>
    /// Advances server time deterministically when the retry policy requests a wait.
    /// </summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Gets the delays requested by the production retry policy.
        /// </summary>
        internal List<TimeSpan> Waits { get; } = [];

        /// <summary>
        /// Returns the current fixture time.
        /// </summary>
        /// <returns>The deterministic UTC instant.</returns>
        public override DateTimeOffset GetUtcNow()
            => _now;

        /// <summary>Advances fixture time without actually blocking the test.</summary>
        /// <param name="duration">The requested wait.</param>
        /// <param name="cancellationToken">The cancellation token to preserve.</param>
        /// <returns>A completed wait.</returns>
        internal Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Waits.Add(duration);
            _now += duration;
            return Task.CompletedTask;
        }
    }
}
