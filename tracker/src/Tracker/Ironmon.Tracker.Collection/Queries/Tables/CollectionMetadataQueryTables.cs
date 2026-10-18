using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps existing collection_meta columns used by generated archive and catalogue operations.
/// </summary>
[SqlTable(_table)]
internal sealed class CollectionMetadataRow
{
    private const string _table = "collection_meta";
    private const string _id = "meta_id";
    private const string _profile = "profile_id";
    private const string _epoch = "epoch_id";
    private const string _incarnation = "store_incarnation_id";
    private const string _revision = "collection_revision";
    private const string _version = "schema_version";
    private const string _created = "created_utc_ms";
    private const string _minimumReader = "minimum_reader_version";
    private const string _coverage = "coverage_start_kind";

    /// <summary>
    /// Gets or initializes the singleton metadata identity.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte profile identity.
    /// </summary>
    [SqlColumn(_profile)]
    public byte[] Profile { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte collection epoch identity.
    /// </summary>
    [SqlColumn(_epoch)]
    public byte[] Epoch { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact sixteen-byte store incarnation identity.
    /// </summary>
    [SqlColumn(_incarnation)]
    public byte[] Incarnation { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte global collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];

    /// <summary>
    /// Gets or initializes the retained physical schema version.
    /// </summary>
    [SqlColumn(_version)]
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the creation time in UTC Unix milliseconds.
    /// </summary>
    [SqlColumn(_created)]
    public long Created { get; init; }

    /// <summary>
    /// Gets or initializes the minimum compatible reader version.
    /// </summary>
    [SqlColumn(_minimumReader)]
    public int MinimumReader { get; init; }

    /// <summary>
    /// Gets or initializes the retained release eligibility boundary.
    /// </summary>
    [SqlColumn(_coverage)]
    public string Coverage { get; init; } = string.Empty;
}

/// <summary>
/// Maps existing sqlite_schema columns for the remaining typed collection operations.
/// </summary>
[SqlTable(_table)]
internal sealed class StoreSchemaLookupRow
{
    private const string _table = "sqlite_schema";
    private const string _name = "name";
    private const string _type = "type";

    /// <summary>
    /// Gets or initializes the exact stored name value.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exact stored type value.
    /// </summary>
    [SqlColumn(_type)]
    public string Type { get; init; } = string.Empty;
}
