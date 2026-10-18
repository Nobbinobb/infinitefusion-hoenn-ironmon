using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps existing collection_current_snapshots columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class CurrentInventoryRow
{
    private const string _table = "collection_current_snapshots";
    private const string _family = "family_id";
    private const string _scope = "scope_id";
    private const string _container = "container_identity_id";
    private const string _format = "format_version";
    private const string _payload = "state_blob";
    private const string _stream = "source_stream_id";
    private const string _sequence = "source_sequence";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the interned lifetime, attempt or battle scope identity.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the container identity id.
    /// </summary>
    [SqlColumn(_container)]
    public long Container { get; init; }

    /// <summary>
    /// Gets or initializes the format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the complete compressed current inventory state.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the source stream id.
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
/// Maps existing collection_resource_parents columns used by generated delivery and checkpoint operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ResourceParentRow
{
    private const string _table = "collection_resource_parents";
    private const string _id = "resource_identity_id";
    private const string _ordinal = "ordinal";
    private const string _parent = "parent_identity_id";

    /// <summary>
    /// Gets or initializes the resource identity id.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based parent relationship ordinal.
    /// </summary>
    [SqlColumn(_ordinal)]
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets or initializes the interned exact parent resource identity.
    /// </summary>
    [SqlColumn(_parent)]
    public long Parent { get; init; }
}

/// <summary>
/// Maps existing collection_resource_lots columns for the remaining typed collection operations.
/// </summary>
[SqlTable(_table)]
internal sealed class ResourceLotWriteRow
{
    private const string _table = "collection_resource_lots";
    private const string _id = "resource_identity_id";
    private const string _item = "item_identity_id";
    private const string _origin = "origin_identity_id";
    private const string _controller = "controller_identity_id";
    private const string _location = "location_identity_id";
    private const string _quantity = "current_quantity";
    private const string _block = "metadata_block_id";
    private const string _stream = "source_stream_id";
    private const string _sequence = "source_sequence";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact stored resource_identity_id value.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored item_identity_id value.
    /// </summary>
    [SqlColumn(_item)]
    public long Item { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored origin_identity_id value.
    /// </summary>
    [SqlColumn(_origin)]
    public long? Origin { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored controller_identity_id value.
    /// </summary>
    [SqlColumn(_controller)]
    public long? Controller { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored location_identity_id value.
    /// </summary>
    [SqlColumn(_location)]
    public long? Location { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored current_quantity value.
    /// </summary>
    [SqlColumn(_quantity)]
    public long Quantity { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored metadata_block_id value.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored source_stream_id value.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact stored source_sequence value.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact stored revision value.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}
