using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps stored block_values columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class ImmutableBlockRow
{
    private const string _table = "block_values";
    private const string _id = "block_id";
    private const string _blockKind = "block_kind_id";
    private const string _format = "format_version";
    private const string _codec = "codec";
    private const string _length = "uncompressed_length";
    private const string _digest = "digest";
    private const string _payload = "payload";

    /// <summary>
    /// Gets or initializes the registered immutable-block kind identity.
    /// </summary>
    [SqlColumn(_blockKind)]
    public long BlockKind { get; init; }

    /// <summary>
    /// Gets or initializes the exact immutable block identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the stored binary format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the stored raw or compressed block encoding.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the uncompressed byte length.
    /// </summary>
    [SqlColumn(_length)]
    public long Length { get; init; }

    /// <summary>
    /// Gets or initializes the complete immutable-block digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete immutable block bytes.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];
}

/// <summary>
/// Maps stored block_kinds columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class BlockKindRow
{
    private const string _table = "block_kinds";
    private const string _id = "block_kind_id";
    private const string _kind = "block_kind";

    /// <summary>
    /// Gets or initializes the exact interned block kind identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the immutable block category.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;
}
