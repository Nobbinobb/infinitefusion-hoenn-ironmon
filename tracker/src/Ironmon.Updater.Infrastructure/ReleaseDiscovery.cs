using System.Text.Json;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Performs non-blocking stable discovery with authenticated caching, conditional requests and monotonic release observations.
/// </summary>
public sealed class ReleaseDiscovery : IUpdateReleaseProvider, IDisposable
{
    private const string CacheName = "discovery.json";
    private const string DraftProperty = "draft";
    private const string PrereleaseProperty = "prerelease";
    private const string TagProperty = "tag_name";
    private const string HtmlProperty = "html_url";
    private const string AssetsProperty = "assets";
    private const string NameProperty = "name";
    private const string DownloadProperty = "browser_download_url";
    private const string TagSegment = "tag/";
    private const string TagPrefix = "v";
    private readonly ReleaseHttpClient _http;
    private readonly ReleaseVerifier _verifier;
    private readonly TimeProvider _clock;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReleaseDiscoveryResult? _startupObservation;

    /// <summary>
    /// Creates a per-installation discovery client without performing startup I/O or network requests.
    /// </summary>
    /// <param name="http">The bounded public release transport.</param>
    /// <param name="verifier">The independently configured updater trust.</param>
    /// <param name="cacheDirectory">The installation-specific user cache outside the game.</param>
    /// <param name="clock">The cache and retry clock.</param>
    public ReleaseDiscovery(ReleaseHttpClient http, ReleaseVerifier verifier, string cacheDirectory, TimeProvider? clock = null)
    {
        _http = http;
        _verifier = verifier;
        _clock = clock ?? TimeProvider.System;
        _cachePath = PlainPaths.Child(PlainPaths.Full(cacheDirectory), CacheName);
    }

