using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.SpriteSizeTool;

/// <summary>
/// Authenticates only the tool's two fixed GitHub API lookups and reports their rate-limit deadlines.
/// </summary>
internal sealed class WorkflowGitHubHandler : DelegatingHandler
{
    private const string MetadataReference = "https://api.github.com/repos/infinitefusion/pif-downloadables/git/ref/heads/master";
    private const string Bearer = "Bearer";
    private const string RemainingHeader = "X-RateLimit-Remaining";
    private const string ResetHeader = "X-RateLimit-Reset";
    private const string NoRemaining = "0";
    private const string MessageProperty = "message";
    private const string SecondaryLimitMessage = "secondary rate limit";
    private readonly string? _token;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates an isolated workflow transport that never follows redirects or inherits machine credentials.
    /// </summary>
    /// <param name="token">The optional short-lived workflow token, never read by player applications.</param>
    internal WorkflowGitHubHandler(string? token) : this(token, new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates a deterministic transport fixture with the same authentication restrictions.
    /// </summary>
    /// <param name="token">The optional workflow credential.</param>
    /// <param name="handler">The inner transport, which must not automatically follow redirects.</param>
    /// <param name="clock">The clock used to interpret server retry timing.</param>
    internal WorkflowGitHubHandler(string? token, HttpMessageHandler handler, TimeProvider clock) : base(handler)
    {
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
        _clock = clock;
    }

    /// <summary>
    /// Adds credentials per request only for exact allowed API URLs and disposes rate-limited responses.
    /// </summary>
    /// <param name="request">The outgoing metadata or sprite request.</param>
    /// <param name="cancellationToken">The caller's bounded request token.</param>
    /// <returns>The response when GitHub did not request a rate-limit pause.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var allowed = request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri is { } url
            && (url == ReleaseProtocol.LatestReleaseUrl || url == MetadataReference);
        request.Headers.Authorization = allowed && _token is not null ? new AuthenticationHeaderValue(Bearer, _token) : null;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var exhausted = response.Headers.TryGetValues(RemainingHeader, out var remaining) && remaining.Contains(NoRemaining);
            if (allowed && (response.StatusCode == HttpStatusCode.TooManyRequests
                || response.StatusCode == HttpStatusCode.Forbidden && (exhausted || response.Headers.RetryAfter is not null || await IsSecondaryLimitAsync(response, cancellationToken).ConfigureAwait(false))))
                throw new ReleaseRateLimitException(RetryAt(response, exhausted));

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Recognizes GitHub's secondary-limit response when no retry header is provided, without retrying ordinary permission errors.
    /// </summary>
    /// <param name="response">The forbidden API response.</param>
    /// <param name="cancellationToken">The bounded request token.</param>
    /// <returns>Whether the bounded JSON message identifies a secondary rate limit.</returns>
    private static async Task<bool> IsSecondaryLimitAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(65536, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(MessageProperty, out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString()!.Contains(SecondaryLimitMessage, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Honors both applicable server deadlines, with a conservative fallback when timing is absent.
    /// </summary>
    /// <param name="response">The rate-limited API response.</param>
    /// <param name="exhausted">Whether the primary quota is exhausted.</param>
    /// <returns>The earliest safe retry time.</returns>
    private DateTimeOffset RetryAt(HttpResponseMessage response, bool exhausted)
    {
        var now = _clock.GetUtcNow();
        var retryAt = response.Headers.RetryAfter?.Date;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            retryAt = now + delta;

        if (exhausted && response.Headers.TryGetValues(ResetHeader, out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds is >= 0 and <= 253402300799)
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (retryAt is null || reset > retryAt)
                retryAt = reset;
        }

        return retryAt > now ? retryAt.Value : now + (exhausted ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1));
    }
}
