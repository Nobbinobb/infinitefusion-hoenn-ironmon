using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Collection.Queries.Tables;

/// <summary>
/// Maps existing coverage_gaps columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class CoverageGapRow
{
    private const string _table = "coverage_gaps";
    private const string _id = "gap_id";
    private const string _scope = "scope_id";
    private const string _stream = "stream_id";
    private const string _first = "first_sequence";
    private const string _reason = "reason";
    private const string _state = "disposition_state";
    private const string _opened = "opened_utc_ms";
    private const string _closed = "closed_utc_ms";
    private const string _unknown = "unknown_tail";
    private const string _format = "detail_version";
    private const string _payload = "detail_payload";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte coverage gap identity.
    /// </summary>
    [SqlColumn(_id)]
    public byte[] Id { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact retained lifetime scope identity.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte interrupted stream identity, or null when absent.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[]? Stream { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte first missing sequence, or null when unknown.
    /// </summary>
    [SqlColumn(_first)]
    public byte[]? First { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained coverage reason.
    /// </summary>
    [SqlColumn(_reason)]
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the retained gap disposition state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the opening time in UTC Unix milliseconds, or null when unavailable.
    /// </summary>
    [SqlColumn(_opened)]
    public long? Opened { get; init; }

    /// <summary>
    /// Gets or initializes the closing time in UTC Unix milliseconds, or null when unavailable.
    /// </summary>
    [SqlColumn(_closed)]
    public long? Closed { get; init; }

    /// <summary>
    /// Gets or initializes the unknown-tail marker, zero or one.
    /// </summary>
    [SqlColumn(_unknown)]
    public int Unknown { get; init; }

    /// <summary>
    /// Gets or initializes the positive detail format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained JSON coverage detail bytes.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];
}

/// <summary>
/// Maps existing coverage_gap_families columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class CoverageGapFamilyRow
{
    private const string _table = "coverage_gap_families";
    private const string _id = "gap_id";
    private const string _family = "family_id";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte coverage gap identity.
    /// </summary>
    [SqlColumn(_id)]
    public byte[] Id { get; init; } = [];

    /// <summary>
    /// Gets or initializes the registered affected aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }
}

/// <summary>
/// Maps existing retirement_gaps columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class RetirementGapRow
{
    private const string _table = "retirement_gaps";
    private const string _stream = "stream_id";
    private const string _id = "gap_id";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte retired stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte retained coverage gap identity.
    /// </summary>
    [SqlColumn(_id)]
    public byte[] Id { get; init; } = [];
}
