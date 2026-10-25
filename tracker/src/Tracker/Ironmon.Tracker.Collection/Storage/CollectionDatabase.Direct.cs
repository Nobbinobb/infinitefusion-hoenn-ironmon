using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Reduction;
using Ironmon.Tracker.Collection.Storage.Encoding;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Maintains directly queryable typed summary values with shared exact dimension atoms.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _groupIdProperty = "id";
    private const string _keyProperty = "key";
    private const string _versionProperty = "version";
    private const string _variantProperty = "variant_key";
    private const string _directContextProperty = "context";
    private const string _sourceFamiliesProperty = "source_families";
    private const string _dimensionsProperty = "dimensions";
    private const string _measuresProperty = "measures";
    private const string _kindProperty = "kind";
    private const string _requiredProperty = "required";
    private const string _mergeProperty = "merge_operator";
    private const string _unitProperty = "unit";
    private const string _fieldsProperty = "fields";
    private const string _bindingProperty = "binding";
    private const string _fieldTypeProperty = "type";
    private const string _groupsProperty = "group_ids";
    private const string _integerKind = "integer";
    private const string _booleanKind = "boolean";
    private const string _textKind = "text";
    private const string _nativeKind = "native_id";
    private const string _uint64Kind = "uint64";
    private const string _rationalKind = "rational";
    private const string _setKind = "set";
    private const string _rationalExtremum = "rational_extremum";
    private const string _minimumRatio = "min_ratio";
    private const string _nonnegativeRational = "nonnegative_rational";
    private const string _nativeMembership = "native_membership";
    private const string _signedInteger = "signed_integer";
    private const string _ratioNumerator = "numerator";
    private const string _ratioDenominator = "denominator";
    private const string _colonSeparator = ":";
    private readonly Dictionary<string, (int Id, JsonElement Definition)> _directGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<long, long> _atomCache = [];
    private readonly Dictionary<string, long> _directContextCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers generic direct definitions and retained relationship bindings without samples.
    /// </summary>
    /// <param name="catalogueBlock">The immutable block retaining every complete catalogue definition.</param>
    private void InitializeDirectGroups(long catalogueBlock)
    {
        int groupId = 0;
        foreach (JsonElement group in _catalogue.DirectGroups)
        {
            groupId++;
            string key = RequiredString(group, _keyProperty);
            string stable = RequiredString(group, _groupIdProperty);
            _directGroups[stable] = (groupId, group);
            byte[] definition = CollectionEncoding.Canonical(group);
            CollectionQueries.EnsureSummaryGroup(_db, _transaction, groupId, key, group.GetProperty(_versionProperty).GetInt32(), RequiredString(group, _variantProperty), SHA256.HashData(definition), catalogueBlock);
            SummaryGroupBindingRecord? binding = CollectionQueries.ReadSummaryGroupBinding(_db, _transaction, groupId);
            if (binding is null || binding.Key != key || !binding.Digest.AsSpan().SequenceEqual(SHA256.HashData(definition)))
                throw new NotSupportedException("The collection summary catalogue changed. Existing data is preserved and requires an explicit projection migration.");

            foreach (JsonElement source in group.GetProperty(_sourceFamiliesProperty).EnumerateArray())
            {
                if (_families.TryGetValue(source.GetString()!, out CollectionFamilyDefinition? family))
                    CollectionQueries.AddSummaryGroupSource(_db, _transaction, groupId, family.Id);
            }

            int dimensionNo = 0;
            foreach (JsonElement dimension in group.GetProperty(_dimensionsProperty).EnumerateArray())
                CollectionQueries.EnsureSummaryDimension(_db, _transaction, groupId, dimensionNo++, RequiredString(dimension, _keyProperty), RequiredString(dimension, _kindProperty), dimension.GetProperty(_requiredProperty).GetBoolean() ? 1 : 0);

            int measureNo = 0;
            foreach (JsonElement measure in group.GetProperty(_measuresProperty).EnumerateArray())
            {
                string kind = RequiredString(measure, _kindProperty);
                string domain = kind == _exactSet ? _nativeMembership : kind == _rationalExtremum ? _nonnegativeRational : _signedInteger;
                CollectionQueries.EnsureSummaryMeasure(_db, _transaction, groupId, measureNo++, RequiredString(measure, _keyProperty), kind, domain, RequiredString(measure, _mergeProperty), RequiredString(measure, _unitProperty));
            }
        }

        int contractId = 0;
        foreach (JsonElement relationship in _catalogue.Relationships)
        {
            contractId++;
            byte[] raw = CollectionEncoding.Canonical(relationship);
            CollectionQueries.EnsureRelationshipContract(_db, _transaction, contractId, RequiredString(relationship, _keyProperty), relationship.GetProperty(_versionProperty).GetInt32(), SHA256.HashData(raw), catalogueBlock);
            int fieldNo = 0;
            foreach (JsonElement field in relationship.GetProperty(_fieldsProperty).EnumerateArray())
                CollectionQueries.EnsureRelationshipField(_db, _transaction, contractId, fieldNo++, RequiredString(field, _keyProperty), RequiredString(field, _fieldTypeProperty), RequiredString(field, _bindingProperty));

            foreach (JsonElement group in relationship.GetProperty(_groupsProperty).EnumerateArray())
                CollectionQueries.AddSummaryContract(_db, _transaction, _directGroups[group.GetString()!].Id, contractId);
        }
    }

    /// <summary>
    /// Interns one exact typed value and its known-empty versus unavailable distinctions.
    /// </summary>
    /// <param name="dimension">The typed dimension value and its evidence coverage to intern.</param>
    /// <returns>The interned atom identifier retaining its value kind and availability.</returns>
    private long Atom(CollectionDimensionValue dimension)
    {
        JsonElement normalizedValue = dimension.Kind == _setKind && dimension.Coverage == _known
            ? JsonSerializer.SerializeToElement(dimension.Value.EnumerateArray().Select(CollectionEncoding.Canonical).Select(Convert.ToHexString).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            : dimension.Value;
        long key = _values.Intern(JsonSerializer.SerializeToElement(new { kind = dimension.Kind, coverage = dimension.Coverage, value = normalizedValue }));
        if (_atomCache.TryGetValue(key, out long atom))
            return atom;

        DirectAtomRecord? found = CollectionQueries.FindAtom(_db, _transaction, key);
        if (found is not null)
            return found.Atom;

        long? integer = null;
        string? text = null;
        byte[]? blob = null;
        long? numerator = null;
        long? denominator = null;
        if (dimension.Coverage == _known)
        {
            if (dimension.Kind == _integerKind)
            {
                integer = ExactInteger(dimension.Value).CompareTo(long.MaxValue) <= 0 && ExactInteger(dimension.Value).CompareTo(long.MinValue) >= 0 ? (long)ExactInteger(dimension.Value) : throw new InvalidDataException("Integer dimension exceeds its declared signed range; use a uint64 or native dimension.");
            }
            else if (dimension.Kind == _booleanKind)
            {
                integer = dimension.Value.GetBoolean() ? 1 : 0;
            }
            else if (dimension.Kind == _textKind)
            {
                text = dimension.Value.GetString() ?? throw new InvalidDataException("Known text dimension cannot be null.");
            }
            else if (dimension.Kind == _nativeKind)
            {
                blob = _values.Encode(dimension.Value);
            }
            else if (dimension.Kind == _uint64Kind)
            {
                blob = CollectionEncoding.Revision(checked((ulong)ExactInteger(dimension.Value)));
            }
            else if (dimension.Kind == _rationalKind)
            {
                numerator = checked((long)ExactInteger(dimension.Value.GetProperty(_ratioNumerator)));
                denominator = checked((long)ExactInteger(dimension.Value.GetProperty(_ratioDenominator)));
            }
        }

        atom = CollectionQueries.InternAtom(_db, _transaction, dimension.Kind, dimension.Coverage, integer, text, blob, numerator, denominator, key);
        if (dimension.Kind == _setKind && dimension.Coverage == _known)
        {
            foreach (JsonElement member in dimension.Value.EnumerateArray())
                CollectionQueries.AddAtomMember(_db, _transaction, atom, _values.Intern(member));
        }

        if (_atomCache.Count < 32768)
            _atomCache[key] = atom;

        return atom;
    }

    /// <summary>
    /// Maintains one directly indexed summary without reconstructing historical blocks.
    /// </summary>
    /// <param name="projection">The reducer output with its exact dimensions, measure updates and evidence coverage.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void ApplyDirectProjection(CollectionDirectProjection projection, JsonElement occurrence)
    {
        _achievementChangedGroups.Add(projection.GroupId);
        (int groupId, JsonElement definition) = _directGroups[projection.GroupId];
        JsonElement[] dimensions = [.. definition.GetProperty(_dimensionsProperty).EnumerateArray()];
        if (dimensions.Length != projection.Dimensions.Count)
            throw new InvalidDataException("Direct projection does not contain its complete declared joint key.");

        string contextKind = RequiredString(definition, _directContextProperty);
        if (!_directContextCache.TryGetValue(contextKind, out long directContext))
        {
            directContext = CollectionQueries.InternDirectContext(_db, _transaction, contextKind, _lifetimeScope, _epoch!);
            if (_directContextCache.Count < 32768)
                _directContextCache[contextKind] = directContext;
        }

        long[] atoms = new long[dimensions.Length];
        for (int index = 0; index < dimensions.Length; index++)
        {
            CollectionDimensionValue value = projection.Dimensions[index];
            if (value.Key != RequiredString(dimensions[index], _keyProperty) || value.Kind != RequiredString(dimensions[index], _kindProperty))
                throw new InvalidDataException("Direct dimension order or type disagrees with its catalogue.");

            atoms[index] = Atom(value);
        }

        byte[] key = [.. atoms.SelectMany(atom => CollectionEncoding.Unsigned(atom))];
        long series = CollectionQueries.TouchDirectSeries(_db, _transaction, groupId, directContext, key, CollectionEncoding.Revision(_revision));
        for (int index = 0; index < atoms.Length; index++)
            CollectionQueries.AddDirectReference(_db, _transaction, series, groupId, index, atoms[index], DimensionKindCode(projection.Dimensions[index].Kind));

        JsonElement[] measures = [.. definition.GetProperty(_measuresProperty).EnumerateArray()];
        foreach (CollectionMeasureUpdate update in projection.Measures)
        {
            int measure = Array.FindIndex(measures, declaration => RequiredString(declaration, _keyProperty) == update.Key);
            if (measure < 0 || RequiredString(measures[measure], _kindProperty) != update.Kind || RequiredString(measures[measure], _mergeProperty) != update.MergeOperator)
                throw new InvalidDataException("Direct measure disagrees with its catalogue.");

            DirectCoverage(series, groupId, measure, update);
            if (update.Coverage != _known)
                continue;

            if (update.Kind == _scalar)
            {
                DirectScalar(series, groupId, measure, update, occurrence);
            }
            else if (update.Kind == _exactSet)
            {
                foreach (JsonElement member in update.Members ?? throw new InvalidDataException("Known direct set has no exact members."))
                    CollectionQueries.AddDirectSetMember(_db, _transaction, series, groupId, measure, _values.Intern(member));
            }
            else
                DirectRatio(series, groupId, measure, update, occurrence);
        }
    }

    /// <summary>
    /// Persists exact counts for all four availability classes.
    /// </summary>
    /// <param name="series">The exact dimension-series identifier receiving this update.</param>
    /// <param name="group">The registered directly indexed summary group identifier.</param>
    /// <param name="measure">The measure ordinal within the registered family or summary group.</param>
    /// <param name="update">The exact value, merge operation and evidence coverage to persist.</param>
    private void DirectCoverage(long series, int group, int measure, CollectionMeasureUpdate update)
    {
        DirectCoverageRecord? stored = CollectionQueries.ReadDirectCoverage(_db, _transaction, series, measure);
        BigInteger[] coverage = stored is null ? [0, 0, 0, 0] : [Pair(stored.KnownInteger, stored.KnownWide), Pair(stored.UnknownInteger, stored.UnknownWide), Pair(stored.UnsupportedInteger, stored.UnsupportedWide), Pair(stored.NotApplicableInteger, stored.NotApplicableWide)];

        AddCoverage(coverage, update.Coverage);
        CollectionQueries.SaveDirectCoverage(_db, _transaction, series, group, measure, update.Kind, coverage[0] > 0 ? 1 : 0, (long?)Small(coverage[0]), (string?)Wide(coverage[0]), (long?)Small(coverage[1]), (string?)Wide(coverage[1]), (long?)Small(coverage[2]), (string?)Wide(coverage[2]), (long?)Small(coverage[3]), (string?)Wide(coverage[3]), CollectionEncoding.Revision(_revision));
    }

    /// <summary>
    /// Merges a directly indexed exact integer and bounded extremum provenance.
    /// </summary>
    /// <param name="series">The exact dimension-series identifier receiving this update.</param>
    /// <param name="group">The registered directly indexed summary group identifier.</param>
    /// <param name="measure">The measure ordinal within the registered family or summary group.</param>
    /// <param name="update">The exact value, merge operation and evidence coverage to persist.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void DirectScalar(long series, int group, int measure, CollectionMeasureUpdate update, JsonElement occurrence)
    {
        DirectScalarRecord? stored = CollectionQueries.ReadScalar(_db, _transaction, series, measure);
        BigInteger? previous = stored is null ? null : Pair(stored.Integer, stored.Wide);

        BigInteger next = update.Scalar ?? throw new InvalidDataException("Known direct scalar has no value.");
        BigInteger result = Merge(previous, next, update.MergeOperator);
        long? witness = update.MergeOperator is _min or _max && (previous is null || result != previous) ? Witness(series, group, measure, occurrence) : null;
        if (previous is not null && update.MergeOperator is _min or _max && result == previous)
            return;

        CollectionQueries.SaveScalar(_db, _transaction, series, group, measure, (long?)Small(result), (string?)Wide(result), witness);
    }

    /// <summary>
    /// Merges exact nonnegative rational extrema without floating-point comparison.
    /// </summary>
    /// <param name="series">The exact dimension-series identifier receiving this update.</param>
    /// <param name="group">The registered directly indexed summary group identifier.</param>
    /// <param name="measure">The measure ordinal within the registered family or summary group.</param>
    /// <param name="update">The exact value, merge operation and evidence coverage to persist.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void DirectRatio(long series, int group, int measure, CollectionMeasureUpdate update, JsonElement occurrence)
    {
        JsonElement ratio = update.OrderedState ?? throw new InvalidDataException("Known ratio has no numerator and denominator.");
        BigInteger numerator = ExactInteger(ratio.GetProperty(_ratioNumerator));
        BigInteger denominator = ExactInteger(ratio.GetProperty(_ratioDenominator));
        if (numerator < 0 || denominator <= 0)
            throw new InvalidDataException("A ratio must have a nonnegative numerator and positive denominator.");

        DirectRatioRecord? stored = CollectionQueries.ReadRatio(_db, _transaction, series, measure);
        if (stored is not null)
        {
            BigInteger oldNumerator = Pair(stored.Numerator, stored.NumeratorWide);
            BigInteger oldDenominator = Pair(stored.Denominator, stored.DenominatorWide);
            int comparison = (numerator * oldDenominator).CompareTo(oldNumerator * denominator);
            if (update.MergeOperator == _minimumRatio ? comparison >= 0 : comparison <= 0)
                return;
        }

        long witness = Witness(series, group, measure, occurrence);
        CollectionQueries.SaveRatio(_db, _transaction, series, group, measure, (long?)Small(numerator), (string?)Wide(numerator), (long?)Small(denominator), (string?)Wide(denominator), witness);
    }

    /// <summary>
    /// Retains the actual source receipt of a winning summary value.
    /// </summary>
    /// <param name="series">The exact dimension-series identifier receiving this update.</param>
    /// <param name="group">The registered directly indexed summary group identifier.</param>
    /// <param name="measure">The measure ordinal within the registered family or summary group.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <returns>The stored witness identifier referencing the actual source receipt for the winning value.</returns>
    private long Witness(long series, int group, int measure, JsonElement occurrence)
        => CollectionQueries.SaveWitness(_db, _transaction, series, group, measure, OptionalIdentifier(occurrence, _attemptProperty), CollectionEncoding.Identifier(RequiredString(_currentBatch, _streamProperty)), CollectionEncoding.Revision(CollectionEncoding.Sequence(occurrence.GetProperty(_sequenceProperty))), CollectionEncoding.Revision(_revision));

    /// <summary>
    /// Returns the native column for a signed exact integer when it fits.
    /// </summary>
    /// <param name="value">The exact integer to place in the native signed 64-bit column when representable.</param>
    /// <returns>The signed 64-bit value when representable; otherwise null for the unused native column.</returns>
    private static object? Small(BigInteger value)
        => value >= long.MinValue && value <= long.MaxValue ? (long)value : null;

    /// <summary>
    /// Returns the canonical overflow column only when a native integer cannot hold the value.
    /// </summary>
    /// <param name="value">The exact integer to place in the decimal overflow column only when it exceeds signed 64-bit storage.</param>
    /// <returns>The canonical decimal string when native storage would overflow; otherwise null.</returns>
    private static object? Wide(BigInteger value)
        => value >= long.MinValue && value <= long.MaxValue ? null : CollectionEncoding.Decimal(value);

    /// <summary>
    /// Reads a native integer or canonical decimal overflow.
    /// </summary>
    /// <param name="integer">The native signed integer column, or database null when the decimal column is used.</param>
    /// <param name="wide">The canonical decimal overflow column used when the native integer is absent.</param>
    /// <returns>The exact integer from the populated native or decimal column.</returns>
    private static BigInteger Pair(long? integer, string? wide)
        => integer is long value ? value : BigInteger.Parse(wide ?? throw new InvalidDataException("An exact integer has neither native nor overflow storage."), CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads exact source numeric tokens or decimal-string quantities.
    /// </summary>
    /// <param name="value">The source JSON number or canonical decimal string representing an exact integer.</param>
    /// <returns>The exact integer represented by the source token or decimal string.</returns>
    private static BigInteger ExactInteger(JsonElement value)
        => BigInteger.Parse(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText(), CultureInfo.InvariantCulture);
}
