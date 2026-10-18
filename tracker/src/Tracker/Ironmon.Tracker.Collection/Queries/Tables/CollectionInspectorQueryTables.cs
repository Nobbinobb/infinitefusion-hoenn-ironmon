using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps existing sqlite_schema operational columns without changing their storage.
/// </summary>
[SqlTable(_table)]
internal sealed class InspectorSchemaRow
{
    private const string _table = "sqlite_schema";
    private const string _name = "name";
    private const string _type = "type";

    /// <summary>
    /// Gets or initializes the stored name value.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the stored type value.
    /// </summary>
    [SqlColumn(_type)]
    public string Type { get; init; } = string.Empty;
}

/// <summary>
/// Maps existing collection_operations operational columns without changing their storage.
/// </summary>
[SqlTable(_table)]
internal sealed class InspectorOperationsRow
{
    private const string _table = "collection_operations";
    private const string _id = "id";
    private const string _batches = "accepted_batches";
    private const string _events = "accepted_events";
    private const string _duplicates = "duplicate_events";
    private const string _bytes = "received_bytes";
    private const string _commitMilliseconds = "previous_commit_ms";
    private const string _updated = "updated_utc_ms";

    /// <summary>
    /// Gets or initializes the stored id value.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the stored accepted_batches value.
    /// </summary>
    [SqlColumn(_batches)]
    public long Batches { get; init; }

    /// <summary>
    /// Gets or initializes the stored accepted_events value.
    /// </summary>
    [SqlColumn(_events)]
    public long Events { get; init; }

    /// <summary>
    /// Gets or initializes the stored duplicate_events value.
    /// </summary>
    [SqlColumn(_duplicates)]
    public long Duplicates { get; init; }

    /// <summary>
    /// Gets or initializes the stored received_bytes value.
    /// </summary>
    [SqlColumn(_bytes)]
    public long Bytes { get; init; }

    /// <summary>
    /// Gets or initializes the stored previous_commit_ms value.
    /// </summary>
    [SqlColumn(_commitMilliseconds)]
    public double CommitMilliseconds { get; init; }

    /// <summary>
    /// Gets or initializes the stored updated_utc_ms value.
    /// </summary>
    [SqlColumn(_updated)]
    public long Updated { get; init; }
}

/// <summary>
/// Maps existing collection_operations columns for the remaining typed collection operations.
/// </summary>
[SqlTable(_table)]
internal sealed class StoreOperationWriteRow
{
    private const string _table = "collection_operations";
    private const string _id = "id";
    private const string _batches = "accepted_batches";
    private const string _events = "accepted_events";
    private const string _duplicates = "duplicate_events";
    private const string _bytes = "received_bytes";
    private const string _milliseconds = "previous_commit_ms";
    private const string _updated = "updated_utc_ms";

    /// <summary>
    /// Gets or initializes the exact stored id value.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored accepted_batches value.
    /// </summary>
    [SqlColumn(_batches)]
    public long Batches { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored accepted_events value.
    /// </summary>
    [SqlColumn(_events)]
    public long Events { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored duplicate_events value.
    /// </summary>
    [SqlColumn(_duplicates)]
    public long Duplicates { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored received_bytes value.
    /// </summary>
    [SqlColumn(_bytes)]
    public long Bytes { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored previous_commit_ms value.
    /// </summary>
    [SqlColumn(_milliseconds)]
    public double Milliseconds { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored updated_utc_ms value.
    /// </summary>
    [SqlColumn(_updated)]
    public long Updated { get; init; }
}
