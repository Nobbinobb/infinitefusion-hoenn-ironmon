using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Collection.Queries.Tables;

/// <summary>
/// Maps existing producer_lineages columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ProducerLineageRow
{
    private const string _table = "producer_lineages";
    private const string _profile = "lineage_id";
    private const string _meta = "meta_id";
    private const string _source = "source_kind";
    private const string _created = "created_utc_ms";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte producer lineage identity.
    /// </summary>
    [SqlColumn(_profile)]
    public byte[] Profile { get; init; } = [];

    /// <summary>
    /// Gets or initializes the profile metadata anchor.
    /// </summary>
    [SqlColumn(_meta)]
    public int Meta { get; init; }

    /// <summary>
    /// Gets or initializes the source kind.
    /// </summary>
    [SqlColumn(_source)]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the creation time in UTC Unix milliseconds.
    /// </summary>
    [SqlColumn(_created)]
    public long Created { get; init; }
}

/// <summary>
/// Maps existing producer_streams columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ProducerStreamRow
{
    private const string _table = "producer_streams";
    private const string _stream = "stream_id";
    private const string _profile = "lineage_id";
    private const string _session = "session_instance_id";
    private const string _version = "descriptor_version";
    private const string _digest = "descriptor_digest";
    private const string _started = "started_utc_ms";
    private const string _registered = "registered_utc_ms";
    private const string _state = "stream_state";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte producer lineage identity.
    /// </summary>
    [SqlColumn(_profile)]
    public byte[] Profile { get; init; } = [];

    /// <summary>
    /// Gets or initializes the session instance id.
    /// </summary>
    [SqlColumn(_session)]
    public byte[] Session { get; init; } = [];

    /// <summary>
    /// Gets or initializes the descriptor version.
    /// </summary>
    [SqlColumn(_version)]
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the complete immutable stream descriptor digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];

    /// <summary>
    /// Gets or initializes the start time in UTC Unix milliseconds, or null when unavailable.
    /// </summary>
    [SqlColumn(_started)]
    public long? Started { get; init; }

    /// <summary>
    /// Gets or initializes the registration time in UTC Unix milliseconds.
    /// </summary>
    [SqlColumn(_registered)]
    public long Registered { get; init; }

    /// <summary>
    /// Gets or initializes the stream state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// Maps existing stream_receipts columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class StreamReceiptRow
{
    private const string _table = "stream_receipts";
    private const string _stream = "stream_id";
    private const string _sequence = "disposed_through_sequence";
    private const string _revision = "store_revision";
    private const string _committed = "committed_utc_ms";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte durable contiguous frontier.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];

    /// <summary>
    /// Gets or initializes the commit time in UTC Unix milliseconds.
    /// </summary>
    [SqlColumn(_committed)]
    public long Committed { get; init; }
}

