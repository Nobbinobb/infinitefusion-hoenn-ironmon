using Ironmon.Tracker.Collection.Reduction;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Persists exact generic aggregate primitives using compact relational keys.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _known = "known";
    private const string _unknown = "unknown";
    private const string _unsupported = "unsupported";
    private const string _notApplicable = "not_applicable";
    private const string _scalar = "scalar";
    private const string _exactSet = "exact_set";
    private const string _orderedPayload = "ordered_payload";
    private const string _sum = "sum";
    private const string _min = "min";
    private const string _max = "max";
    private const string _orderedReplace = "ordered_replace";
    private const string _scopeLifetime = "profile_lifetime";
    private const string _scopeAttempt = "attempt";
    private const string _scopeBattle = "battle";
    private const string _scopeLifetimeAlias = "profile_since_tracking_release";
    private const string _lifetimeAlias = "lifetime";
    private const string _profileAlias = "profile";
    private const string _currentAttemptScope = "current_attempt";
    private const string _individualScope = "individual";
    private const string _sessionScope = "session";
    private const string _saveLineageScope = "save_lineage";
    private const string _wardrobeSessionScope = "wardrobe_session";
    private const string _cosmeticProfileScope = "cosmetic_profile";
    private const string _trackingEpochScope = "tracking_epoch";
    private const string _cosmeticEpochScope = "cosmetic_profile_tracking_epoch";
    private const string _profileCurrentScope = "profile_current";
    private const string _currentSaveScope = "current_save";
    private const string _snapshotScope = "current_snapshot_only";
    private const string _inventoryScope = "active_inventory";
    private const string _partyScope = "active_party";
    private const string _entityContextProperty = "entity_id";
    private const string _sessionContextProperty = "session_id";
    private const string _saveLineageProperty = "save_lineage_id";
    private const string _wardrobeSessionProperty = "wardrobe_session_id";
    private const string _familyDefinitionBlock = "family_definition";
    private const string _collectionContext = "collection_context";
    private const string _battleProperty = "battle_id";
    private const string _physicalOrdered = "ordered_current";
    private const string _physicalSet = "set_union";
    private const string _physicalExtrema = "extrema";
    private const string _physicalJoint = "joint_additive_extrema";
    private const string _physicalAdditive = "additive";

    private const string _familyNamespacePrefix = "dimension/";
    private const string _memberNamespacePrefix = "member/";
    private readonly Dictionary<string, long> _namespaceCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _identityCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _tupleCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _blockKinds = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Kind, string Key), long> _scopeCache = [];

    /// <summary>
    /// Registers generic immutable catalogue metadata without any earned samples.
    /// </summary>
    private void InitializeCatalogue()
    {
        using SqliteTransaction transaction = _db.BeginTransaction();
        _transaction = transaction;
        long catalogueBlock = CurrentCatalogueBlock();
        foreach (CollectionFamilyDefinition family in _catalogue.Families)
        {
            byte[] definition = CollectionEncoding.Canonical(family.Definition);
            long block = catalogueBlock;
            CollectionQueries.EnsureFamilyDefinition(_db, _transaction, family.Id, family.Key, family.OwnerEvent, PhysicalFamilyKind(family), block, SHA256.HashData(definition));
            FamilyBindingRecord? binding = CollectionQueries.ReadFamilyBinding(_db, _transaction, family.Id);
            if (binding is null || binding.Key != family.Key || !binding.Digest.AsSpan().SequenceEqual(SHA256.HashData(definition)))
                throw new NotSupportedException("The collection catalogue changed. Existing data is preserved and requires an explicit schema/projection migration.");

            foreach (CollectionMeasureDefinition measure in family.Measures)
                CollectionQueries.EnsureFamilyMeasure(_db, _transaction, family.Id, measure.Id, measure.Key, measure.Kind, measure.Unit, measure.MergeOperator);
        }

        InitializeDirectGroups(catalogueBlock);
        transaction.Commit();
        _transaction = null;
    }

    /// <summary>
    /// Interns one exact native identity without copying labels into every observation.
    /// </summary>
    /// <param name="nameSpace">The identity namespace that keeps unrelated native identifier domains distinct.</param>
    /// <param name="value">The exact native identity value, retaining string-versus-number distinctions.</param>
    /// <returns>The interned database identity identifier for this namespace and exact value.</returns>
    private long Identity(string nameSpace, JsonElement value)
    {
        long key = _values.Intern(value);
        string cacheKey = nameSpace + _colonSeparator + key.ToString(CultureInfo.InvariantCulture);
        if (_identityCache.TryGetValue(cacheKey, out long identity))
            return identity;

        if (!_namespaceCache.TryGetValue(nameSpace, out long namespaceId))
        {
            namespaceId = CollectionQueries.InternNamespace(_db, _transaction, nameSpace);
            _namespaceCache[nameSpace] = namespaceId;
        }

        identity = CollectionQueries.InternIdentity(_db, _transaction, namespaceId, key);
        if (_identityCache.Count < 32768)
            _identityCache[cacheKey] = identity;

        return identity;
    }

    /// <summary>
    /// Interns a complete ordered tuple including explicit unknown and typed zero distinctions.
    /// </summary>
    /// <param name="dimensions">The ordered joint dimensions, including typed values and explicit unavailable evidence.</param>
    /// <returns>The interned tuple identifier preserving dimension order and availability.</returns>
    private long Tuple(IReadOnlyList<CollectionDimensionValue> dimensions)
    {
        long[] identities = [.. dimensions.Select(dimension => dimension.Coverage == _known && dimension.Kind == _nativeKind
            ? Identity(NativeNamespace(dimension.Key), dimension.Value)
            : Identity(_familyNamespacePrefix + dimension.Key, JsonSerializer.SerializeToElement(new { kind = dimension.Kind, coverage = dimension.Coverage, value = dimension.Value })))];

        byte[] key = [.. identities.SelectMany(identity => CollectionEncoding.Unsigned(identity))];
        string cacheKey = Convert.ToHexString(key);
        if (_tupleCache.TryGetValue(cacheKey, out long tuple))
            return tuple;

        tuple = CollectionQueries.InternTuple(_db, _transaction, key, identities.Length);
        for (int start = 0; start < identities.Length; start += 4)
            CollectionQueries.SaveTupleGroup(_db, _transaction, tuple, start / 4, identities[start], start + 1 < identities.Length ? identities[start + 1] : null, start + 2 < identities.Length ? identities[start + 2] : null, start + 3 < identities.Length ? identities[start + 3] : null);

        if (_tupleCache.Count < 32768)
            _tupleCache[cacheKey] = tuple;

        return tuple;
    }

    /// <summary>
    /// Interns an immutable compressed block with digest and actual byte equality.
    /// </summary>
    /// <param name="kind">The immutable block category used to separate different payload formats.</param>
    /// <param name="raw">The uncompressed bytes whose content must be preserved exactly.</param>
    /// <returns>The identifier of an existing identical block or the newly stored compressed block.</returns>
    private long Block(string kind, byte[] raw)
    {
        int format = kind is _familyDefinitionBlock or _resourceMetadataKind or _catalogueMetadataKind ? 1 : kind == _coldBlockKind ? 3 : 2;
        if (format is 2 or 3)
        {
            using JsonDocument value = JsonDocument.Parse(raw);
            raw = format == 3 ? CollectionArchiveEncoding.Encode(value.RootElement) : _values.Encode(value.RootElement);
        }

        if (!_blockKinds.TryGetValue(kind, out long kindId))
        {
            kindId = CollectionQueries.InternBlockKind(_db, _transaction, kind);
            _blockKinds[kind] = kindId;
        }

        byte[] digest = SHA256.HashData(raw);
        foreach (BlockCandidateRecord candidate in CollectionQueries.FindBlockCandidates(_db, _transaction, kindId, format, digest[..8], digest, raw.LongLength))
        {
            byte[] stored = candidate.Codec == _rawBlockCodec ? candidate.Payload : CollectionEncoding.Decompress(candidate.Payload, raw.LongLength, digest);
            if (stored.AsSpan().SequenceEqual(raw))
                return candidate.Id;
        }

        byte[] compressed = CollectionEncoding.Compress(raw);
        bool smaller = compressed.Length < raw.Length;
        return CollectionQueries.InsertBlock(_db, _transaction, kindId, format, smaller ? _compressedBlockCodec : _rawBlockCodec, raw.LongLength, digest, smaller ? compressed : raw);
    }

    /// <summary>
    /// Resolves a canonical lifetime, actual attempt or active battle scope.
    /// </summary>
    /// <param name="kind">The logical scope kind declared by the collection catalogue.</param>
    /// <param name="key">The canonical binary identity of this scope.</param>
    /// <param name="attempt">The owning attempt UUID, or null for scopes shared across attempts.</param>
    /// <returns>The interned identifier of the requested collection scope.</returns>
    private long Scope(string kind, byte[] key, byte[]? attempt)
    {
        (string Kind, string Key) cacheKey = (kind, Convert.ToHexString(key));
        if (_scopeCache.TryGetValue(cacheKey, out long scope))
            return scope;

        long? parent = kind == _scopeLifetime || _lifetimeScope == 0 ? null : _lifetimeScope;
        scope = CollectionQueries.InternScope(_db, _transaction, kind, key, parent, attempt);
        if (_scopeCache.Count < 32768)
            _scopeCache[cacheKey] = scope;

        return scope;
    }

    /// <summary>
    /// Returns the real scopes declared for a family and present in this occurrence.
    /// </summary>
    /// <param name="family">The family definition declaring which real event scopes receive this occurrence.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <returns>Distinct real scope identifiers supported by this event and the family declaration.</returns>
    private IEnumerable<long> ProjectionScopes(CollectionFamilyDefinition family, JsonElement occurrence)
    {
        byte[]? attempt = OptionalIdentifier(occurrence, _attemptProperty);
        HashSet<long> emitted = [];
        foreach (string kind in family.Scopes)
        {
            long? scope = kind.Contains(_scopeLifetime, StringComparison.Ordinal) || kind == _scopeLifetimeAlias || kind == _lifetimeAlias || kind == _profileAlias ? _lifetimeScope
                : kind is _scopeAttempt or _currentAttemptScope && attempt is not null ? Scope(kind, attempt, attempt)
                : kind == _scopeBattle && attempt is not null && occurrence.TryGetProperty(_battleProperty, out JsonElement battle) && battle.ValueKind == JsonValueKind.String ? Scope(_scopeBattle, _values.Encode(JsonSerializer.SerializeToElement(new { attempt = CollectionEncoding.Identifier(attempt), battle = battle.GetString() })), attempt)
                : kind == _cosmeticProfileScope ? Scope(kind, _profile!, null)
                : kind is _trackingEpochScope or _cosmeticEpochScope or _profileCurrentScope or _currentSaveScope or _snapshotScope or _inventoryScope or _partyScope ? Scope(kind, _epoch!, null)
                : SpecificScope(kind, occurrence, attempt);

            if (scope is long known && emitted.Add(known))
                yield return known;
        }
    }

    /// <summary>
    /// Resolves narrower actual identities only when the source disclosed them.
    /// </summary>
    /// <param name="kind">The narrower scope kind whose source identity is required.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <param name="attempt">The owning attempt UUID, or null when no attempt was disclosed.</param>
    /// <returns>The interned narrower scope identifier, or null when its source identity was not disclosed.</returns>
    private long? SpecificScope(string kind, JsonElement occurrence, byte[]? attempt)
    {
        string property = kind switch { _individualScope => _ownedEntityProperty, _sessionScope => _sessionContextProperty, _saveLineageScope => _saveLineageProperty, _wardrobeSessionScope => _wardrobeSessionProperty, _ => string.Empty };
        if (property.Length == 0)
            return null;

        JsonElement context = occurrence.TryGetProperty(_contextProperty, out JsonElement contextValue) ? contextValue : default;
        JsonElement payload = occurrence.TryGetProperty(_payloadProperty, out JsonElement payloadValue) ? payloadValue : default;
        JsonElement identity = context.ValueKind == JsonValueKind.Object && context.TryGetProperty(property, out JsonElement first) ? first : payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(property, out first) ? first : default;
        if (identity.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined && kind == _individualScope && context.ValueKind == JsonValueKind.Object && context.TryGetProperty(_entityContextProperty, out JsonElement entity))
            identity = entity;

        if (identity.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return Scope(kind, _values.Encode(JsonSerializer.SerializeToElement(new { attempt = attempt is null ? null : CollectionEncoding.Identifier(attempt), id = identity })), attempt);
    }

    /// <summary>
    /// Persists one exact generic family projection at its declared scopes.
    /// </summary>
    /// <param name="projection">The reducer output with its exact dimensions, measure updates and evidence coverage.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void ApplyProjection(CollectionProjection projection, JsonElement occurrence)
    {
        if (!_families.TryGetValue(projection.FamilyKey, out CollectionFamilyDefinition? family))
            throw new InvalidDataException("Reducer emitted an undeclared aggregate family.");

        long tuple = Tuple(projection.Dimensions);
        foreach (long scope in ProjectionScopes(family, occurrence))
        {
            ThawPartition(scope, family.Id);
            long partition = CollectionQueries.TouchPartition(_db, _transaction, family.Id, scope, CollectionEncoding.Revision(_revision));
            long series = CollectionQueries.InternSeries(_db, _transaction, partition, family.Id, tuple);
            foreach (CollectionMeasureUpdate update in projection.Measures)
            {
                CollectionMeasureDefinition measure = family.Measures.Single(measure => measure.Key == update.Key);
                if (measure.Kind != update.Kind || measure.MergeOperator != update.MergeOperator)
                    throw new InvalidDataException("Reducer measure type or merge law disagrees with its catalogue.");

                if (measure.Kind == _scalar)
                {
                    ApplyScalar(family.Id, series, measure, update);
                }
                else if (measure.Kind == _exactSet)
                {
                    ApplySet(family.Id, series, measure, update);
                }
                else
                {
                    ApplyOrdered(family.Id, scope, tuple, update, occurrence);
                }
            }
        }
    }

    /// <summary>
    /// Merges exact scalar values and all availability populations.
    /// </summary>
    /// <param name="family">The registered aggregate family identifier.</param>
    /// <param name="series">The exact dimension-series identifier receiving this update.</param>
    /// <param name="measure">The scalar measure definition supplying its stable ordinal and merge operation.</param>
    /// <param name="update">The exact value, merge operation and evidence coverage to persist.</param>
    private void ApplyScalar(int family, long series, CollectionMeasureDefinition measure, CollectionMeasureUpdate update)
    {
        BigInteger? prior = null;
        AggregateCellRecord? stored = CollectionQueries.ReadCell(_db, _transaction, series, measure.Id);
        if (stored is not null)
            prior = CollectionEncoding.ReadScalar(stored.Value);

        BigInteger[] coverage = stored is null ? [0, 0, 0, 0] : [CollectionEncoding.ReadCount(stored.Known), CollectionEncoding.ReadCount(stored.Unknown), CollectionEncoding.ReadCount(stored.Unsupported), CollectionEncoding.ReadCount(stored.NotApplicable)];
        AddCoverage(coverage, update.Coverage);
        BigInteger? result = update.Coverage == _known ? Merge(prior, update.Scalar ?? throw new InvalidDataException("A known scalar has no value."), measure.MergeOperator) : prior;
        CollectionQueries.SaveCell(_db, _transaction, series, family, measure.Id, CollectionEncoding.Scalar(result), CollectionEncoding.Count(coverage[0]), CollectionEncoding.Count(coverage[1]), CollectionEncoding.Count(coverage[2]), CollectionEncoding.Count(coverage[3]), CollectionEncoding.Revision(_revision));
    }

    /// <summary>
    /// Merges exact set membership while retaining known-empty and unavailable evidence.
    /// </summary>
    /// <param name="family">The registered aggregate family identifier.</param>
    /// <param name="series">The exact dimension-series identifier receiving this update.</param>
    /// <param name="measure">The exact-set measure definition supplying its stable ordinal and union operation.</param>
    /// <param name="update">The exact value, merge operation and evidence coverage to persist.</param>
    private void ApplySet(int family, long series, CollectionMeasureDefinition measure, CollectionMeasureUpdate update)
    {
        AggregateCoverageRecord? stored = CollectionQueries.ReadSetCoverage(_db, _transaction, series, measure.Id);
        BigInteger[] coverage = stored is null ? [0, 0, 0, 0] : [CollectionEncoding.ReadCount(stored.Known), CollectionEncoding.ReadCount(stored.Unknown), CollectionEncoding.ReadCount(stored.Unsupported), CollectionEncoding.ReadCount(stored.NotApplicable)];

        AddCoverage(coverage, update.Coverage);
        if (update.Coverage == _known)
        {
            if (measure.MergeOperator == _orderedReplace)
                CollectionQueries.DeleteSetMembers(_db, _transaction, series, measure.Id);

            foreach (JsonElement member in update.Members ?? throw new InvalidDataException("A known membership update has no exact members."))
                CollectionQueries.AddSetMember(_db, _transaction, series, family, measure.Id, Identity(_memberNamespacePrefix + measure.Key, member));
        }

        CollectionQueries.SaveSetCoverage(_db, _transaction, series, family, measure.Id, CollectionEncoding.Count(coverage[0]), CollectionEncoding.Count(coverage[1]), CollectionEncoding.Count(coverage[2]), CollectionEncoding.Count(coverage[3]), CollectionEncoding.Revision(_revision));
    }

    /// <summary>
    /// Stores a reducer's sufficient ordered state rather than a permanent event sequence.
    /// </summary>
    /// <param name="family">The registered aggregate family identifier.</param>
    /// <param name="scope">The interned lifetime, attempt or battle scope identifier.</param>
    /// <param name="tuple">The interned ordered dimension tuple for this sufficient state.</param>
    /// <param name="update">The exact value, merge operation and evidence coverage to persist.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void ApplyOrdered(int family, long scope, long tuple, CollectionMeasureUpdate update, JsonElement occurrence)
    {
        byte[]? previous = CollectionQueries.ReadOrdered(_db, _transaction, family, scope, tuple)?.Payload;
        JsonObject values = previous is byte[] bytes ? JsonNode.Parse(_values.Decode(CollectionEncoding.Inflate(bytes, _maximumBatchBytes)).GetRawText())?.AsObject() ?? throw new InvalidDataException("Ordered working state was null.") : new JsonObject();
        values[update.Key] = JsonSerializer.SerializeToNode(new { coverage = update.Coverage, value = update.OrderedState });
        byte[] state = _values.Encode(JsonSerializer.SerializeToElement(values));
        CollectionQueries.SaveOrdered(_db, _transaction, family, scope, tuple, CollectionEncoding.Compress(state), CollectionEncoding.Identifier(RequiredString(_currentBatch, _streamProperty)), CollectionEncoding.Revision(CollectionEncoding.Sequence(occurrence.GetProperty(_sequenceProperty))), CollectionEncoding.Revision(_revision));
    }

    /// <summary>
    /// Merges a single typed scalar using its declared operator.
    /// </summary>
    /// <param name="previous">The previous exact scalar, or null when no known value has been stored.</param>
    /// <param name="next">The incoming exact scalar value.</param>
    /// <param name="operation">The declared sum, minimum, maximum or ordered-replacement operator.</param>
    /// <returns>The exact merged scalar, using the incoming value when no prior value exists.</returns>
    private static BigInteger Merge(BigInteger? previous, BigInteger next, string operation)
        => previous is null ? next : operation switch { _sum => previous.Value + next, _min => BigInteger.Min(previous.Value, next), _max => BigInteger.Max(previous.Value, next), _orderedReplace => next, _ => throw new InvalidDataException("Unsupported scalar merge operator.") };

    /// <summary>
    /// Increments the exact coverage class, never treating missing as zero.
    /// </summary>
    /// <param name="values">The four exact counters ordered as known, unknown, unsupported and not applicable.</param>
    /// <param name="coverage">The known, unknown, unsupported or not-applicable population to increment.</param>
    private static void AddCoverage(BigInteger[] values, string coverage)
        => values[coverage switch { _known => 0, _unknown => 1, _unsupported => 2, _notApplicable => 3, _ => throw new InvalidDataException("Unknown availability class.") }]++;

    /// <summary>
    /// Clears bounded SQL-ID caches after rollback so no uncommitted identity survives.
    /// </summary>
    private void ClearCaches()
    {
        _namespaceCache.Clear();
        _identityCache.Clear();
        _tupleCache.Clear();
        _blockKinds.Clear();
        _atomCache.Clear();
        _values.ClearCaches();
        _archiveReadCache.Clear();
        _scopeCache.Clear();
        _knownAttempts.Clear();
        _directContextCache.Clear();
        _checkedHotPartitions.Clear();
        _verifiedOpenStream = null;
    }

    /// <summary>
    /// Maps logical collector ownership to the schema's generic physical merge class.
    /// </summary>
    /// <param name="family">The logical family definition whose measure kinds determine its physical representation.</param>
    /// <returns>The physical ordered, set, extrema, joint or additive family class.</returns>
    private static string PhysicalFamilyKind(CollectionFamilyDefinition family) => family.Measures.Any(measure => measure.Kind == _orderedPayload) ? _physicalOrdered
        : family.Measures.All(measure => measure.Kind == _exactSet) ? _physicalSet
        : family.Measures.All(measure => measure.MergeOperator is _min or _max) ? _physicalExtrema
        : family.Measures.Any(measure => measure.Kind == _exactSet) ? _physicalJoint
        : _physicalAdditive;

    /// <summary>
    /// Shares stable native identity namespaces across facts, configurations and optional display labels.
    /// </summary>
    /// <param name="key">The dimension key naming the species, move, ability, item, nature or family-local identity domain.</param>
    /// <returns>The shared native identifier namespace, or a family-local namespace for other keys.</returns>
    private static string NativeNamespace(string key) => key.Contains(_speciesNamespace, StringComparison.Ordinal) || key is _headProperty or _bodyProperty ? _speciesNamespace
        : key.Contains(_moveNamespace, StringComparison.Ordinal) ? _moveNamespace
        : key.Contains(_abilityNamespace, StringComparison.Ordinal) ? _abilityNamespace
        : key.Contains(_itemNamespace, StringComparison.Ordinal) ? _itemNamespace
        : key.Contains(_natureNamespace, StringComparison.Ordinal) ? _natureNamespace
        : _familyNamespacePrefix + key;
}
