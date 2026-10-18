using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated delivery storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _gameplaySource = "gameplay";
    private const string _deliveryOpenState = "open";
    private const string _cleanRetirement = "clean";
    private const string _settledRetirement = "settled";
    private const string _closureJsonCodec = "json";
    private const string _admittedDependency = "admitted";
    private const string _collectorContextKind = "collector";
    private const string _checkpointCodec = "zlib-json-v1";
    private const string _genericReducerKind = "generic_reducer";
    private const string _recoveredInterruptedKind = "recovered_interrupted";
    private const string _retiredStreamState = "retired";

    /// <summary>
    /// Declares typed delivery queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Creates the producer lineage anchor without modifying existing metadata.
        /// </summary>
        /// <param name="profile">The exact sixteen-byte producer lineage identity.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> EnsureProfileLineage(byte[] profile, long now)
        {
            return Sql.Insert(new ProducerLineageRow { Profile = profile, Meta = 1, Source = _gameplaySource, Created = now })
                .OnConflict(row => row.Profile)
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the immutable descriptor and current state using the full stream identity.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<StreamDescriptorRecord?> ReadStreamDescriptor(byte[] stream)
        {
            return Sql.From<ProducerStreamRow>()
                .Where(row => row.Stream == stream)
                .Select(row => new StreamDescriptorRecord { Digest = row.Digest, State = row.State })
                .FirstOrDefault();
        }

        /// <summary>
        /// Reads the registered stream state without accepting an absent stream.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<StreamStateRecord?> ReadStreamState(byte[] stream)
        {
            return Sql.From<ProducerStreamRow>()
                .Where(row => row.Stream == stream)
                .Select(row => new StreamStateRecord { State = row.State })
                .FirstOrDefault();
        }

        /// <summary>
        /// Registers every immutable stream field with identical start and registration timestamps.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="profile">The exact sixteen-byte producer lineage identity.</param>
        /// <param name="digest">The complete immutable stream descriptor digest.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> InsertStream(byte[] stream, byte[] profile, byte[] digest, long now)
        {
            return Sql.Insert(new ProducerStreamRow { Stream = stream, Profile = profile, Session = stream, Version = 1, Digest = digest, Started = now, Registered = now, State = _deliveryOpenState })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the exact durable contiguous source frontier, including unsigned overflow values.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<StreamReceiptRecord?> ReadStreamReceipt(byte[] stream)
        {
            return Sql.From<StreamReceiptRow>()
                .Where(row => row.Stream == stream)
                .Select(row => new StreamReceiptRecord { Sequence = row.Sequence })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts the complete durable frontier and its commit metadata.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte contiguous source frontier.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveStreamReceipt(byte[] stream, byte[] sequence, byte[] revision, long now)
        {
            return Sql.Insert(new StreamReceiptRow { Stream = stream, Sequence = sequence, Revision = revision, Committed = now })
                .OnConflict(row => row.Stream)
                .UpdateFromInserted(row => new { row.Sequence, row.Revision, row.Committed })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains the settled retirement fence once without rewriting an existing settlement.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="fence">The exact sixteen-byte retirement fence identity.</param>
        /// <param name="sequence">The exact eight-byte contiguous source frontier.</param>
        /// <param name="payload">The complete JSON retirement closure payload.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> InsertStreamRetirement(byte[] stream, byte[] fence, byte[] sequence, byte[] payload, long now)
        {
            return Sql.Insert(new StreamRetirementRow { Stream = stream, Fence = fence, Recovered = sequence, Required = sequence, Tracker = sequence, Settled = sequence, Kind = _cleanRetirement, State = _settledRetirement, Format = 1, Codec = _closureJsonCodec, Payload = payload, Time = now })
                .OnConflict(row => row.Stream)
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains the exact admitted predecessor relationship and required frontier.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="profile">The exact sixteen-byte producer lineage identity.</param>
        /// <param name="predecessor">The exact sixteen-byte predecessor stream identity.</param>
        /// <param name="fence">The exact sixteen-byte retirement fence identity.</param>
        /// <param name="sequence">The exact eight-byte contiguous source frontier.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> InsertStreamDependency(byte[] stream, byte[] profile, byte[] predecessor, byte[] fence, byte[] sequence, long now)
        {
            return Sql.Insert(new StreamDependencyRow { Stream = stream, Profile = profile, Predecessor = predecessor, Fence = fence, Required = sequence, State = _admittedDependency, Admitted = now })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Updates only live payload and source metadata while preserving its original ownership and format.
        /// </summary>
        /// <param name="id">The exact sixteen-byte live context and work identity.</param>
        /// <param name="profile">The exact sixteen-byte producer lineage identity.</param>
        /// <param name="payload">The complete compressed live reducer checkpoint.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte contiguous source frontier.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveLiveContext(byte[] id, byte[] profile, byte[] payload, byte[] stream, byte[] sequence)
        {
            return Sql.Insert(new LiveContextRow { Id = id, Profile = profile, Attempt = null, Load = null, Kind = _collectorContextKind, State = _activeContextState, Format = 1, Codec = _checkpointCodec, Payload = payload, Stream = stream, Sequence = sequence })
                .OnConflict(row => row.Id)
                .UpdateFromInserted(row => new { row.Payload, row.Stream, row.Sequence })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Updates reducer state while preserving its original opening stream and sequence.
        /// </summary>
        /// <param name="id">The exact sixteen-byte live context and work identity.</param>
        /// <param name="payload">The complete compressed causal reducer checkpoint.</param>
        /// <param name="length">The exact uncompressed checkpoint length in bytes.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte contiguous source frontier.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveCausalCheckpoint(byte[] id, byte[] payload, long length, byte[] stream, byte[] sequence)
        {
            return Sql.Insert(new CausalWorkRow { Id = id, Context = id, Kind = _genericReducerKind, Key = id, State = _deliveryOpenState, Projection = 1, Format = 1, Codec = _checkpointCodec, Payload = payload, Length = length, OpenedStream = stream, OpenedSequence = sequence, Stream = stream, Sequence = sequence })
                .OnConflict(row => row.Id)
                .UpdateFromInserted(row => new { row.Payload, row.Length, row.Stream, row.Sequence })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the first generic reducer checkpoint using the original unordered selection semantics.
        /// </summary>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<ReducerCheckpointRecord?> ReadCausalCheckpoint()
        {
            return Sql.From<CausalWorkRow>()
                .Where(row => row.Kind == _genericReducerKind)
                .Select(row => new ReducerCheckpointRecord { Payload = row.Payload, Length = row.Length })
                .FirstOrDefault();
        }

        /// <summary>
        /// Preserves recovered retirement classification and complete closure evidence.
        /// </summary>
        /// <param name="stream">The exact stream value retained by the original operation.</param>
        /// <param name="payload">The exact payload value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> MarkInterruptedRetirement(byte[] stream, byte[] payload)
        {
            return Sql.From<StreamRetirementRow>().Where(row => row.Stream == stream)
                .Update(row => new StreamRetirementRow { Kind = _recoveredInterruptedKind, Payload = payload }).ExecuteNonQuery();
        }

        /// <summary>
        /// Marks only the exact source stream as retired.
        /// </summary>
        /// <param name="stream">The exact stream value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> RetireProducerStream(byte[] stream)
        {
            return Sql.From<ProducerStreamRow>().Where(row => row.Stream == stream)
                .Update(row => new ProducerStreamRow { State = _retiredStreamState }).ExecuteNonQuery();
        }
    }
}
