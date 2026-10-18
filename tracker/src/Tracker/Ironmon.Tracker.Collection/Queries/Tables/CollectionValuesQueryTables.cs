using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps the existing immutable dictionary for build-time SQL generation without changing its schema.
/// </summary>
[SqlTable(_table)]
internal sealed class CollectionValueRow
{
    private const string _table = "collection_values";
    private const string _id = "value_id";
    private const string _kind = "value_kind";
    private const string _payload = "payload";
    private const string _digest = "digest";

    /// <summary>
    /// Gets or initializes the immutable dictionary identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the stored primitive representation code.
    /// </summary>
    [SqlColumn(_kind)]
    public int Kind { get; init; }

    /// <summary>
    /// Gets or initializes the complete immutable payload bytes.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the eight-byte lookup and integrity digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];
}
