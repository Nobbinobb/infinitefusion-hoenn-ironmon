using System.Net;
using System.Net.Http.Headers;
using Ironmon.Setup.Core;
using Ironmon.SpriteLibrary;
using Ironmon.Updater.Infrastructure;
using Ironmon.Tracker.Connection.Sprites;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies shared target selection, header-only measurement, feed freshness and installer totals without network access.
/// </summary>
public sealed class SpriteDownloadEstimateTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string PngType = "image/png";
    private const string MissingSheet = "/2.png";
    private const string GameExecutable = "InfiniteFusion2.exe";
    private const string CustomManifest = "Data/sprites/CUSTOM_SPRITES";
    private const string BaseManifest = "Data/sprites/BASE_SPRITES";
    private static readonly string[] _custom = ["1.2.png", "1.3.png", "1.2a.png", "not-a-sprite"];
    private static readonly string[] _normal = ["1.png", "1a.png", "2.png"];

    /// <summary>
    /// Counts shared fusion and base sheets once and never reads their image bodies.
    /// </summary>
    [Fact]
    public async Task MeasurementReusesThePlayerTargetsAndOnlyRequestsHeaders()
    {
        var requests = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.EndsWith(MissingSheet, StringComparison.Ordinal) ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new HeadersOnly(100) };
        });
        using var client = new HttpClient(handler);
        var result = await SpriteDownloadMeasurement.MeasureAsync(client, Commit, _custom, _normal, CancellationToken.None);
        Assert.Equal(4, requests.Count);
        Assert.Equal(4, requests.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(300, result.Bytes);
        Assert.Equal(3, result.AvailableSheets);
        Assert.Equal(1, result.UnavailableSheets);

        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.PathFor(CustomManifest))!);
        File.WriteAllBytes(workspace.PathFor(GameExecutable), [77, 90]);
        File.WriteAllLines(workspace.PathFor(CustomManifest), _custom);
        File.WriteAllLines(workspace.PathFor(BaseManifest), _normal);
        using var installer = new CustomSpriteSheetInstaller();
        var plan = installer.CreatePlan(workspace.Root);
        Assert.Equal(requests.Order(StringComparer.Ordinal), plan.PendingSheets.Select(target => CustomSpriteSheetInstaller.GetResourceUri(target).AbsolutePath).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Does not publish a partial estimate after an ambiguous or unsuccessful response.
    /// </summary>
    /// <param name="status">The unsupported header response.</param>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task IncompleteScansFail(HttpStatusCode status)
    {
        using var handler = new Handler(_ => new(status));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => SpriteDownloadMeasurement.MeasureAsync(client, Commit, _custom, _normal, CancellationToken.None));
    }

    /// <summary>
    /// Rejects a successful response without an accurate positive PNG content length.
    /// </summary>
    [Fact]
    public async Task MissingSizeIsNotCountedAsZero()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new HeadersOnly(0) });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => SpriteDownloadMeasurement.MeasureAsync(client, Commit, _custom, _normal, CancellationToken.None));
    }

    /// <summary>
    /// Respects a host request to stop instead of repeatedly querying it ahead of its retry window.
    /// </summary>
    [Fact]
    public async Task LongRetryWindowKeepsThePreviousEstimate()
    {
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return response;
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => SpriteDownloadMeasurement.MeasureAsync(client, Commit, [], ["1.png"], CancellationToken.None));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Binds complete estimates to one game commit and rejects expired or future observations.
    /// </summary>
    [Fact]
    public void FeedIsBoundedDatedAndGameSpecific()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new SpriteDownloadEstimateDocument(1, now, [new(Commit, 6000, 3, 1)]);
        var bytes = SpriteDownloadEstimates.Serialize(document);
        Assert.Equal(6000, SpriteDownloadEstimates.Parse(bytes, Commit, now)!.Bytes);
        Assert.Null(SpriteDownloadEstimates.Parse(bytes, OtherCommit, now));
        Assert.Null(SpriteDownloadEstimates.Parse(bytes, Commit, now.AddDays(8)));
        Assert.Throws<InvalidDataException>(() => SpriteDownloadEstimates.Parse(bytes, Commit, now.AddHours(-1)));
        Assert.Throws<InvalidDataException>(() => SpriteDownloadEstimates.Serialize(document with { Games = [document.Games[0], document.Games[0]] }));
        Assert.Throws<InvalidDataException>(() => SpriteDownloadEstimates.Serialize(document with { Games = [new(Commit, 0, 3, 1)] }));
        Assert.Throws<InvalidDataException>(() => SpriteDownloadEstimates.Parse(new byte[SpriteDownloadEstimates.MaximumDocumentBytes + 1], Commit, now));
    }

    /// <summary>
    /// Fetches only the fixed metadata endpoint, keeps feed failure optional, and respects cancellation.
    /// </summary>
    [Fact]
    public async Task OptionalFeedFailureDoesNotBlockSetup()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Uri.UriSchemeHttps, request.RequestUri!.Scheme);
            return new(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        Assert.Null(await SpriteDownloadEstimates.ReadAsync(client, Commit, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SpriteDownloadEstimates.ReadAsync(client, Commit, cancellation.Token));
    }

    /// <summary>
    /// Includes the daily sprite estimate only when selected and removes the incomplete-total marker.
    /// </summary>
    [Fact]
    public void DailySpriteEstimateParticipatesOnlyWhenSelected()
    {
        var review = new SetupReview(null!, null!, null!, 100, false, false);
        var selected = SetupDownloadSummary.Create(review, false, null, true, 6000);
        Assert.Equal(6100, selected.KnownBytes);
        Assert.True(selected.Estimated);
        Assert.False(selected.Incomplete);
        Assert.Equal(100, SetupDownloadSummary.Create(review, false, null, false, 6000).KnownBytes);
        Assert.True(SetupDownloadSummary.Create(review, false, null, true).Incomplete);
    }

    /// <summary>
    /// Supplies isolated deterministic HTTP responses.
    /// </summary>
    /// <remarks>Constructs a request observer and response factory.</remarks>
    /// <param name="response">The fake response factory.</param>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        /// <summary>
        /// Returns an in-memory response while preserving cancellation.
        /// </summary>
        /// <param name="request">The observed request.</param>
        /// <param name="cancellationToken">The request token.</param>
        /// <returns>The fake response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    /// <summary>
    /// Exposes PNG size metadata and fails if any code tries to download the image body.
    /// </summary>
    private sealed class HeadersOnly : HttpContent
    {
        private readonly long _length;

        /// <summary>
        /// Creates a fake PNG response that is valid only for header inspection.
        /// </summary>
        /// <param name="length">The advertised image bytes.</param>
        public HeadersOnly(long length)
        {
            _length = length;
            Headers.ContentType = new MediaTypeHeaderValue(PngType);
        }

        /// <summary>
        /// Rejects any attempted body consumption.
        /// </summary>
        /// <param name="stream">The unused output stream.</param>
        /// <param name="context">The unused transport context.</param>
        /// <returns>No body is available.</returns>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("A size scan must not download sprite bodies.");

        /// <summary>
        /// Returns the advertised image size.
        /// </summary>
        /// <param name="length">The declared content length.</param>
        /// <returns>True because this fixture knows its length.</returns>
        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
