using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using Ironmon.Tracker.Collection;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Reassembles bounded transport fragments and acknowledges only committed gameplay prefixes.
/// </summary>
/// <remarks>
/// Initializes a connection-scoped fragment receiver using the independent store, negotiated protocol session and operational diagnostics.
/// </remarks>
/// <param name="store">The independent durable collector that commits accepted gameplay prefixes.</param>
/// <param name="requests">The negotiated protocol session used to send acknowledgements and rejections.</param>
/// <param name="diagnostics">The bounded operational error sink; gameplay payloads are not logged here.</param>
/// <param name="achievementNotifications">Whether the game advertised the earned-notification protocol.</param>
internal sealed class GameplayCollectionReceiver(GameplayCollectionStore store, TrackerRequestSession requests, TrackerDiagnosticsStore diagnostics, bool achievementNotifications = false) : IDisposable
{
    private const string _achievementIds = "ids";
    private const string TransferIdKey = "transfer_id";
    private const string IndexKey = "index";
    private const string CountKey = "count";
    private const string TotalBytesKey = "total_bytes";
    private const string DataKey = "data";
    private const string DigestKey = "sha256";
    private const string FailureCode = "collection_not_committed";
    private const string InvalidFragment = "Invalid gameplay collection fragment.";
    private const string EncodingVersionKey = "encoding_version";
    private const string UncompressedBytesKey = "uncompressed_bytes";
    private MemoryStream? _assembly;
    private string? _transferId;
    private string? _digest;
    private int _nextIndex;
    private int _count;
    private int _totalBytes;

