using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps stored aggregate_cells columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class AggregateCellRow
{
    private const string _table = "aggregate_cells";
    private const string _series = "series_id";
    private const string _family = "family_id";
    private const string _measure = "measure_no";
    private const string _value = "value_blob";
    private const string _known = "known_count";
    private const string _unknown = "unknown_count";
    private const string _unsupported = "unsupported_count";
    private const string _notApplicable = "not_applicable_count";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the native integer, overflow bytes or DBNull for an unknown scalar.
    /// </summary>
    [SqlColumn(_value)]
    public object Value { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact known population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_known)]
    public object Known { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unknown population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_unknown)]
    public object Unknown { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unsupported population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_unsupported)]
    public object Unsupported { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact not-applicable population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_notApplicable)]
    public object NotApplicable { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps stored aggregate_set_coverage columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class AggregateSetCoverageRow
{
    private const string _table = "aggregate_set_coverage";
    private const string _series = "series_id";
    private const string _family = "family_id";
    private const string _measure = "measure_no";
    private const string _known = "known_count";
    private const string _unknown = "unknown_count";
    private const string _unsupported = "unsupported_count";
    private const string _notApplicable = "not_applicable_count";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the exact known population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_known)]
    public object Known { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unknown population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_unknown)]
    public object Unknown { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unsupported population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_unsupported)]
    public object Unsupported { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact not-applicable population as an integer or overflow bytes.
    /// </summary>
    [SqlColumn(_notApplicable)]
    public object NotApplicable { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps stored set_members columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class AggregateSetMemberRow
{
    private const string _table = "set_members";
    private const string _series = "series_id";
    private const string _family = "family_id";
    private const string _measure = "measure_no";
    private const string _member = "member_identity_id";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the interned exact member identity.
    /// </summary>
    [SqlColumn(_member)]
    public long Member { get; init; }
}

/// <summary>
/// Maps stored ordered_state columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class OrderedStateRow
{
    private const string _table = "ordered_state";
    private const string _family = "family_id";
    private const string _scope = "scope_id";
    private const string _tuple = "tuple_id";
    private const string _context = "context_id";
    private const string _format = "format_version";
    private const string _codec = "state_codec";
    private const string _payload = "state_blob";
    private const string _stream = "last_stream_id";
    private const string _sequence = "last_sequence";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the interned lifetime, attempt or battle scope.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the interned ordered dimension tuple.
    /// </summary>
    [SqlColumn(_tuple)]
    public long Tuple { get; init; }

    /// <summary>
    /// Gets or initializes the retained context identity, or null when absent.
    /// </summary>
    [SqlColumn(_context)]
    public long? Context { get; init; }

    /// <summary>
    /// Gets or initializes the stored binary format version.
    /// </summary>
    [SqlColumn(_format)]
    public long Format { get; init; }

    /// <summary>
    /// Gets or initializes the stored state encoding name.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete compressed sufficient state.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte producer stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps stored hot_partitions columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class HotPartitionRow
{
    private const string _table = "hot_partitions";
    private const string _id = "partition_id";
    private const string _family = "family_id";
    private const string _scope = "scope_id";
    private const string _revision = "head_revision";

    /// <summary>
    /// Gets or initializes the exact hot partition identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the interned lifetime, attempt or battle scope.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte latest collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps stored aggregate_series columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class AggregateSeriesRow
{
    private const string _table = "aggregate_series";
    private const string _id = "series_id";
    private const string _partition = "partition_id";
    private const string _family = "family_id";
    private const string _tuple = "tuple_id";

    /// <summary>
    /// Gets or initializes the exact aggregate series identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the owning hot partition identity.
    /// </summary>
    [SqlColumn(_partition)]
    public long Partition { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the interned ordered dimension tuple.
    /// </summary>
    [SqlColumn(_tuple)]
    public long Tuple { get; init; }
}
