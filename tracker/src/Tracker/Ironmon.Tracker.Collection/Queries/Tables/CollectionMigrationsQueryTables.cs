using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps stable immutable-block view columns for historical migration selection.
/// </summary>
[SqlTable(_table)]
internal sealed class MaintenanceBlockViewRow
{
    private const string _table = "immutable_blocks";
    private const string _id = "block_id";
    private const string _kind = "block_kind";
    private const string _format = "format_version";

    /// <summary>
    /// Gets or initializes the complete immutable block identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the retained block-kind name.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete stored format version.
    /// </summary>
    [SqlColumn(_format)]
    public long Format { get; init; }
}

/// <summary>
/// Maps full-width ordered-state migration keys without narrowing historical SQLite integers.
/// </summary>
[SqlTable(_table)]
internal sealed class MaintenanceOrderedRow
{
    private const string _table = "ordered_state";
    private const string _family = "family_id";
    private const string _scope = "scope_id";
    private const string _tuple = "tuple_id";
    private const string _format = "format_version";
    private const string _codec = "state_codec";
    private const string _payload = "state_blob";

    /// <summary>
    /// Gets or initializes the exact historical family identity.
    /// </summary>
    [SqlColumn(_family)]
    public long Family { get; init; }

    /// <summary>
    /// Gets or initializes the exact historical scope identity.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the exact historical tuple identity.
    /// </summary>
    [SqlColumn(_tuple)]
    public long Tuple { get; init; }

    /// <summary>
    /// Gets or initializes the positive stored format version.
    /// </summary>
    [SqlColumn(_format)]
    public long Format { get; init; }

    /// <summary>
    /// Gets or initializes the original or replacement encoding identifier.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete compressed state body.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];
}

/// <summary>
/// Maps existing ordered_state columns without narrowing retained archive values.
/// </summary>
[SqlTable(_table)]
internal sealed class OrderedMigrationRow
{
    private const string _table = "ordered_state";
    private const string _family = "family_id";
    private const string _scope = "scope_id";
    private const string _tuple = "tuple_id";
    private const string _payload = "state_blob";

    /// <summary>
    /// Gets or initializes the complete registered family identity.
    /// </summary>
    [SqlColumn(_family)]
    public long Family { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained scope identity.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the complete ordered tuple identity.
    /// </summary>
    [SqlColumn(_tuple)]
    public long Tuple { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained ordered-state bytes.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];
}

/// <summary>
/// Maps legacy direct_groups JSON columns only for reads before compact-schema migration.
/// </summary>
[SqlTable(_table)]
internal sealed class LegacySummaryDefinitionRow
{
    private const string _table = "direct_groups";
    private const string _id = "group_id";
    private const string _definition = "definition_json";

    /// <summary>
    /// Gets or initializes the complete original summary group identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the complete original JSON definition before compact-schema migration.
    /// </summary>
    [SqlColumn(_definition)]
    public string Definition { get; init; } = string.Empty;
}

/// <summary>
/// Maps legacy direct_relationship_contracts JSON columns only for reads before compact-schema migration.
/// </summary>
[SqlTable(_table)]
internal sealed class LegacyContractDefinitionRow
{
    private const string _table = "direct_relationship_contracts";
    private const string _id = "contract_id";
    private const string _definition = "definition_json";

    /// <summary>
    /// Gets or initializes the complete original relationship contract identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the complete original JSON definition before compact-schema migration.
    /// </summary>
    [SqlColumn(_definition)]
    public string Definition { get; init; } = string.Empty;
}
