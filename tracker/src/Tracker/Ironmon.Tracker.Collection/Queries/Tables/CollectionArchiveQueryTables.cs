using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Collection.Queries.Tables;

/// <summary>
/// Maps existing cold_publications columns used by generated archive and catalogue operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ColdPublicationRow
{
    private const string _table = "cold_publications";
    private const string _id = "publication_id";
    private const string _revision = "head_revision";
    private const string _through = "base_through_revision";

    /// <summary>
    /// Gets or initializes the immutable publication identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte head collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte included collection revision.
    /// </summary>
    [SqlColumn(_through)]
    public byte[] Through { get; init; } = [];
}

/// <summary>
/// Maps existing cold_partitions columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ColdPartitionRow
{
    private const string _table = "cold_partitions";
    private const string _scope = "scope_id";
    private const string _family = "family_id";
    private const string _block = "base_block_id";
    private const string _publication = "publication_id";

    /// <summary>
    /// Gets or initializes the interned lifetime, attempt or battle scope identity.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the immutable archived partition block identity.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }

    /// <summary>
    /// Gets or initializes the publication id.
    /// </summary>
    [SqlColumn(_publication)]
    public long Publication { get; init; }
}

/// <summary>
/// Maps existing aggregate_cells columns without narrowing retained archive values.
/// </summary>
[SqlTable(_table)]
internal sealed class ArchiveCellRow
{
    private const string _table = "aggregate_cells";
    private const string _series = "series_id";
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
    /// Gets or initializes the complete signed 64-bit measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public long Measure { get; init; }

    /// <summary>
    /// Gets or initializes the original scalar integer, overflow bytes or DBNull for unavailable data.
    /// </summary>
    [SqlColumn(_value)]
    public object Value { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the known population in its original integer or overflow storage class.
    /// </summary>
    [SqlColumn(_known)]
    public object Known { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the unknown population in its original integer or overflow storage class.
    /// </summary>
    [SqlColumn(_unknown)]
    public object Unknown { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the unsupported population in its original integer or overflow storage class.
    /// </summary>
    [SqlColumn(_unsupported)]
    public object Unsupported { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the not-applicable population in its original integer or overflow storage class.
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
/// Maps existing set_members columns without narrowing retained archive values.
/// </summary>
[SqlTable(_table)]
internal sealed class ArchiveMemberRow
{
    private const string _table = "set_members";
    private const string _series = "series_id";
    private const string _measure = "measure_no";
    private const string _member = "member_identity_id";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the complete signed 64-bit measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public long Measure { get; init; }

    /// <summary>
    /// Gets or initializes the complete exact member identity.
    /// </summary>
    [SqlColumn(_member)]
    public long Member { get; init; }
}

/// <summary>
/// Maps existing aggregate_set_coverage columns without narrowing retained archive values.
/// </summary>
[SqlTable(_table)]
internal sealed class ArchiveCoverageRow
{
    private const string _table = "aggregate_set_coverage";
    private const string _series = "series_id";
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
    /// Gets or initializes the complete signed 64-bit measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public long Measure { get; init; }

    /// <summary>
    /// Gets or initializes the known population in its original integer or overflow storage class.
    /// </summary>
    [SqlColumn(_known)]
    public object Known { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the unknown population in its original integer or overflow storage class.
    /// </summary>
    [SqlColumn(_unknown)]
    public object Unknown { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the unsupported population in its original integer or overflow storage class.
    /// </summary>
    [SqlColumn(_unsupported)]
    public object Unsupported { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the not-applicable population in its original integer or overflow storage class.
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
/// Maps existing cold_overlays columns for the remaining typed collection operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ColdOverlayLookupRow
{
    private const string _table = "cold_overlays";
    private const string _block = "block_id";

    /// <summary>
    /// Gets or initializes the exact stored block_id value.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }
}
