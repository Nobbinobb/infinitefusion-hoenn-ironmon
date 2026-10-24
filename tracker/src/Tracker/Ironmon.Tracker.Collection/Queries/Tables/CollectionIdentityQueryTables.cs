using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Collection.Queries.Tables;

/// <summary>
/// Maps stored identity_namespaces columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class IdentityNamespaceRow
{
    private const string _table = "identity_namespaces";
    private const string _id = "namespace_id";
    private const string _revision = "namespace_revision";
    private const string _name = "namespace";

    /// <summary>
    /// Gets or initializes the exact stored namespace identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the native identity namespace revision.
    /// </summary>
    [SqlColumn(_revision)]
    public string Revision { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exact identity namespace name.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Maps stored identity_values columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class NativeIdentityRow
{
    private const string _table = "identity_values";
    private const string _id = "identity_id";
    private const string _namespace = "namespace_id";
    private const string _value = "native_value_id";

    /// <summary>
    /// Gets or initializes the exact interned native identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the owning identity namespace.
    /// </summary>
    [SqlColumn(_namespace)]
    public long Namespace { get; init; }

    /// <summary>
    /// Gets or initializes the immutable dictionary value identity.
    /// </summary>
    [SqlColumn(_value)]
    public long Value { get; init; }
}

/// <summary>
/// Maps stored tuples columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class DimensionTupleRow
{
    private const string _table = "tuples";
    private const string _id = "tuple_id";
    private const string _format = "format_version";
    private const string _key = "canonical_key";
    private const string _arity = "arity";

    /// <summary>
    /// Gets or initializes the exact interned tuple identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the stored format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the complete canonical binary key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the number of ordered tuple dimensions.
    /// </summary>
    [SqlColumn(_arity)]
    public int Arity { get; init; }
}

/// <summary>
/// Maps stored scopes columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class CollectionScopeRow
{
    private const string _table = "scopes";
    private const string _id = "scope_id";
    private const string _kind = "scope_kind";
    private const string _key = "exact_key";
    private const string _parent = "parent_scope_id";
    private const string _attempt = "attempt_id";

    /// <summary>
    /// Gets or initializes the exact interned scope identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the declared scope kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete exact binary scope key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the parent lifetime scope, or null for a root.
    /// </summary>
    [SqlColumn(_parent)]
    public long? Parent { get; init; }

    /// <summary>
    /// Gets or initializes the sixteen-byte owning attempt identity, or null when absent.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[]? Attempt { get; init; }
}

/// <summary>
/// Maps stored contexts columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class CollectionContextRow
{
    private const string _table = "contexts";
    private const string _id = "context_id";
    private const string _kind = "context_kind";
    private const string _key = "canonical_key";
    private const string _digest = "key_hash";
    private const string _format = "format_version";
    private const string _origin = "origin_kind";
    private const string _block = "block_id";

    /// <summary>
    /// Gets or initializes the exact retained context identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the declared context kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete canonical binary key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete context key digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];

    /// <summary>
    /// Gets or initializes the stored format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the context origin discriminator.
    /// </summary>
    [SqlColumn(_origin)]
    public string Origin { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the immutable context definition block.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }
}

/// <summary>
/// Maps existing tuple_member_groups columns for the remaining typed collection operations.
/// </summary>
[SqlTable(_table)]
internal sealed class TupleGroupWriteRow
{
    private const string _table = "tuple_member_groups";
    private const string _tuple = "tuple_id";
    private const string _group = "group_no";
    private const string _zero = "identity_0";
    private const string _one = "identity_1";
    private const string _two = "identity_2";
    private const string _three = "identity_3";

    /// <summary>
    /// Gets or initializes the exact stored tuple_id value.
    /// </summary>
    [SqlColumn(_tuple)]
    public long Tuple { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored group_no value.
    /// </summary>
    [SqlColumn(_group)]
    public long Group { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored identity_0 value.
    /// </summary>
    [SqlColumn(_zero)]
    public long? Zero { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored identity_1 value.
    /// </summary>
    [SqlColumn(_one)]
    public long? One { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored identity_2 value.
    /// </summary>
    [SqlColumn(_two)]
    public long? Two { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored identity_3 value.
    /// </summary>
    [SqlColumn(_three)]
    public long? Three { get; init; }
}
