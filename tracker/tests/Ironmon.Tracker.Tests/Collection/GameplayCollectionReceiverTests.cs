using Ironmon.Tracker.Collection;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks physical fragment receipts never substitute for a durable gameplay acknowledgement.
/// </summary>
public sealed class GameplayCollectionReceiverTests
{
    private const string _profile = "11111111111111111111111111111111";
    private const string _epoch = "22222222222222222222222222222222";
    private const string _stream = "33333333333333333333333333333333";
    private const string _sourceType = "session.state_changed";
    private const string _connected = "connected";
    private const string _boot = "boot";
    private const string _one = "1";
    private const string _tablesProperty = "tables";
    private const string _receiptsTable = "stream_receipts";
    private const string _operationsProperty = "operations";
    private const string _acceptedEventsProperty = "accepted_events";
    private const string _prefixProperty = "durable_prefix";
    private const string _directoryPrefix = "Ironmon-Collection-receiver-";
    private const string _guidFormat = "N";
    private const string _lineSeparator = "\n";
    private const string _badDigest = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Commits an exact compressed batch once even when its complete transfer is repeated.
    /// </summary>
    /// <returns>Completes after checking both durable acknowledgements and the unchanged earned total.</returns>
    [Fact]
    public async Task CompressedBatchCommitsOnceAndAcceptsExactRetry()
    {
        await using ReceiverHarness harness = await ReceiverHarness.CreateAsync();
        TrackerMessage message = Compressed(BatchBytes(), 0);
        await harness.Receiver.ProcessAsync(message, CancellationToken.None);
        await harness.Receiver.ProcessAsync(message, CancellationToken.None);
        Assert.All(harness.Replies(), reply => Assert.Equal(GameplayCollectionProtocol.Acknowledged, reply.Event));
        JsonElement inspection = GameplayCollectionInspector.Read(harness.Directory);
        Assert.Equal(1, inspection.GetProperty(_operationsProperty).GetProperty(_acceptedEventsProperty).GetInt64());
    }

    /// <summary>
    /// Rejects a bad digest, truncated stream, unsupported format, expansion overflow or invalid length before committing.
    /// </summary>
    /// <param name="mutation">The malformed envelope variant from one through five.</param>
    /// <returns>Completes after confirming rejection without a database receipt.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidCompressedBatchDoesNotAdvanceReceipt(int mutation)
    {
        await using ReceiverHarness harness = await ReceiverHarness.CreateAsync();
        await harness.Receiver.ProcessAsync(Compressed(BatchBytes(), mutation), CancellationToken.None);
        Assert.Equal(GameplayCollectionProtocol.Rejected, Assert.Single(harness.Replies()).Event);
        Assert.Equal(0, GameplayCollectionInspector.Read(harness.Directory).GetProperty(_tablesProperty).GetProperty(_receiptsTable).GetInt64());
    }

