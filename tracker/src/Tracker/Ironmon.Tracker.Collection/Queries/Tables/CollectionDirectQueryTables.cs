using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Collection.Queries.Tables;

/// <summary>
/// Maps stored direct_dimension_atoms columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectAtomLookupRow
{
    private const string _table = "direct_dimension_atoms";
    private const string _atom = "atom_id";
    private const string _valueId = "value_id";

    /// <summary>
    /// Gets or initializes the interned exact dimension atom identity.
    /// </summary>
    [SqlColumn(_atom)]
    public long Atom { get; init; }

    /// <summary>
    /// Gets or initializes the immutable dictionary value identity.
    /// </summary>
    [SqlColumn(_valueId)]
    public long ValueId { get; init; }
}

/// <summary>
/// Maps stored direct_atom_members columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectAtomMemberRow
{
    private const string _table = "direct_atom_members";
    private const string _atom = "atom_id";
    private const string _member = "member_value_id";

    /// <summary>
    /// Gets or initializes the interned exact dimension atom identity.
    /// </summary>
    [SqlColumn(_atom)]
    public long Atom { get; init; }

    /// <summary>
    /// Gets or initializes the immutable dictionary member identity.
    /// </summary>
    [SqlColumn(_member)]
    public long Member { get; init; }
}

/// <summary>
/// Maps stored direct_series_dimension_refs columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectDimensionRefRow
{
    private const string _table = "direct_series_dimension_refs";
    private const string _series = "series_id";
    private const string _group = "group_id";
    private const string _dimension = "dimension_no";
    private const string _atom = "atom_id";
    private const string _kindCode = "value_kind_code";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered direct-summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the stable dimension ordinal.
    /// </summary>
    [SqlColumn(_dimension)]
    public int Dimension { get; init; }

    /// <summary>
    /// Gets or initializes the interned exact dimension atom identity.
    /// </summary>
    [SqlColumn(_atom)]
    public long Atom { get; init; }

    /// <summary>
    /// Gets or initializes the stored dimension-kind code.
    /// </summary>
    [SqlColumn(_kindCode)]
    public int KindCode { get; init; }
}

/// <summary>
/// Maps stored direct_measure_states columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectCoverageRow
{
    private const string _table = "direct_measure_states";
    private const string _series = "series_id";
    private const string _group = "group_id";
    private const string _measure = "measure_no";
    private const string _kind = "measure_kind";
    private const string _presence = "known_presence";
    private const string _knownInteger = "known_count_i64";
    private const string _knownWide = "known_count_decimal";
    private const string _unknownInteger = "unknown_count_i64";
    private const string _unknownWide = "unknown_count_decimal";
    private const string _unsupportedInteger = "unsupported_count_i64";
    private const string _unsupportedWide = "unsupported_count_decimal";
    private const string _notApplicableInteger = "not_applicable_count_i64";
    private const string _notApplicableWide = "not_applicable_count_decimal";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered direct-summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the declared scalar, set or rational measure kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the known-value presence flag, zero or one.
    /// </summary>
    [SqlColumn(_presence)]
    public int Presence { get; init; }

    /// <summary>
    /// Gets or initializes the native known count, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_knownInteger)]
    public long? KnownInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact known overflow count text, or null for a native integer.
    /// </summary>
    [SqlColumn(_knownWide)]
    public string? KnownWide { get; init; }

    /// <summary>
    /// Gets or initializes the native unknown count, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_unknownInteger)]
    public long? UnknownInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact unknown overflow count text, or null for a native integer.
    /// </summary>
    [SqlColumn(_unknownWide)]
    public string? UnknownWide { get; init; }

    /// <summary>
    /// Gets or initializes the native unsupported count, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_unsupportedInteger)]
    public long? UnsupportedInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact unsupported overflow count text, or null for a native integer.
    /// </summary>
    [SqlColumn(_unsupportedWide)]
    public string? UnsupportedWide { get; init; }

    /// <summary>
    /// Gets or initializes the native not-applicable count, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_notApplicableInteger)]
    public long? NotApplicableInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact not-applicable overflow count text, or null for a native integer.
    /// </summary>
    [SqlColumn(_notApplicableWide)]
    public string? NotApplicableWide { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps stored direct_rational_extrema columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectRatioRow
{
    private const string _table = "direct_rational_extrema";
    private const string _series = "series_id";
    private const string _group = "group_id";
    private const string _measure = "measure_no";
    private const string _numerator = "numerator_i64";
    private const string _numeratorWide = "numerator_decimal";
    private const string _denominator = "denominator_i64";
    private const string _denominatorWide = "denominator_decimal";
    private const string _witness = "witness_id";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered direct-summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the native nonnegative numerator, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_numerator)]
    public long? Numerator { get; init; }

    /// <summary>
    /// Gets or initializes the exact numerator overflow text, or null for a native integer.
    /// </summary>
    [SqlColumn(_numeratorWide)]
    public string? NumeratorWide { get; init; }

    /// <summary>
    /// Gets or initializes the native positive denominator, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_denominator)]
    public long? Denominator { get; init; }

    /// <summary>
    /// Gets or initializes the exact denominator overflow text, or null for a native integer.
    /// </summary>
    [SqlColumn(_denominatorWide)]
    public string? DenominatorWide { get; init; }

    /// <summary>
    /// Gets or initializes the retained source witness identity.
    /// </summary>
    [SqlColumn(_witness)]
    public long Witness { get; init; }
}

