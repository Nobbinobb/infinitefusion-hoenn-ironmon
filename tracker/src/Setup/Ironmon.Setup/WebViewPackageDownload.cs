using System.IO;
using System.Net;
using System.Net.Http;
using Ironmon.Updater.Core;

namespace Ironmon.Setup;

/// <summary>
/// Observes and downloads Microsoft's complete x64 runtime package through a bounded, Microsoft-only redirect chain.
/// </summary>
internal sealed class WebViewPackageDownload
{
    private const string DownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2124701";
    private const string MicrosoftDomain = ".microsoft.com";
    private const string MicrosoftHost = "microsoft.com";
    internal const long MaximumBytes = 512L * 1024 * 1024;
    private Uri? _reviewedUri;
    private long? _reviewedBytes;

    /// <summary>
    /// Reads only response headers and remembers the exact resolved package for subsequent installation.
    /// </summary>
    /// <param name="client">A client with automatic redirects disabled and a bounded timeout.</param>
    /// <param name="cancellationToken">The review token.</param>
    /// <returns>The complete package size, or null when the metadata request is unavailable.</returns>
    internal async Task<long?> InspectAsync(HttpClient client, CancellationToken cancellationToken)
    {
        _reviewedUri = null;
        _reviewedBytes = null;
        try
        {
            var (response, uri) = await RequestAsync(client, new Uri(DownloadUrl), HttpMethod.Head, cancellationToken).ConfigureAwait(false);
            using (response)
            {
                var size = response.Content.Headers.ContentLength;
                if (size is null or <= 0 or > MaximumBytes)
                    return null;

                _reviewedUri = uri;
                _reviewedBytes = size;
                return size;
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the reviewed package and rejects changed, truncated or oversized content before publisher verification.
    /// </summary>
    /// <param name="client">A client with automatic redirects disabled.</param>
    /// <param name="output">The owned destination stream.</param>
    /// <param name="cancellationToken">The download token.</param>
    internal async Task DownloadAsync(HttpClient client, Stream output, CancellationToken cancellationToken)
    {
        var (response, _) = await RequestAsync(client, _reviewedUri ?? new Uri(DownloadUrl), HttpMethod.Get, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            var expected = _reviewedBytes ?? response.Content.Headers.ContentLength;
            if (expected is <= 0 or > MaximumBytes || _reviewedBytes is { } reviewed && response.Content.Headers.ContentLength is { } received && reviewed != received)
                throw new IOException(UpdaterText.SetupRuntimePackageChanged);

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                total += count;
                if (total > MaximumBytes || expected is { } limit && total > limit)
                    throw new IOException(UpdaterText.SetupRuntimePackageTooLarge);

                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }

            if (total == 0 || expected is { } bytes && total != bytes)
                throw new IOException(UpdaterText.SetupRuntimePackageIncomplete);
        }
    }

    /// <summary>
    /// Validates every redirect before contacting the destination and returns an owned successful response.
    /// </summary>
    /// <param name="client">The redirect-disabled HTTP client.</param>
    /// <param name="uri">The initial Microsoft address.</param>
    /// <param name="method">The header-only or download method.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The successful response and its final validated address.</returns>
    private static async Task<(HttpResponseMessage Response, Uri Uri)> RequestAsync(HttpClient client, Uri uri, HttpMethod method, CancellationToken cancellationToken)
    {
        for (var redirects = 0; ; redirects++)
        {
            if (redirects > 5 || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || !(uri.Host.Equals(MicrosoftHost, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(MicrosoftDomain, StringComparison.OrdinalIgnoreCase)))
                throw new IOException(UpdaterText.WindowsSetupPlatformThePrerequisiteDownloadDidNotRemainOnMicrosoftS);

            using var request = new HttpRequestMessage(method, uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            try
            {
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
                {
                    uri = new Uri(uri, response.Headers.Location ?? throw new IOException(UpdaterText.WindowsSetupPlatformMicrosoftSDownloadRedirectIsIncomplete));
                    response.Dispose();
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return (response, uri);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }
}
