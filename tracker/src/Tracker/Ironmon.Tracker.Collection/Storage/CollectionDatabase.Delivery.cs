using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Implements ordered durable delivery and bounded reducer recovery.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _predecessorProperty = "predecessor_stream_id";
    private const string _predecessorSequenceProperty = "predecessor_final_sequence";
    private const string _openState = "open";
    private const string _collectorContextSuffix = "/collector";
    private const string _retirementSuffix = "/retirement";

    /// <summary>
    /// Registers an immutable descriptor only after a predecessor's durable frontier is present.
    /// </summary>
    /// <param name="batch">The incoming batch with the stream's immutable predecessor descriptor.</param>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    private void RegisterStream(JsonElement batch, byte[] stream)
    {
        string? predecessorText = batch.TryGetProperty(_predecessorProperty, out JsonElement predecessorValue) && predecessorValue.ValueKind == JsonValueKind.String ? predecessorValue.GetString() : null;
        string? requiredText = batch.TryGetProperty(_predecessorSequenceProperty, out JsonElement sequenceValue) && sequenceValue.ValueKind == JsonValueKind.String ? sequenceValue.GetString() : null;
        if ((predecessorText is null) != (requiredText is null))
            throw new InvalidDataException("Predecessor identity and frontier must be supplied together.");

        byte[] digest = SHA256.HashData(CollectionEncoding.Canonical(new { profile_id = CollectionEncoding.Identifier(_profile!), epoch_id = CollectionEncoding.Identifier(_epoch!), stream_id = CollectionEncoding.Identifier(stream), predecessor_stream_id = predecessorText, predecessor_final_sequence = requiredText }));
        StreamDescriptorRecord? descriptor = CollectionQueries.ReadStreamDescriptor(_db, _transaction, stream);
        if (descriptor is not null)
        {
            if (!descriptor.Digest.AsSpan().SequenceEqual(digest))
                throw new InvalidDataException("A registered collection stream descriptor cannot change.");

            return;
        }

        CollectionQueries.EnsureProfileLineage(_db, _transaction, _profile!, Now());
        byte[]? predecessor = null;
        ulong required = 0;
        byte[]? fence = null;
        if (predecessorText is not null)
        {
            predecessor = CollectionEncoding.Identifier(predecessorText);
            if (predecessor.AsSpan().SequenceEqual(stream))
                throw new InvalidDataException("A stream cannot succeed itself.");

            required = ulong.Parse(requiredText!, CultureInfo.InvariantCulture);
            if (CollectionQueries.ReadStreamState(_db, _transaction, predecessor) is null || Receipt(predecessor) < required)
                throw new InvalidDataException("Collection predecessor is not durable yet; send its pending ranges first.");

            fence = CollectionEncoding.DerivedIdentifier(predecessorText + _retirementSuffix);
            CloseStream(predecessor, Receipt(predecessor));
        }

        CollectionQueries.InsertStream(_db, _transaction, stream, _profile!, digest, Now());
        if (predecessor is not null)
            CollectionQueries.InsertStreamDependency(_db, _transaction, stream, _profile!, predecessor, fence!, CollectionEncoding.Revision(required), Now());
    }

    /// <summary>
    /// Reads the durable contiguous prefix, independent of save-restored sequence state.
    /// </summary>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <returns>The durable contiguous source prefix, or zero when no receipt exists.</returns>
    private ulong Receipt(byte[] stream)
    {
        StreamReceiptRecord? receipt = CollectionQueries.ReadStreamReceipt(_db, _transaction, stream);
        return receipt is not null ? CollectionEncoding.ReadRevision(receipt.Sequence) : 0;
    }

    /// <summary>
    /// Rejects new facts appended after a stream's immutable retirement fence.
    /// </summary>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    private void EnsureStreamOpen(byte[] stream)
    {
        Guid identity = new(stream);
        if (_verifiedOpenStream == identity)
            return;

        if (!string.Equals(CollectionQueries.ReadStreamState(_db, _transaction, stream)?.State, _openState, StringComparison.Ordinal))
            throw new InvalidDataException("New occurrences cannot append after a stream is retired.");

        _verifiedOpenStream = identity;
    }

    /// <summary>
    /// Persists the once-only frontier in the same transaction as its derived evidence.
    /// </summary>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void SaveReceipt(byte[] stream, ulong sequence) => CollectionQueries.SaveStreamReceipt(_db, _transaction, stream, CollectionEncoding.Revision(sequence), CollectionEncoding.Revision(_revision), Now());

    /// <summary>
    /// Publishes a final source fence without clearing ordered same-attempt progress.
    /// </summary>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void CloseStream(byte[] stream, ulong sequence)
    {
        byte[] fence = CollectionEncoding.DerivedIdentifier(CollectionEncoding.Identifier(stream) + _retirementSuffix);
        CollectionQueries.RetireProducerStream(_db, _transaction, stream);
        CollectionQueries.InsertStreamRetirement(_db, _transaction, stream, fence, CollectionEncoding.Revision(sequence), CollectionEncoding.Canonical(new { final_sequence = CollectionEncoding.Decimal(sequence) }), Now());
        if (_verifiedOpenStream == new Guid(stream))
            _verifiedOpenStream = null;
    }

    /// <summary>
    /// Persists sufficient bounded causal state before acknowledging open effects.
    /// </summary>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void SaveReducer(byte[] stream, ulong sequence)
    {
        byte[] raw = CollectionEncoding.Canonical(_reducer.ExportState());
        byte[] compressed = CollectionEncoding.Compress(raw);
        byte[] identity = CollectionEncoding.DerivedIdentifier(CollectionEncoding.Identifier(_profile!) + _collectorContextSuffix);
        CollectionQueries.SaveLiveContext(_db, _transaction, identity, _profile!, compressed, stream, CollectionEncoding.Revision(sequence));
        CollectionQueries.SaveCausalCheckpoint(_db, _transaction, identity, compressed, raw.LongLength, stream, CollectionEncoding.Revision(sequence));
    }

    /// <summary>
    /// Restores the last committed reducer state without replaying game history.
    /// </summary>
    private void RestoreReducer()
    {
        ReducerCheckpointRecord? checkpoint = CollectionQueries.ReadCausalCheckpoint(_db, _transaction);
        if (checkpoint is null)
            return;

        byte[] compressed = checkpoint.Payload;
        long length = checkpoint.Length;
        byte[] bytes = CollectionEncoding.Inflate(compressed, length);
        if (bytes.LongLength != length)
            throw new InvalidDataException("Reducer checkpoint length is invalid.");

        using JsonDocument state = JsonDocument.Parse(bytes);
        _reducer.ImportState(state.RootElement);
    }
}