    /// <summary>
    /// Builds an exact fast-zlib envelope or one explicitly malformed variant.
    /// </summary>
    /// <param name="raw">The original batch JSON bytes.</param>
    /// <param name="mutation">Zero for valid input, or a numbered invalid variant.</param>
    /// <returns>The complete compressed collection message.</returns>
    private static TrackerMessage Compressed(byte[] raw, int mutation)
    {
        using MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Fastest, true))
            compressor.Write(raw);

        byte[] compressed = output.ToArray();
        if (mutation == 2)
            compressed = compressed[..(compressed.Length / 2)];

        string digest = mutation == 1 ? _badDigest : Convert.ToHexStringLower(SHA256.HashData(raw));
        int length = mutation == 4 ? 1 : mutation == 5 ? GameplayCollectionProtocol.MaximumBatchBytes + 1 : raw.Length;
        return TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.CompressedBatch, 1, new { encoding_version = mutation == 3 ? 99 : 1, uncompressed_bytes = length, sha256 = digest, data = Convert.ToBase64String(compressed) });
    }

    /// <summary>
    /// Checks a partial transfer advances no gameplay receipt and remains safely inspectable.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task PartialChunkDoesNotAdvanceTheDatabaseReceipt()
    {
        await using ReceiverHarness harness = await ReceiverHarness.CreateAsync();
        byte[] bytes = BatchBytes();
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int split = bytes.Length / 2;
        await harness.Receiver.ProcessAsync(Fragment(bytes[..split], digest, 0, 2, bytes.Length), CancellationToken.None);
        TrackerMessage reply = Assert.Single(harness.Replies());
        Assert.Equal(GameplayCollectionProtocol.ChunkAcknowledged, reply.Event);
        Assert.Equal(0, GameplayCollectionInspector.Read(harness.Directory).GetProperty(_tablesProperty).GetProperty(_receiptsTable).GetInt64());
    }

    /// <summary>
    /// Checks a malformed transfer cannot persist or acknowledge gameplay.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task BadDigestIsRejectedWithoutCreatingAReceipt()
    {
        await using ReceiverHarness harness = await ReceiverHarness.CreateAsync();
        byte[] bytes = BatchBytes();
        await harness.Receiver.ProcessAsync(Fragment(bytes, _badDigest, 0, 1, bytes.Length), CancellationToken.None);
        TrackerMessage reply = Assert.Single(harness.Replies());
        Assert.Equal(GameplayCollectionProtocol.Rejected, reply.Event);
        Assert.Equal(0, GameplayCollectionInspector.Read(harness.Directory).GetProperty(_tablesProperty).GetProperty(_receiptsTable).GetInt64());
    }

    /// <summary>
    /// Checks completed and repeated physical transfers acknowledge one committed occurrence.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task RepeatedCompleteTransferDoesNotApplyTheOccurrenceTwice()
    {
        await using ReceiverHarness harness = await ReceiverHarness.CreateAsync();
        byte[] bytes = BatchBytes();
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int split = bytes.Length / 2;
        for (int repetition = 0; repetition < 2; repetition++)
        {
            await harness.Receiver.ProcessAsync(Fragment(bytes[..split], digest, 0, 2, bytes.Length), CancellationToken.None);
            await harness.Receiver.ProcessAsync(Fragment(bytes[split..], digest, 1, 2, bytes.Length), CancellationToken.None);
        }

        TrackerMessage[] replies = harness.Replies();
        Assert.Equal(4, replies.Length);
        Assert.Equal(GameplayCollectionProtocol.Acknowledged, replies[1].Event);
        Assert.Equal(GameplayCollectionProtocol.Acknowledged, replies[3].Event);
        Assert.Equal(_one, replies[3].Payload.GetProperty(_prefixProperty).GetString());
        JsonElement inspection = GameplayCollectionInspector.Read(harness.Directory);
        Assert.Equal(1, inspection.GetProperty(_tablesProperty).GetProperty(_receiptsTable).GetInt64());
        Assert.Equal(1, inspection.GetProperty(_operationsProperty).GetProperty(_acceptedEventsProperty).GetInt64());
    }

    /// <summary>
    /// Creates one normal collection batch without gameplay history or synthetic progress.
    /// </summary>
    /// <returns>The UTF-8 JSON bytes of one ordinary fixture batch with no imported history.</returns>
    private static byte[] BatchBytes()
        => JsonSerializer.SerializeToUtf8Bytes(new { schema_version = 1, profile_id = _profile, epoch_id = _epoch, stream_id = _stream, predecessor_stream_id = (string?)null, predecessor_final_sequence = (string?)null, events = new[] { new { sequence = _one, type = _sourceType, attempt_id = (string?)null, branch_id = (string?)null, battle_id = (string?)null, occurred_at_us = _one, context = new { }, payload = new { state_after = _connected, reason = _boot } } } });

    /// <summary>
    /// Creates a bounded physical fragment with its whole-batch digest.
    /// </summary>
    /// <param name="bytes">The encoded bytes carried by this physical fragment.</param>
    /// <param name="digest">The lowercase SHA-256 digest identifying the complete batch.</param>
    /// <param name="index">The zero-based position of this fragment in the transfer.</param>
    /// <param name="count">The total number of fragments in the transfer.</param>
    /// <param name="total">The complete encoded batch length in bytes.</param>
    /// <returns>The protocol chunk message with fragment coordinates, complete size and whole-batch digest.</returns>
    private static TrackerMessage Fragment(byte[] bytes, string digest, int index, int count, int total)
        => TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.Chunk, index + 1, new { transfer_id = digest, sha256 = digest, index, count, total_bytes = total, data = Convert.ToBase64String(bytes) });

    /// <summary>
    /// Owns one actual receiver, store, protocol writer and disposable profile directory.
    /// </summary>
    private sealed class ReceiverHarness : IAsyncDisposable
    {
        private readonly GameplayCollectionStore _store;
        private readonly MemoryStream _output = new();
        private readonly TrackerMessageWriter _writer;
        private readonly TrackerRequestSession _requests;

        /// <summary>
        /// Allocates the exact owned profile and in-memory response stream.
        /// </summary>
        private ReceiverHarness()
        {
            Directory = Path.Combine(Path.GetTempPath(), _directoryPrefix + Guid.NewGuid().ToString(_guidFormat));
            System.IO.Directory.CreateDirectory(Directory);
            _store = new GameplayCollectionStore(Directory);
            TrackerDiagnosticsStore diagnostics = new();
            _writer = new TrackerMessageWriter(_output, true);
            _requests = new TrackerRequestSession(diagnostics);
            _requests.Connect(_writer);
            Receiver = new GameplayCollectionReceiver(_store, _requests, diagnostics);
        }

        /// <summary>
        /// Gets the exact temporary directory.
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// Gets the real transport receiver under test.
        /// </summary>
        public GameplayCollectionReceiver Receiver { get; }

        /// <summary>
        /// Creates the harness after the background writer has initialized its schema.
        /// </summary>
        /// <returns>A task whose result is a disposable harness with an initialized real collection writer and receiver.</returns>
        public static async Task<ReceiverHarness> CreateAsync()
        {
            ReceiverHarness harness = new();
            await harness._store.GetMeasurementsAsync();
            return harness;
        }

        /// <summary>
        /// Decodes only protocol responses written by the receiver.
        /// </summary>
        /// <returns>Only the decoded protocol response messages emitted by the fixture receiver.</returns>
        public TrackerMessage[] Replies()
            => [.. Encoding.UTF8.GetString(_output.ToArray()).Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries).Select(TrackerMessageCodec.Deserialize)];

        /// <summary>
        /// Releases all handles before removing only this verified fixture directory.
        /// </summary>
        /// <returns>A value task that completes after fixture store/protocol handles close and the owned test directory is removed.</returns>
        public async ValueTask DisposeAsync()
        {
            Receiver.Dispose();
            _requests.Dispose();
            await _writer.DisposeAsync();
            _output.Dispose();
            await _store.DisposeAsync();
            string root = Path.GetFullPath(Path.GetTempPath());
            string owned = Path.GetFullPath(Directory);
            if (owned.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(owned).StartsWith(_directoryPrefix, StringComparison.Ordinal))
                System.IO.Directory.Delete(owned, true);
        }
    }
}
