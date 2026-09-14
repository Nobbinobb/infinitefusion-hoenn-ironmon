using System.Net;

namespace Ironmon.Setup.Tests;

/// <summary>
/// Exercises header-only prerequisite sizing, trusted redirects and exact reviewed package lengths without network access.
/// </summary>
public sealed class WebViewPackageDownloadTests
{
    private const string PackageUrl = "https://download.microsoft.com/runtime.exe";
    private const string UntrustedUrl = "https://example.invalid/runtime.exe";

    /// <summary>
    /// Reads no package body during review and downloads directly from the exact Microsoft URL that was reviewed.
    /// </summary>
    [Fact]
    public async Task ReviewOnlyReadsHeadersAndPinsTheResolvedDownload()
    {
        var requests = new List<(HttpMethod Method, Uri? Uri)>();
        using var handler = new Handler(request =>
        {
            requests.Add((request.Method, request.RequestUri));
            if (requests.Count == 1)
                return Redirect(PackageUrl);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = request.Method == HttpMethod.Head ? new HeaderContent(3) : new ByteArrayContent([1, 2, 3]) };
        });
        using var client = new HttpClient(handler);
        var package = new WebViewPackageDownload();
        Assert.Equal(3, await package.InspectAsync(client, CancellationToken.None));
        Assert.All(requests, request => Assert.Equal(HttpMethod.Head, request.Method));
        using var output = new MemoryStream();
        await package.DownloadAsync(client, output, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        Assert.Equal((HttpMethod.Get, new Uri(PackageUrl)), requests[^1]);
    }

    /// <summary>
    /// Refuses a changed advertised size, truncated bytes and excess bytes after successful review.
    /// </summary>
    /// <param name="length">The later advertised package length.</param>
    /// <param name="actual">The returned body length.</param>
    [Theory]
    [InlineData(4, 4)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    public async Task ChangedOrIncompleteDownloadsCannotReachExecution(long length, int actual)
    {
        using var handler = new Handler(request =>
        {
            HttpContent content = request.Method == HttpMethod.Head ? new HeaderContent(3) : new ByteArrayContent(new byte[actual]);
            if (request.Method == HttpMethod.Get)
                content.Headers.ContentLength = length;

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler);
        var package = new WebViewPackageDownload();
        Assert.Equal(3, await package.InspectAsync(client, CancellationToken.None));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => package.DownloadAsync(client, output, CancellationToken.None));
    }

    /// <summary>
    /// Treats unavailable header sizes as unknown while still allowing the consented, bounded installer download.
    /// </summary>
    [Fact]
    public async Task UnsupportedHeadDoesNotBlockInstallation()
    {
        using var handler = new Handler(request => request.Method == HttpMethod.Head ? new(HttpStatusCode.MethodNotAllowed) : new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = new HttpClient(handler);
        var package = new WebViewPackageDownload();
        Assert.Null(await package.InspectAsync(client, CancellationToken.None));
        using var output = new MemoryStream();
        await package.DownloadAsync(client, output, CancellationToken.None);
        Assert.Equal(3, output.Length);
    }

    /// <summary>
    /// Rejects an untrusted redirect before making any request outside Microsoft's servers.
    /// </summary>
    [Fact]
    public async Task RedirectsCannotLeaveMicrosoft()
    {
        var calls = 0;
        using var handler = new Handler(_ => { calls++; return Redirect(UntrustedUrl); });
        using var client = new HttpClient(handler);
        var package = new WebViewPackageDownload();
        Assert.Null(await package.InspectAsync(client, CancellationToken.None));
        Assert.Equal(1, calls);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => package.DownloadAsync(client, output, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    /// <summary>
    /// Propagates user cancellation instead of converting it into a successful review with an unavailable size.
    /// </summary>
    [Fact]
    public async Task CancelledReviewRemainsCancelled()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WebViewPackageDownload().InspectAsync(client, cancellation.Token));
    }

    /// <summary>
    /// Creates an explicit fixture redirect.
    /// </summary>
    /// <param name="url">The next fixture address.</param>
    /// <returns>The redirect response.</returns>
    private static HttpResponseMessage Redirect(string url)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(url);
        return response;
    }

    /// <summary>
    /// Supplies deterministic responses without opening sockets.
    /// </summary>
    /// <remarks>Constructs an isolated request handler.</remarks>
    /// <param name="response">The response factory.</param>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        /// <summary>
        /// Returns the fixture response while preserving cancellation.
        /// </summary>
        /// <param name="request">The observed request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The fixture response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    /// <summary>
    /// Advertises a size but fails if review attempts to consume a response body.
    /// </summary>
    /// <remarks>Constructs header-only fixture content.</remarks>
    /// <param name="length">The advertised complete package size.</param>
    private sealed class HeaderContent(long length) : HttpContent
    {
        /// <summary>
        /// Rejects body consumption during a header-only request.
        /// </summary>
        /// <param name="stream">The unused destination stream.</param>
        /// <param name="context">The unused transport context.</param>
        /// <returns>No body read is permitted.</returns>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("Review must not download the prerequisite body.");

        /// <summary>
        /// Supplies the advertised metadata size.
        /// </summary>
        /// <param name="value">The returned size.</param>
        /// <returns>True because the fixture advertises a complete length.</returns>
        protected override bool TryComputeLength(out long value)
        {
            value = length;
            return true;
        }
    }
}