/// <summary>
/// Maps existing stream_retirements columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class StreamRetirementRow
{
    private const string _table = "stream_retirements";
    private const string _stream = "stream_id";
    private const string _fence = "retirement_fence_id";
    private const string _recovered = "source_recovered_prefix";
    private const string _required = "required_frontier";
    private const string _tracker = "tracker_prefix_at_settlement";
    private const string _settled = "settled_frontier";
    private const string _kind = "retirement_kind";
    private const string _state = "settlement_state";
    private const string _format = "closure_format_version";
    private const string _codec = "closure_codec";
    private const string _payload = "closure_payload";
    private const string _time = "settled_utc_ms";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the retirement fence id.
    /// </summary>
    [SqlColumn(_fence)]
    public byte[] Fence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source recovered prefix.
    /// </summary>
    [SqlColumn(_recovered)]
    public byte[] Recovered { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte required source frontier.
    /// </summary>
    [SqlColumn(_required)]
    public byte[] Required { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte tracker prefix at settlement, or null when unsettled.
    /// </summary>
    [SqlColumn(_tracker)]
    public byte[]? Tracker { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte settled frontier, or null when unsettled.
    /// </summary>
    [SqlColumn(_settled)]
    public byte[]? Settled { get; init; }

    /// <summary>
    /// Gets or initializes the retirement kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the settlement state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the closure format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the closure codec.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete JSON retirement closure payload.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the settlement time in UTC Unix milliseconds, or null when unavailable.
    /// </summary>
    [SqlColumn(_time)]
    public long? Time { get; init; }
}

/// <summary>
/// Maps existing stream_dependencies columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class StreamDependencyRow
{
    private const string _table = "stream_dependencies";
    private const string _stream = "successor_stream_id";
    private const string _profile = "lineage_id";
    private const string _predecessor = "predecessor_stream_id";
    private const string _fence = "retirement_fence_id";
    private const string _required = "required_source_prefix";
    private const string _state = "admission_state";
    private const string _admitted = "admitted_utc_ms";

    /// <summary>
    /// Gets or initializes the successor stream id.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte producer lineage identity.
    /// </summary>
    [SqlColumn(_profile)]
    public byte[] Profile { get; init; } = [];

    /// <summary>
    /// Gets or initializes the predecessor stream id.
    /// </summary>
    [SqlColumn(_predecessor)]
    public byte[] Predecessor { get; init; } = [];

    /// <summary>
    /// Gets or initializes the retirement fence id.
    /// </summary>
    [SqlColumn(_fence)]
    public byte[] Fence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte required predecessor source prefix.
    /// </summary>
    [SqlColumn(_required)]
    public byte[] Required { get; init; } = [];

    /// <summary>
    /// Gets or initializes the admission state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the admission time in UTC Unix milliseconds, or null when unavailable.
    /// </summary>
    [SqlColumn(_admitted)]
    public long? Admitted { get; init; }
}

/// <summary>
/// Maps existing live_contexts columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class LiveContextRow
{
    private const string _table = "live_contexts";
    private const string _id = "live_context_id";
    private const string _profile = "lineage_id";
    private const string _attempt = "attempt_id";
    private const string _load = "load_incarnation_id";
    private const string _kind = "context_kind";
    private const string _state = "context_state";
    private const string _format = "format_version";
    private const string _codec = "codec";
    private const string _payload = "payload";
    private const string _stream = "last_stream_id";
    private const string _sequence = "last_sequence";

    /// <summary>
    /// Gets or initializes the live context id.
    /// </summary>
    [SqlColumn(_id)]
    public byte[] Id { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte producer lineage identity.
    /// </summary>
    [SqlColumn(_profile)]
    public byte[] Profile { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte attempt identity, or null for profile-wide state.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[]? Attempt { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte load incarnation identity, or null when absent.
    /// </summary>
    [SqlColumn(_load)]
    public byte[]? Load { get; init; }

    /// <summary>
    /// Gets or initializes the context kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the context state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the codec.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete compressed reducer checkpoint.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the last stream id.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte last recorded source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];
}

/// <summary>
/// Maps existing causal_work columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class CausalWorkRow
{
    private const string _table = "causal_work";
    private const string _id = "work_id";
    private const string _context = "live_context_id";
    private const string _kind = "work_kind";
    private const string _key = "canonical_group_key";
    private const string _state = "work_state";
    private const string _projection = "projection_version";
    private const string _format = "format_version";
    private const string _codec = "codec";
    private const string _payload = "payload";
    private const string _length = "uncompressed_length";
    private const string _openedStream = "opened_stream_id";
    private const string _openedSequence = "opened_sequence";
    private const string _stream = "last_stream_id";
    private const string _sequence = "last_sequence";

    /// <summary>
    /// Gets or initializes the work id.
    /// </summary>
    [SqlColumn(_id)]
    public byte[] Id { get; init; } = [];

    /// <summary>
    /// Gets or initializes the live context id.
    /// </summary>
    [SqlColumn(_context)]
    public byte[] Context { get; init; } = [];

    /// <summary>
    /// Gets or initializes the work kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the canonical group key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the work state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the projection version.
    /// </summary>
    [SqlColumn(_projection)]
    public int Projection { get; init; }

    /// <summary>
    /// Gets or initializes the format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the codec.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete compressed reducer checkpoint.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact uncompressed checkpoint length in bytes.
    /// </summary>
    [SqlColumn(_length)]
    public long Length { get; init; }

    /// <summary>
    /// Gets or initializes the opened stream id.
    /// </summary>
    [SqlColumn(_openedStream)]
    public byte[] OpenedStream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte original opening source sequence.
    /// </summary>
    [SqlColumn(_openedSequence)]
    public byte[] OpenedSequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the last stream id.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte last recorded source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];
}