/// <summary>
/// Maps stored direct_exact_set_members columns for compile-time queries without changing the schema.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectSetMemberRow
{
    private const string _table = "direct_exact_set_members";
    private const string _series = "series_id";
    private const string _group = "group_id";
    private const string _measure = "measure_no";
    private const string _namespace = "member_namespace";
    private const string _member = "member_value_id";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered direct-summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the exact-member namespace discriminator.
    /// </summary>
    [SqlColumn(_namespace)]
    public string Namespace { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the immutable dictionary member identity.
    /// </summary>
    [SqlColumn(_member)]
    public long Member { get; init; }
}

/// <summary>
/// Maps stored direct_contexts columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectContextRow
{
    private const string _table = "direct_contexts";
    private const string _id = "direct_context_id";
    private const string _meta = "meta_id";
    private const string _kind = "context_kind";
    private const string _scope = "scope_id";
    private const string _key = "canonical_key";
    private const string _state = "context_state";

    /// <summary>
    /// Gets or initializes the exact direct context identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the owning collection metadata singleton.
    /// </summary>
    [SqlColumn(_meta)]
    public int Meta { get; init; }

    /// <summary>
    /// Gets or initializes the declared direct context kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the interned lifetime, attempt or battle scope.
    /// </summary>
    [SqlColumn(_scope)]
    public long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the complete canonical binary key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the active direct context state.
    /// </summary>
    [SqlColumn(_state)]
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// Maps stored direct_dimension_atoms columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectAtomRow
{
    private const string _table = "direct_dimension_atoms";
    private const string _id = "atom_id";
    private const string _kind = "value_kind";
    private const string _coverage = "coverage_kind";
    private const string _integer = "int_value";
    private const string _text = "text_value";
    private const string _blob = "blob_value";
    private const string _numerator = "ratio_numerator";
    private const string _denominator = "ratio_denominator";
    private const string _value = "value_id";

    /// <summary>
    /// Gets or initializes the exact interned atom identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the declared exact dimension value kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the known or unavailable evidence class.
    /// </summary>
    [SqlColumn(_coverage)]
    public string Coverage { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the native signed value, or null for another value kind.
    /// </summary>
    [SqlColumn(_integer)]
    public long? Integer { get; init; }

    /// <summary>
    /// Gets or initializes the exact text value, or null for another value kind.
    /// </summary>
    [SqlColumn(_text)]
    public string? Text { get; init; }

    /// <summary>
    /// Gets or initializes the exact binary value, or null for another value kind.
    /// </summary>
    [SqlColumn(_blob)]
    public byte[]? Blob { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative rational numerator, or null for another value kind.
    /// </summary>
    [SqlColumn(_numerator)]
    public long? Numerator { get; init; }

    /// <summary>
    /// Gets or initializes the exact positive rational denominator, or null for another value kind.
    /// </summary>
    [SqlColumn(_denominator)]
    public long? Denominator { get; init; }

    /// <summary>
    /// Gets or initializes the immutable dictionary identity for the complete typed atom.
    /// </summary>
    [SqlColumn(_value)]
    public long Value { get; init; }
}

/// <summary>
/// Maps stored direct_series columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectSeriesRow
{
    private const string _table = "direct_series";
    private const string _id = "series_id";
    private const string _group = "group_id";
    private const string _context = "direct_context_id";
    private const string _format = "key_format_version";
    private const string _key = "canonical_key";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact direct summary series identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the registered direct-summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the interned direct summary context.
    /// </summary>
    [SqlColumn(_context)]
    public long Context { get; init; }

    /// <summary>
    /// Gets or initializes the stored dimension key format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the complete canonical binary key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps stored direct_witnesses columns used by generated identity resolution.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectWitnessRow
{
    private const string _table = "direct_witnesses";
    private const string _id = "witness_id";
    private const string _series = "series_id";
    private const string _group = "group_id";
    private const string _measure = "measure_no";
    private const string _slot = "witness_slot";
    private const string _kind = "witness_kind";
    private const string _attempt = "attempt_id";
    private const string _stream = "source_stream_id";
    private const string _sequence = "source_sequence";
    private const string _revision = "source_revision";

    /// <summary>
    /// Gets or initializes the exact retained source witness identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the registered direct-summary group identity.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the stable measure ordinal.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the single retained witness slot.
    /// </summary>
    [SqlColumn(_slot)]
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the retained witness kind.
    /// </summary>
    [SqlColumn(_kind)]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the sixteen-byte owning attempt identity, or null when absent.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[]? Attempt { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte producer stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps only the stored direct-scalar columns; SQLite retains ownership of its generated columns.
/// </summary>
[SqlTable(_table)]
internal sealed class DirectScalarRow
{
    private const string _table = "direct_scalar_values";
    private const string _series = "series_id";
    private const string _group = "group_id";
    private const string _measure = "measure_no";
    private const string _domain = "value_domain";
    private const string _integer = "value_i64";
    private const string _wide = "value_decimal";
    private const string _witness = "witness_id";

    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    [SqlColumn(_series)]
    public long Series { get; init; }

    /// <summary>
    /// Gets or initializes the owning direct-summary group.
    /// </summary>
    [SqlColumn(_group)]
    public int Group { get; init; }

    /// <summary>
    /// Gets or initializes the measure ordinal within its group.
    /// </summary>
    [SqlColumn(_measure)]
    public int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the existing value-domain discriminator.
    /// </summary>
    [SqlColumn(_domain)]
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exact native signed integer, or null for decimal overflow.
    /// </summary>
    [SqlColumn(_integer)]
    public long? Integer { get; init; }

    /// <summary>
    /// Gets or initializes the exact overflow decimal text, or null when the native integer is used.
    /// </summary>
    [SqlColumn(_wide)]
    public string? Wide { get; init; }

    /// <summary>
    /// Gets or initializes the retained witness identity when present.
    /// </summary>
    [SqlColumn(_witness)]
    public long? Witness { get; init; }
}