    /// <summary>
    /// Accepts a collection message independently of live tracker presentation.
    /// </summary>
    /// <param name="message">The complete collection batch or one bounded physical transport fragment.</param>
    /// <param name="cancellationToken">Cancels storage waits and protocol sends for this connection.</param>
    /// <returns>A task that completes after the fragment is accepted or a committed-prefix acknowledgement or rejection is sent.</returns>
    public async Task ProcessAsync(TrackerMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (message.Event == GameplayCollectionProtocol.AchievementPoll)
            {
                await SendAchievementNoticesAsync(message.RunId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message.Event == GameplayCollectionProtocol.AchievementAcknowledged)
            {
                JsonElement ids = message.Payload.GetProperty(_achievementIds);
                if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() > GameplayCollectionStore.AchievementCount)
                    throw new InvalidDataException("Invalid achievement display acknowledgement.");

                HashSet<Guid> displayed = [.. ids.EnumerateArray().Select(value => Guid.Parse(value.GetString()!))];
                await store.AcknowledgeAchievementsAsync(displayed, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message.Event == GameplayCollectionProtocol.CompressedBatch)
            {
                using JsonDocument batch = DecodeCompressedBatch(message.Payload);
                JsonElement acknowledgement = await store.AcceptBatchAsync(batch.RootElement, cancellationToken).ConfigureAwait(false);
                await requests.SendEventAsync(GameplayCollectionProtocol.Acknowledged, acknowledgement, message.RunId, cancellationToken).ConfigureAwait(false);
                await SendAchievementNoticesAsync(message.RunId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message.Event == GameplayCollectionProtocol.Batch)
            {
                JsonElement acknowledgement = await store.AcceptBatchAsync(message.Payload, cancellationToken).ConfigureAwait(false);
                await requests.SendEventAsync(GameplayCollectionProtocol.Acknowledged, acknowledgement, message.RunId, cancellationToken).ConfigureAwait(false);
                await SendAchievementNoticesAsync(message.RunId, cancellationToken).ConfigureAwait(false);
                return;
            }

            JsonElement fragment = message.Payload;
            string transferId = fragment.GetProperty(TransferIdKey).GetString() ?? throw new InvalidDataException(InvalidFragment);
            string digest = fragment.GetProperty(DigestKey).GetString() ?? throw new InvalidDataException(InvalidFragment);
            int index = fragment.GetProperty(IndexKey).GetInt32();
            int count = fragment.GetProperty(CountKey).GetInt32();
            int totalBytes = fragment.GetProperty(TotalBytesKey).GetInt32();
            byte[] bytes = fragment.GetProperty(DataKey).GetBytesFromBase64();
            if (transferId.Length != 64 || digest.Length != 64 || transferId != digest || count < 1 || count > 128 || index < 0 || index >= count || bytes.Length > GameplayCollectionProtocol.MaximumChunkBytes || totalBytes < 1 || totalBytes > GameplayCollectionProtocol.MaximumBatchBytes)
                throw new InvalidDataException(InvalidFragment);

            if (index == 0)
            {
                Dispose();
                _assembly = new MemoryStream(totalBytes);
                _transferId = transferId;
                _digest = digest;
                _nextIndex = 0;
                _count = count;
                _totalBytes = totalBytes;
            }

            if (_assembly is null || _transferId != transferId || _digest != digest || index != _nextIndex || count != _count || totalBytes != _totalBytes || _assembly.Length + bytes.Length > totalBytes)
                throw new InvalidDataException(InvalidFragment);

            _assembly.Write(bytes);
            _nextIndex++;
            if (_nextIndex == count)
            {
                if (_assembly.Length != totalBytes || !Convert.ToHexStringLower(SHA256.HashData(_assembly.GetBuffer().AsSpan(0, totalBytes))).Equals(digest, StringComparison.Ordinal))
                    throw new InvalidDataException(InvalidFragment);

                using JsonDocument batch = JsonDocument.Parse(_assembly.GetBuffer().AsMemory(0, totalBytes));
                JsonElement acknowledgement = await store.AcceptBatchAsync(batch.RootElement, cancellationToken).ConfigureAwait(false);
                await requests.SendEventAsync(GameplayCollectionProtocol.Acknowledged, acknowledgement, message.RunId, cancellationToken).ConfigureAwait(false);
                await SendAchievementNoticesAsync(message.RunId, cancellationToken).ConfigureAwait(false);
                Dispose();
                return;
            }

            await requests.SendEventAsync(GameplayCollectionProtocol.ChunkAcknowledged, new { TransferId = transferId, Index = index }, message.RunId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException)
        {
            Dispose();
            diagnostics.RecordError(exception);
            await requests.SendEventAsync(GameplayCollectionProtocol.Rejected, new { Code = FailureCode, Message = exception.Message }, message.RunId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Delivers only committed earned metadata while preserving the durable queue until display acknowledgement.
    /// </summary>
    /// <param name="runId">The current connection run routing identity, or null when unavailable.</param>
    /// <param name="cancellationToken">Cancels waiting or sending without deleting pending notices.</param>
    /// <returns>A task completing after any pending earned notices have been sent.</returns>
    private async Task SendAchievementNoticesAsync(string? runId, CancellationToken cancellationToken)
    {
        if (!achievementNotifications)
            return;

        try
        {
            var notices = await store.GetAchievementsAsync(true, cancellationToken).ConfigureAwait(false);
            var batch = notices.Take(GameplayCollectionProtocol.MaximumAchievementNotices).ToArray();
            if (batch.Length == 0)
                return;

            var icons = await store.GetAchievementIconsAsync(batch.Select(notice => notice.Id), cancellationToken).ConfigureAwait(false);
            var payload = batch.Select(notice => new
            {
                notice.Id,
                notice.Name,
                Icon = new { icons[notice.Id].Width, icons[notice.Id].Height, icons[notice.Id].Pixels }
            }).ToArray();
            await requests.SendEventAsync(GameplayCollectionProtocol.AchievementNotifications, new { Unlocks = payload }, runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException)
        {
            diagnostics.RecordError(exception);
        }
    }

    /// <summary>
    /// Expands one negotiated batch within its exact length budget and verifies its original JSON digest.
    /// </summary>
    /// <param name="payload">The bounded compression envelope received from the producer.</param>
    /// <returns>An owned document containing the original batch; malformed or oversized input is rejected.</returns>
    private static JsonDocument DecodeCompressedBatch(JsonElement payload)
    {
        int expected = payload.GetProperty(UncompressedBytesKey).GetInt32();
        if (payload.GetProperty(EncodingVersionKey).GetInt32() != GameplayCollectionProtocol.CompressionVersion || expected < 1 || expected > GameplayCollectionProtocol.MaximumBatchBytes)
            throw new InvalidDataException(InvalidFragment);

        string digest = payload.GetProperty(DigestKey).GetString() ?? throw new InvalidDataException(InvalidFragment);
        byte[] compressed = payload.GetProperty(DataKey).GetBytesFromBase64();
        if (digest.Length != 64 || compressed.Length > GameplayCollectionProtocol.MaximumBatchBytes)
            throw new InvalidDataException(InvalidFragment);

        byte[] raw = new byte[expected];
        using MemoryStream source = new(compressed, false);
        using ZLibStream inflater = new(source, CompressionMode.Decompress);
        inflater.ReadExactly(raw);
        if (inflater.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(raw)) != digest)
            throw new InvalidDataException(InvalidFragment);

        return JsonDocument.Parse(raw);
    }

    /// <summary>
    /// Discards transport fragments; the producer retains the unacknowledged durable source.
    /// </summary>
    public void Dispose()
    {
        _assembly?.Dispose();
        _assembly = null;
        _transferId = null;
        _digest = null;
    }
}
