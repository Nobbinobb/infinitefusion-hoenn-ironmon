using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Copies a bounded artifact into private staging.
/// </summary>
public interface IArtifactSource
{
    /// <summary>
    /// Downloads an artifact without owning or closing the destination stream.
    /// </summary>
    /// <param name="source">The artifact address.</param>
    /// <param name="destination">The writable stream owned by the caller; partial content may remain on failure.</param>
    /// <param name="maximumBytes">The maximum permitted number of artifact bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that completes when the artifact has been copied within the byte limit.</returns>
    Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken);
}

/// <summary>
/// Downloads public GitHub artifacts with explicit HTTPS redirect restrictions.
/// </summary>
public sealed class GitHubArtifactSource : IArtifactSource, IDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false });
    private const string GitHubHost = "github.com";
    private const string AssetsHost = "release-assets.githubusercontent.com";
    private const string ObjectsHost = "objects.githubusercontent.com";
    private const int MaximumRedirects = 5;

    /// <summary>
    /// Initializes a bounded public downloader.
    /// </summary>
    public GitHubArtifactSource()
    {
        _client.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Downloads an artifact, restricting every redirect to approved public GitHub HTTPS hosts.
    /// </summary>
    /// <param name="source">The public GitHub HTTPS artifact address.</param>
    /// <param name="destination">The caller-owned writable stream, which remains open; partial content may remain on failure.</param>
    /// <param name="maximumBytes">The maximum permitted number of bytes, enforced while streaming the response.</param>
    /// <param name="cancellationToken">The token used to cancel the download and destination writes.</param>
    /// <returns>A task that completes when the artifact has been copied within the byte limit.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            if (source.Scheme != Uri.UriSchemeHttps || source.UserInfo.Length != 0 || !source.IsDefaultPort || source.Host is not (GitHubHost or AssetsHost or ObjectsHost))
                throw new InvalidDataException(UpdaterText.IArtifactSourceTheArtifactSourceIsNotAnAllowedPublicHTTPS);

            using var response = await _client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                source = new Uri(source, response.Headers.Location ?? throw new InvalidDataException(UpdaterText.IArtifactSourceMissingDownloadRedirect));
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new InvalidDataException(UpdaterText.IArtifactSourceTheArtifactExceedsItsDeclaredSize);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total = checked(total + count);
                if (total > maximumBytes)
                    throw new InvalidDataException(UpdaterText.IArtifactSourceTheArtifactExceedsItsDeclaredSize);

                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        throw new InvalidDataException(UpdaterText.IArtifactSourceTooManyDownloadRedirects);
    }

    /// <summary>
    /// Disposes the owned HTTP client and its handler.
    /// </summary>
    public void Dispose()
        => _client.Dispose();
}
