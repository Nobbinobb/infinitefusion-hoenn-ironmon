using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps existing aggregate_families columns used by generated archive and catalogue operations.
/// </summary>
[SqlTable(_table)]
internal sealed class AggregateFamilyRow
{
    private const string _table = "aggregate_families";
    private const string _id = "family_id";
    private const string _key = "family_key";
    private const string _version = "projection_version";
    private const string _event = "owner_event_key";
    private const string _kind = "storage_kind";
    private const string _block = "definition_block_id";
    private const string _digest = "definition_hash";

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the stable aggregate family key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the positive projection version.
    /// </summary>
    [SqlColumn(_version)]
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the owning event key.
    /// </summary>
    [SqlColumn(_event)]
    public string Event { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the physical aggregate storage kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete retained definition block identity.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }

    /// <summary>
    /// Gets or initializes the complete thirty-two-byte definition digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];
}

/// <summary>
/// Maps existing family_measures columns used by generated archive and catalogue operations.
/// </summary>
[SqlTable(_table)]
internal sealed class FamilyMeasureRow
{
    private const string _table = "family_measures";
    private const string _family = "family_id";
    private const string _id = "measure_no";
    private const string _key = "measure_key";
    private const string _kind = "measure_kind";
    private const string _codec = "value_codec";
    private const string _unit = "unit";
    private const string _merge = "merge_operator";

    /// <summary>
    /// Gets or initializes the registered aggregate family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based measure ordinal.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared measure kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exact value encoding identifier.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared unit of measurement.
    /// </summary>
    [SqlColumn(_unit)]
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared merge operation.
    /// </summary>
    [SqlColumn(_merge)]
    public string Merge { get; init; } = string.Empty;
}

/// <summary>
/// Maps existing aggregate_families columns without narrowing retained archive values.
/// </summary>
[SqlTable(_table)]
internal sealed class HistoricalFamilyRow
{
    private const string _table = "aggregate_families";
    private const string _id = "family_id";
    private const string _block = "definition_block_id";

    /// <summary>
    /// Gets or initializes the complete original family identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the original complete definition block identity.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }
}

/// <summary>
/// Maps existing direct_groups columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class SummaryGroupRow
{
    private const string _table = "direct_groups";
    private const string _id = "group_id";
    private const string _key = "projection_key";
    private const string _version = "projection_version";
    private const string _variant = "variant_key";
    private const string _shape = "shape";
    private const string _digest = "definition_digest";
    private const string _block = "definition_block_id";
    private const string _codecVersion = "numeric_codec_version";

    /// <summary>
    /// Gets or initializes the registered summary group identity.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the stable summary projection key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the positive projection version.
    /// </summary>
    [SqlColumn(_version)]
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the exact summary variant key.
    /// </summary>
    [SqlColumn(_variant)]
    public string Variant { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared summary group shape.
    /// </summary>
    [SqlColumn(_shape)]
    public string Shape { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete thirty-two-byte definition digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete retained definition block identity.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }

    /// <summary>
    /// Gets or initializes the positive numeric encoding version.
    /// </summary>
    [SqlColumn(_codecVersion)]
    public int CodecVersion { get; init; }
}

/// <summary>
/// Maps existing direct_group_sources columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class SummaryGroupSourceRow
{
    private const string _table = "direct_group_sources";
    private const string _group = "group_id";
    private const string _family = "family_id";

    /// <summary>
    /// Gets or initializes the registered summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the registered aggregate source family identity.
    /// </summary>
    [SqlColumn(_family)]
    public int Family { get; init; }
}

/// <summary>
/// Maps existing direct_dimensions columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class SummaryDimensionRow
{
    private const string _table = "direct_dimensions";
    private const string _group = "group_id";
    private const string _ordinal = "dimension_no";
    private const string _key = "dimension_key";
    private const string _kind = "value_kind";
    private const string _meaning = "meaning";
    private const string _required = "required";

    /// <summary>
    /// Gets or initializes the registered summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based dimension ordinal.
    /// </summary>
    [SqlColumn(_ordinal)]
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets or initializes the stable dimension key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared dimension kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the retained complete dimension meaning.
    /// </summary>
    [SqlColumn(_meaning)]
    public string Meaning { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the required-dimension marker, zero or one.
    /// </summary>
    [SqlColumn(_required)]
    public int Required { get; init; }
}

/// <summary>
/// Maps existing direct_measures columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class SummaryMeasureRow
{
    private const string _table = "direct_measures";
    private const string _group = "group_id";
    private const string _ordinal = "measure_no";
    private const string _key = "measure_key";
    private const string _kind = "measure_kind";
    private const string _domain = "value_domain";
    private const string _merge = "merge_operator";
    private const string _unit = "unit";
    private const string _meaning = "meaning";

    /// <summary>
    /// Gets or initializes the registered summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based measure ordinal.
    /// </summary>
    [SqlColumn(_ordinal)]
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared measure kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared exact value domain.
    /// </summary>
    [SqlColumn(_domain)]
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared merge operation.
    /// </summary>
    [SqlColumn(_merge)]
    public string Merge { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared measurement unit.
    /// </summary>
    [SqlColumn(_unit)]
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the retained complete measure meaning.
    /// </summary>
    [SqlColumn(_meaning)]
    public string Meaning { get; init; } = string.Empty;
}

/// <summary>
/// Maps existing direct_relationship_contracts columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class RelationshipContractRow
{
    private const string _table = "direct_relationship_contracts";
    private const string _id = "contract_id";
    private const string _key = "contract_key";
    private const string _version = "contract_version";
    private const string _digest = "definition_digest";
    private const string _block = "definition_block_id";

    /// <summary>
    /// Gets or initializes the registered relationship contract identity.
    /// </summary>
    [SqlColumn(_id)]
    public int Id { get; init; }

    /// <summary>
    /// Gets or initializes the stable relationship contract key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the positive contract version.
    /// </summary>
    [SqlColumn(_version)]
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the complete thirty-two-byte definition digest.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete retained definition block identity.
    /// </summary>
    [SqlColumn(_block)]
    public long Block { get; init; }
}

/// <summary>
/// Maps existing direct_relationship_fields columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class RelationshipFieldRow
{
    private const string _table = "direct_relationship_fields";
    private const string _contract = "contract_id";
    private const string _ordinal = "field_no";
    private const string _key = "field_key";
    private const string _type = "logical_type";
    private const string _binding = "storage_binding";
    private const string _meaning = "meaning";

    /// <summary>
    /// Gets or initializes the registered relationship contract identity.
    /// </summary>
    [SqlColumn(_contract)]
    public int Contract { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based relationship field ordinal.
    /// </summary>
    [SqlColumn(_ordinal)]
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets or initializes the stable relationship field key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared logical field type.
    /// </summary>
    [SqlColumn(_type)]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the declared physical storage binding.
    /// </summary>
    [SqlColumn(_binding)]
    public string Binding { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the retained complete field meaning.
    /// </summary>
    [SqlColumn(_meaning)]
    public string Meaning { get; init; } = string.Empty;
}

/// <summary>
/// Maps existing direct_group_contracts columns used by generated registration and coverage queries.
/// </summary>
[SqlTable(_table)]
internal sealed class SummaryContractRow
{
    private const string _table = "direct_group_contracts";
    private const string _group = "group_id";
    private const string _contract = "contract_id";

    /// <summary>
    /// Gets or initializes the registered summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the registered relationship contract identity.
    /// </summary>
    [SqlColumn(_contract)]
    public int Contract { get; init; }
}
