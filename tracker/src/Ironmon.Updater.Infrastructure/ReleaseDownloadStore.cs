using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Caches release assets by authenticated content identity and never promotes a partial or corrupt download.
/// </summary>
/// <remarks>
/// Constructs a private download cache whose transport is owned by the caller.
/// </remarks>
/// <param name="root">The private user download directory.</param>
/// <param name="source">The bounded public artifact source.</param>
public sealed class ReleaseDownloadStore(string root, IArtifactSource source)
{
    private const string ArtifactSuffix = ".asset";
    private const string PartialSuffix = ".partial";
    private const int MaximumCachedContainers = 2;
    private readonly string _root = PlainPaths.Full(root);
    private readonly IArtifactSource _source = source;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ReleaseContainer, ReleaseMetadataDocument> _metadata = new();

    /// <summary>
    /// Gets the canonical cache root for destination-volume and overlap checks.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Reports actual bytes written for the current signed asset, including verified cache hits.
    /// </summary>
    public event Action<ReleaseDownloadProgress>? Progress;

    /// <summary>
    /// Returns a verified complete file, downloading into a unique temporary file when the cache cannot be reused.
    /// </summary>
    /// <param name="asset">The authenticated release artifact.</param>
    /// <param name="cancellationToken">The download cancellation token.</param>
    /// <returns>The path of a complete hash-verified cache file.</returns>
    public async Task<string> GetAsync(ReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        var expected = ReleaseProtocol.Content(asset.Bytes, asset.Sha256);
        var path = PlainPaths.Child(_root, expected.Sha256 + ArtifactSuffix);
        if (File.Exists(path) && await TransactionStorage.ContentAsync(path, cancellationToken).ConfigureAwait(false) == expected)
        {
            Progress?.Invoke(new(asset.Name, asset.Bytes, asset.Bytes, true));
            return path;
        }

        Directory.CreateDirectory(_root);
        var temporary = PlainPaths.Child(_root, Guid.NewGuid().ToString(TransactionStorage.GuidFormat) + PartialSuffix);
        try
        {
            InstallationProgressScope.Report(new(InstallationStage.DownloadingPackage, 0, asset.Bytes));
            Progress?.Invoke(new(asset.Name, 0, asset.Bytes, false));
            await using (var output = new DownloadStream(temporary, received => { InstallationProgressScope.Report(new(InstallationStage.DownloadingPackage, received, asset.Bytes, received)); Progress?.Invoke(new(asset.Name, received, asset.Bytes, false)); }))
            {
                if (asset.Container is { } container)
                {
                    if (container.Name != ReleaseMetadata.Name || container.Bytes is <= 0 or > ReleaseMetadata.Limit)
                        throw new InvalidDataException(UpdaterText.ReleaseDownloadStoreTheMetadataContainerIsUnsupportedOrOversized);

                    if (!_metadata.TryGetValue(container, out var metadata))
                    {
                        var containerAsset = new ReleaseAsset(container.Name, ReleaseMetadata.Role, asset.Url, container.Bytes, container.Sha256);
                        var containerPath = await GetAsync(containerAsset, cancellationToken).ConfigureAwait(false);
                        metadata = ReleaseMetadata.Parse(await File.ReadAllBytesAsync(containerPath, cancellationToken).ConfigureAwait(false), container);
                        if (_metadata.Count >= MaximumCachedContainers)
                            _metadata.Clear();

                        _metadata.TryAdd(container, metadata);
                    }

                    var bytes = ReleaseMetadata.Read(metadata, asset);
                    await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _source.CopyToAsync(new Uri(asset.Url), output, asset.Bytes, cancellationToken).ConfigureAwait(false);
                }

                output.Flush(true);
            }

            if (await TransactionStorage.ContentAsync(temporary, cancellationToken).ConfigureAwait(false) != expected)
                throw new InvalidDataException(UpdaterText.ReleaseDownloadStoreTheReleaseDownloadFailedItsSignedSizeOrSHA);

            PlainPaths.Full(path);
            File.Move(temporary, path, overwrite: true);
            Progress?.Invoke(new(asset.Name, asset.Bytes, asset.Bytes, true));
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(PlainPaths.Full(temporary));
        }
    }

    /// <summary>
    /// Measures streamed writes without estimating network or verification progress.
    /// </summary>
    /// <remarks>
    /// Opens a unique partial file and leaves publication to the signed cache verifier.
    /// </remarks>
    /// <param name="path">The private partial pathname.</param>
    /// <param name="report">The completed byte observation.</param>
    private sealed class DownloadStream(string path, Action<long> report) : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        private readonly Action<long> _report = report;

        /// <summary>
        /// Reports the actual output position after an asynchronous memory write.
        /// </summary>
        /// <param name="buffer">The downloaded bytes.</param>
        /// <param name="cancellationToken">The download token.</param>
        /// <returns>The completed write.</returns>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _report(Position);
        }

        /// <summary>
        /// Reports writes from transports using the array-based stream API.
        /// </summary>
        /// <param name="buffer">The downloaded bytes.</param>
        /// <param name="offset">The first byte to write.</param>
        /// <param name="count">The number of bytes.</param>
        /// <param name="cancellationToken">The download token.</param>
        /// <returns>The completed write.</returns>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            _report(Position);
        }
    }
}

/// <summary>
/// Describes byte progress for one authenticated asset without treating downloading as completed verification.
/// </summary>
/// <remarks>
/// Constructs a display observation; it never authorizes installing the asset.
/// </remarks>
/// <param name="AssetName">The signed asset name.</param>
/// <param name="Received">The bytes written or verified in the cache.</param>
/// <param name="Total">The signed expected byte length.</param>
/// <param name="Verified">Whether the complete hash and length have passed.</param>
public sealed record ReleaseDownloadProgress(string AssetName, long Received, long Total, bool Verified);
