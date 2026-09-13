using Ironmon.Updater.Core;
using System.Net;
using System.Net.Http.Headers;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reads public release metadata and artifacts through bounded HTTPS requests with explicit redirect and retry policies.
/// </summary>
public sealed class ReleaseHttpClient : IArtifactSource, IDisposable
{
    private const string ApiHost = "api.github.com";
    private const string GitHubHost = "github.com";
    private const string AssetsHost = "release-assets.githubusercontent.com";
    private const string ObjectsHost = "objects.githubusercontent.com";
    private const string UserAgent = "Ironmon-Updater/1.0";
    private const string ResetHeader = "X-RateLimit-Reset";
    private const string RemainingHeader = "X-RateLimit-Remaining";
    private const string NoRemaining = "0";
    private readonly HttpClient _client;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates an unauthenticated public client with no automatic redirects, cookies or inherited credentials.
    /// </summary>
    /// <param name="clock">The clock for retry deadlines.</param>
    public ReleaseHttpClient(TimeProvider? clock = null) : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }, clock ?? TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates an isolated deterministic transport fixture without weakening production URL validation.
    /// </summary>
    /// <param name="handler">The fixture HTTP transport.</param>
    /// <param name="clock">The retry clock.</param>
    internal ReleaseHttpClient(HttpMessageHandler handler, TimeProvider clock)
    {
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _clock = clock;
    }

    /// <summary>
    /// Reads one bounded metadata document with a ten-second request budget and conditional ETag support.
    /// </summary>
    /// <param name="source">The fixed API or repository-bound asset URL.</param>
    /// <param name="maximumBytes">The hard document limit.</param>
    /// <param name="etag">The previously validated API ETag, if available.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The complete bytes or a not-modified observation.</returns>
    public async Task<ReleaseHttpDocument> ReadAsync(Uri source, int maximumBytes, string? etag = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await SendAsync(source, etag, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new ReleaseHttpDocument(true, [], response.Headers.ETag?.ToString() ?? etag);

        await using var output = new MemoryStream();
        await CopyBodyAsync(response, output, maximumBytes, deadline.Token).ConfigureAwait(false);
        return new ReleaseHttpDocument(false, output.ToArray(), response.Headers.ETag?.ToString());
    }

    /// <summary>
    /// Streams one bounded artifact without credentials or unvalidated redirect hops.
    /// </summary>
    /// <param name="source">The repository-bound initial URL.</param>
    /// <param name="destination">The caller-owned output stream.</param>
    /// <param name="maximumBytes">The signed size limit.</param>
    /// <param name="cancellationToken">The download cancellation token.</param>
    /// <returns>A task that completes after the bounded body is copied.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        using var response = await SendAsync(source, null, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
            throw new InvalidDataException(UpdaterText.ReleaseHttpClientAnArtifactRequestReturnedNoBody);

        await CopyBodyAsync(response, destination, maximumBytes, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a fixed retry budget, validates each redirect and surfaces rate limits without busy retrying.
    /// </summary>
    /// <param name="source">The initial fixed-source URL.</param>
    /// <param name="etag">The optional API entity tag.</param>
    /// <param name="cancellationToken">The shared request deadline.</param>
    /// <returns>The owned successful response.</returns>
    private async Task<HttpResponseMessage> SendAsync(Uri source, string? etag, CancellationToken cancellationToken)
    {
        ValidateUrl(source, initial: true);
        var initial = source;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            source = initial;
            try
            {
                for (var redirects = 0; redirects <= 5; redirects++)
                {
                    ValidateUrl(source, initial: redirects == 0);
                    using var request = new HttpRequestMessage(HttpMethod.Get, source);
                    if (etag is not null && source.Host == ApiHost)
                        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));

                    var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                        {
                            source = new Uri(source, response.Headers.Location ?? throw new InvalidDataException(UpdaterText.ReleaseHttpClientTheReleaseRedirectHasNoDestination));
                            if (initial.Host == ApiHost)
                                throw new InvalidDataException(UpdaterText.ReleaseHttpClientTheFixedReleaseAPIMustNotRedirectToAnother);

                            response.Dispose();
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.TooManyRequests || (response.StatusCode == HttpStatusCode.Forbidden && (response.Headers.RetryAfter is not null || response.Headers.TryGetValues(RemainingHeader, out var remaining) && remaining.Contains(NoRemaining))))
                            throw new ReleaseRateLimitException(RetryAt(response));

                        if ((int)response.StatusCode >= 500 && attempt < 2)
                        {
                            response.Dispose();
                            break;
                        }

                        if (response.StatusCode != HttpStatusCode.NotModified)
                            response.EnsureSuccessStatusCode();

                        return response;
                    }
                    catch
                    {
                        response.Dispose();
                        throw;
                    }
                }
            }
            catch (HttpRequestException error) when (attempt < 2 && !cancellationToken.IsCancellationRequested && (error.StatusCode is null || (int)error.StatusCode >= 500))
            {
            }

            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), _clock, cancellationToken).ConfigureAwait(false);
        }

        throw new HttpRequestException(UpdaterText.ReleaseHttpClientTheReleaseRequestExceededItsRetryOrRedirectBudget);
    }

    /// <summary>
    /// Copies only the declared body budget, including responses that omit or misreport Content-Length.
    /// </summary>
    /// <param name="response">The validated response.</param>
    /// <param name="destination">The caller-owned destination.</param>
    /// <param name="maximumBytes">The hard byte limit.</param>
    /// <param name="cancellationToken">The shared deadline.</param>
    private static async Task CopyBodyAsync(HttpResponseMessage response, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumBytes, 0);
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException(UpdaterText.ReleaseHttpClientTheReleaseResponseExceedsItsSizeLimit);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
                throw new InvalidDataException(UpdaterText.ReleaseHttpClientTheReleaseResponseExceedsItsSizeLimit);

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rejects credentials, downgrades, alternate ports and unexpected repositories or CDN hosts.
    /// </summary>
    /// <param name="uri">The current request URL.</param>
    /// <param name="initial">Whether the URL is the initial repository-bound request.</param>
    private static void ValidateUrl(Uri uri, bool initial)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException(UpdaterText.ReleaseHttpClientTheReleaseURLIsNotACredentialFreePublic);

        var allowed = initial
            ? uri.AbsoluteUri == ReleaseProtocol.LatestReleaseUrl || uri.Host == GitHubHost && uri.AbsoluteUri.StartsWith(ReleaseProtocol.ReleaseBaseUrl + ReleaseProtocol.DownloadSegment, StringComparison.Ordinal)
            : uri.Host is AssetsHost or ObjectsHost || uri.Host == GitHubHost && uri.AbsoluteUri.StartsWith(ReleaseProtocol.ReleaseBaseUrl + ReleaseProtocol.DownloadSegment, StringComparison.Ordinal);

        if (!allowed)
            throw new InvalidDataException(UpdaterText.ReleaseHttpClientTheReleaseRequestOrRedirectIsOutsideTheFixed);
    }

    /// <summary>
    /// Uses server-provided retry timing, falling back to a conservative one-hour delay.
    /// </summary>
    /// <param name="response">The rate-limited response.</param>
    /// <returns>The earliest permitted next request.</returns>
    private DateTimeOffset RetryAt(HttpResponseMessage response)
    {
        var now = _clock.GetUtcNow();
        var retry = response.Headers.RetryAfter?.Date ?? now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
        if (response.Headers.TryGetValues(ResetHeader, out var values) && long.TryParse(values.FirstOrDefault(), out var seconds) && seconds is >= 0 and <= 253402300799)
            retry = DateTimeOffset.FromUnixTimeSeconds(seconds);

        return retry > now ? retry : now + TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Releases the owned HTTP transport.
    /// </summary>
    public void Dispose()
        => _client.Dispose();
}

/// <summary>
/// Carries a bounded metadata response and its conditional-request identity.
/// </summary>
/// <remarks>
/// Constructs a completed HTTP observation.
/// </remarks>
/// <param name="NotModified">Whether the server reused the prior entity.</param>
/// <param name="Bytes">The returned body.</param>
/// <param name="ETag">The server entity tag.</param>
public sealed record ReleaseHttpDocument(bool NotModified, byte[] Bytes, string? ETag);

/// <summary>
/// Carries an explicit server retry boundary without treating rate limits as an absent update.
/// </summary>
public sealed class ReleaseRateLimitException : IOException
{
    /// <summary>
    /// Gets the earliest permitted next release check.
    /// </summary>
    public DateTimeOffset RetryAt { get; }

    /// <summary>
    /// Initializes the server's retry boundary.
    /// </summary>
    /// <param name="retryAt">The earliest permitted retry.</param>
    public ReleaseRateLimitException(DateTimeOffset retryAt) : base(UpdaterText.ReleaseHttpClientReleaseCheckingIsTemporarilyRateLimited)
    {
        RetryAt = retryAt;
    }
}
