using System.Net;
using Ironmon.Tracker.Connection.Sprites;

namespace Ironmon.SpriteLibrary;

/// <summary>
/// Measures a complete library with bounded header-only requests using the player's exact sheet-selection rules.
/// </summary>
public static class SpriteDownloadMeasurement
{
    private const string PngType = "image/png";

    /// <summary>
    /// Measures each unique sheet, distinguishing confirmed absence from failures that invalidate the whole scan.
    /// </summary>
    /// <param name="client">The redirect-disabled HTTP client.</param>
    /// <param name="gameCommit">The pinned game identity associated with the input manifests.</param>
    /// <param name="customManifest">The pinned custom-fusion manifest lines.</param>
    /// <param name="baseManifest">The pinned base-species manifest lines.</param>
    /// <param name="cancellationToken">The bounded scan token.</param>
    /// <returns>The complete estimate; transient or ambiguous responses never produce a partial total.</returns>
    public static async Task<SpriteGameDownloadEstimate> MeasureAsync(HttpClient client, string gameCommit, IEnumerable<string> customManifest, IEnumerable<string> baseManifest, CancellationToken cancellationToken)
    {
        var targets = CustomSpriteSheetInstaller.GetSheetTargets(customManifest, baseManifest);
        if (targets.Count is 0 or > 50000)
            throw new InvalidDataException("The sprite manifest has no supported bounded sheet selection.");

        long total = 0;
        int available = 0, unavailable = 0;
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (target, token) =>
        {
            var bytes = await ReadSizeAsync(client, CustomSpriteSheetInstaller.GetResourceUri(target), token).ConfigureAwait(false);
            if (bytes is { } size)
            {
                Interlocked.Add(ref total, size);
                Interlocked.Increment(ref available);
            }
            else
            {
                Interlocked.Increment(ref unavailable);
            }
        }).ConfigureAwait(false);
        if (available == 0 || total > 107374182400L)
            throw new InvalidDataException("The sprite library measurement is empty or exceeds its supported size.");

        return new SpriteGameDownloadEstimate(gameCommit, total, available, unavailable);
    }

    /// <summary>
    /// Reads fresh HEAD metadata, retrying a bounded number of temporary failures without downloading an image body.
    /// </summary>
    /// <param name="client">The redirect-disabled HTTP client.</param>
    /// <param name="uri">The sheet address produced by the shared manifest parser.</param>
    /// <param name="cancellationToken">The scan cancellation token.</param>
    /// <returns>The positive length, or null only for a confirmed missing resource.</returns>
    private static async Task<long?> ReadSizeAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    return null;

                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                    if (retry > TimeSpan.FromMinutes(1))
                        throw new InvalidDataException("The sprite host requested a later scan. Keeping the previous estimate.");

                    if (retry > TimeSpan.Zero)
                        await Task.Delay(retry.Value, cancellationToken).ConfigureAwait(false);

                    throw new HttpRequestException("The sprite host is temporarily unavailable.", null, response.StatusCode);
                }

                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                if (response.Content.Headers.ContentType?.MediaType != PngType || length is null or <= 0 or > 536870912)
                    throw new InvalidDataException("A sprite sheet has no valid PNG download length.");

                return length;
            }
            catch (Exception error) when (attempt < 2 && !cancellationToken.IsCancellationRequested && (error is OperationCanceledException || error is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500)))
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