    /// <summary>
    /// Bridges authenticated discovery to the tracker-independent startup probe.
    /// </summary>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The latest authenticated target, or null when checking is unavailable.</returns>
    public async Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var result = await CheckAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Release is null)
            return null;

        var manifest = _verifier.Verify(result.Release.Manifest, result.Release.Signatures);
        return new UpdateRelease(ReleaseProtocol.ParseVersion(manifest.IronmonVersion), manifest.Game.PreferredCommit);
    }

    /// <summary>
    /// Checks once per startup or on explicit manual request while respecting server rate limits and six-hour successful caching.
    /// </summary>
    /// <param name="manual">Whether to bypass the successful-check cache.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>A non-fatal availability observation; caller cancellation still propagates.</returns>
    public async Task<ReleaseDiscoveryResult> CheckAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!manual && _startupObservation is not null)
                return _startupObservation;

            var now = _clock.GetUtcNow();
            DiscoveryCache? cache = null;
            try
            {
                cache = File.Exists(_cachePath) ? ReleaseJson.Parse<DiscoveryCache>(TransactionStorage.ReadBytes(_cachePath), 4 * ReleaseJson.ManifestLimit) : null;
                var previous = cache?.Release is null ? null : _verifier.Verify(cache.Release.Manifest, cache.Release.Signatures);
                if (cache is not null && (cache.SchemaVersion != 1 || cache.HighestSequence != (previous?.ReleaseSequence ?? 0)))
                    throw new InvalidDataException(UpdaterText.ReleaseDiscoveryTheReleaseCacheWatermarkDoesNotMatchItsAuthenticated);

                if (cache?.RetryAt > now)
                    return Observe(new ReleaseDiscoveryResult(null, now, cache.RetryAt, UpdaterText.ReleaseDiscoveryReleaseCheckingIsTemporarilyRateLimited));

                if (!manual && cache?.Release is not null && cache.CheckedAt <= now && now - cache.CheckedAt < TimeSpan.FromHours(6))
                    return Observe(new ReleaseDiscoveryResult(cache.Release, cache.CheckedAt, null, null));

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var response = await _http.ReadAsync(new Uri(ReleaseProtocol.LatestReleaseUrl), ReleaseJson.ManifestLimit, cache?.ETag, deadline.Token).ConfigureAwait(false);
                ReleaseEvidence evidence;
                if (response.NotModified)
                {
                    evidence = cache?.Release ?? throw new InvalidDataException(UpdaterText.ReleaseDiscoveryAConditionalReleaseResponseHasNoAuthenticatedCachedRelease);
                }
                else
                {
                    var version = ValidateApi(response.Bytes);
                    var manifest = await _http.ReadAsync(new Uri(ReleaseProtocol.AssetUrl(version, ReleaseProtocol.ManifestName)), ReleaseJson.ManifestLimit, cancellationToken: deadline.Token).ConfigureAwait(false);
                    var signature = await _http.ReadAsync(new Uri(ReleaseProtocol.AssetUrl(version, ReleaseProtocol.SignatureName)), ReleaseJson.SignatureLimit, cancellationToken: deadline.Token).ConfigureAwait(false);
                    _verifier.Verify(manifest.Bytes, signature.Bytes, TagPrefix + version);
                    evidence = new ReleaseEvidence(manifest.Bytes, signature.Bytes, []);
                }

                var verified = _verifier.Verify(evidence.Manifest, evidence.Signatures);
                if (previous is not null && (verified.ReleaseSequence < previous.ReleaseSequence || ReleaseProtocol.ParseVersion(verified.IronmonVersion) < ReleaseProtocol.ParseVersion(previous.IronmonVersion) || verified.ReleaseSequence == previous.ReleaseSequence && TransactionStorage.Hash(evidence.Manifest) != TransactionStorage.Hash(cache!.Release!.Manifest)))
                    throw new InvalidDataException(UpdaterText.ReleaseDiscoveryTheReleaseFeedAttemptedToRollBackOrReplace);

                var next = new DiscoveryCache(1, now, response.ETag ?? cache?.ETag, null, verified.ReleaseSequence, evidence);
                Save(next);
                return Observe(new ReleaseDiscoveryResult(evidence, now, null, null));
            }
            catch (ReleaseRateLimitException error)
            {
                try
                {
                    Save(new DiscoveryCache(1, cache?.CheckedAt ?? DateTimeOffset.MinValue, cache?.ETag, error.RetryAt, cache?.HighestSequence ?? 0, cache?.Release));
                }
                catch (Exception storageError) when (storageError is IOException or UnauthorizedAccessException)
                {
                    return Observe(new ReleaseDiscoveryResult(null, now, error.RetryAt, storageError.Message));
                }

                return Observe(new ReleaseDiscoveryResult(null, now, error.RetryAt, error.Message));
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is IOException or InvalidDataException or HttpRequestException or JsonException or FormatException or OperationCanceledException or System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
            {
                var message = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }
                    ? UpdaterText.ReleaseDiscoveryNoPubliclyAccessibleIronmonReleaseWasFoundCheckThat
                    : error is HttpRequestException or OperationCanceledException
                        ? UpdaterText.ReleaseDiscoveryTheReleaseServiceCouldNotBeReachedCheckYour
                        : error.Message;
                return Observe(new ReleaseDiscoveryResult(null, now, null, message));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Validates only the needed public GitHub fields while allowing unrelated API metadata to evolve.
    /// </summary>
    /// <param name="bytes">The bounded API document.</param>
    /// <returns>The stable version attached to the fixed repository release.</returns>
    private static string ValidateApi(byte[] bytes)
    {
        ReleaseJson.Validate(bytes, ReleaseJson.ManifestLimit);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var tag = root.GetProperty(TagProperty).GetString() ?? throw new InvalidDataException(UpdaterText.ReleaseDiscoveryTheReleaseTagIsMissing);
        if (root.GetProperty(DraftProperty).GetBoolean() || root.GetProperty(PrereleaseProperty).GetBoolean() || !tag.StartsWith(TagPrefix, StringComparison.Ordinal) || root.GetProperty(HtmlProperty).GetString() != ReleaseProtocol.ReleaseBaseUrl + TagSegment + tag)
            throw new InvalidDataException(UpdaterText.ReleaseDiscoveryOnlyPublishedStableReleasesFromTheFixedRepositoryAre);

        var version = tag[TagPrefix.Length..];
        ReleaseProtocol.ParseVersion(version);
        var assets = root.GetProperty(AssetsProperty).EnumerateArray().ToArray();
        if (assets.Length > 256 || assets.Select(asset => asset.GetProperty(NameProperty).GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assets.Length)
            throw new InvalidDataException(UpdaterText.ReleaseDiscoveryThePublicReleaseAssetNamesAreAmbiguous);

        foreach (var name in new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName })
        {
            var matching = assets.Where(asset => asset.GetProperty(NameProperty).GetString() == name).ToArray();
            if (matching.Length != 1 || matching[0].GetProperty(DownloadProperty).GetString() != ReleaseProtocol.AssetUrl(version, name))
                throw new InvalidDataException(UpdaterText.ReleaseDiscoveryTheReleaseIsMissingItsRepositoryBoundManifestOr);
        }

        return version;
    }

    /// <summary>
    /// Records the one automatic observation while allowing explicit checks to refresh it.
    /// </summary>
    /// <param name="result">The completed observation.</param>
    /// <returns>The supplied observation.</returns>
    private ReleaseDiscoveryResult Observe(ReleaseDiscoveryResult result)
    {
        _startupObservation = result;
        return result;
    }

    /// <summary>
    /// Persists a complete checked cache generation without placing mutable cache data in the game directory.
    /// </summary>
    /// <param name="cache">The complete cache state.</param>
    private void Save(DiscoveryCache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        TransactionStorage.WriteDurable(_cachePath, ReleaseJson.Serialize(cache));
    }

    /// <summary>
    /// Releases the request serialization gate; the transport remains owned by its caller.
    /// </summary>
    public void Dispose()
        => _gate.Dispose();

    /// <summary>
    /// Stores the signed high-water observation and conditional-request metadata.
    /// </summary>
    /// <remarks>
    /// Constructs one cache generation; its embedded release is reauthenticated on every new process startup.
    /// </remarks>
    /// <param name="SchemaVersion">The cache schema.</param>
    /// <param name="CheckedAt">The last successful request time.</param>
    /// <param name="ETag">The API entity tag.</param>
    /// <param name="RetryAt">The server retry boundary.</param>
    /// <param name="HighestSequence">The sequence of the authenticated highest observation.</param>
    /// <param name="Release">The exact signed highest observed release.</param>
    private sealed record DiscoveryCache(int SchemaVersion, DateTimeOffset CheckedAt, string? ETag, DateTimeOffset? RetryAt, long HighestSequence, ReleaseEvidence? Release);
}

/// <summary>
/// Reports release availability independently of tracker startup and gameplay.
/// </summary>
/// <remarks>
/// Constructs a completed non-fatal check result.
/// </remarks>
/// <param name="Release">The authenticated release evidence, if available.</param>
/// <param name="CheckedAt">The observation time.</param>
/// <param name="RetryAt">The earliest permitted retry after rate limiting.</param>
/// <param name="Error">The check failure for optional display or diagnostics.</param>
public sealed record ReleaseDiscoveryResult(ReleaseEvidence? Release, DateTimeOffset CheckedAt, DateTimeOffset? RetryAt, string? Error);
