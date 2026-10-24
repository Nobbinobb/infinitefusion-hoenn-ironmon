using System.Security.Cryptography;
using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Retains actual owned individuals, meaningful configurations and exact shared vectors/movesets.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _ownedConfiguration = "pokemon.owned_configuration_committed";
    private const string _attemptContextKind = "attempt_context";
    private const string _attemptSummaryKind = "attempt_summary";
    private const string _ownedEntityProperty = "owned_entity_id";
    private const string _captureKindProperty = "capture_kind";
    private const string _teamCommitProperty = "team_commit_context";
    private const string _retainedProperty = "retained_after_commit";
    private const string _speciesIdentityProperty = "species_identity";
    private const string _speciesProperty = "canonical_species_id";
    private const string _formProperty = "form_id";
    private const string _headProperty = "fusion_head_id";
    private const string _bodyProperty = "fusion_body_id";
    private const string _levelProperty = "level";
    private const string _experienceProperty = "experience";
    private const string _generatedProperty = "generated_base_stats";
    private const string _referenceProperty = "reference_base_stats";
    private const string _computedProperty = "computed_stats";
    private const string _trainingProperty = "training_traits";
    private const string _rawIvProperty = "iv";
    private const string _effectiveIvProperty = "effective_iv";
    private const string _evProperty = "ev";
    private const string _natureProperty = "nature_id";
    private const string _effectiveNatureProperty = "effective_nature_id";
    private const string _abilityProperty = "persistent_ability";
    private const string _abilityIdProperty = "ability_id";
    private const string _heldProperty = "held_item";
    private const string _itemIdProperty = "item_id";
    private const string _movesProperty = "moves";
    private const string _ownedTypesProperty = "owned_types";
    private const string _shinyTraitsProperty = "shiny_traits";
    private const string _displayMetadataProperty = "display_metadata";
    private const string _friendshipProperty = "friendship";
    private const string _moveIdProperty = "move_id";
    private const string _maxPpProperty = "max_pp";
    private const string _ppUpProperty = "pp_up_count";
    private const string _slotProperty = "slot";
    private const string _roleProperty = "ownership_role";
    private const string _favoriteRevisionProperty = "favorite_set_revision";
    private const string _checkpointResourcesProperty = "checkpoint_resources";
    private const string _firstObservedBaseline = "first_observed_baseline";
    private const string _reconciliation = "reconciliation";
    private const string _acquisition = "acquisition";
    private const string _terminal = "terminal";
    private const string _runnerExit = "runner_exit";
    private const string _runnerEntry = "runner_entry";
    private const string _configurationChange = "configuration_change";
    private const string _runnerRole = "runner";
    private const string _ownedDetailKind = "owned_configuration";
    private const string _ownedCheckpointKind = "owned_checkpoint";
    private const string _favoriteContextKind = "favorite_revision";
    private const string _branchProperty = "branch_id";
    private const string _branchNamespace = "play_branch";
    private const string _speciesNamespace = "species";
    private const string _formNamespace = "form";
    private const string _abilityNamespace = "ability";
    private const string _natureNamespace = "nature";
    private const string _itemNamespace = "item";
    private const string _moveNamespace = "move";
    private const string _hpProperty = "hp";
    private const string _maximumHpProperty = "maximum_hp";
    private const string _attackProperty = "attack";
    private const string _defenseProperty = "defense";
    private const string _specialAttackProperty = "special_attack";
    private const string _specialDefenseProperty = "special_defense";
    private const string _speedProperty = "speed";
    private const string _resultProperty = "result";
    private const string _outcomeProperty = "outcome";
    private const string _wonResult = "won";
    private const string _lostResult = "lost";
    private const string _resetResult = "reset";
    private const string _abandonedResult = "abandoned";
    private const string _interruptedResult = "interrupted";
    private const string _unknownResult = "retired_unknown";
    private const string _lineageContextKind = "owned_transaction";
    private const string _lineageProperty = "lineage";
    private const string _parentEntitiesProperty = "parent_entity_ids";
    private const string _lineageParentContextKind = "owned_lineage_parent";
    private readonly HashSet<Guid> _knownAttempts = [];

    /// <summary>
    /// Creates a technical attempt anchor only when an actual post-release event references it.
    /// </summary>
    /// <param name="attempt">The actual attempt UUID in its 16-byte database representation.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void EnsureAttempt(byte[] attempt, JsonElement occurrence)
    {
        Guid identity = new(attempt);
        if (_knownAttempts.Contains(identity))
            return;

        if (CollectionQueries.FindAttemptPresence(_db, _transaction, attempt) is not null)
        {
            if (_knownAttempts.Count < 32768)
                _knownAttempts.Add(identity);

            return;
        }

        long context = Context(_attemptContextKind, JsonSerializer.SerializeToElement(new { attempt_id = CollectionEncoding.Identifier(attempt), source_context = occurrence.TryGetProperty(_contextProperty, out JsonElement source) ? source : default }));
        CollectionQueries.EnsureAttempt(_db, _transaction, attempt, context, Now(), CollectionEncoding.Revision(_revision));
        if (_knownAttempts.Count < 32768)
            _knownAttempts.Add(identity);
    }

    /// <summary>
    /// Closes one actual attempt without inferring a result from disconnection or missing battle data.
    /// </summary>
    /// <param name="attempt">The actual attempt UUID in its 16-byte database representation.</param>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void CloseAttempt(byte[] attempt, JsonElement occurrence)
    {
        JsonElement payload = occurrence.GetProperty(_payloadProperty);
        string? result = payload.TryGetProperty(_resultProperty, out JsonElement resultValue) ? resultValue.GetString() : payload.TryGetProperty(_outcomeProperty, out resultValue) ? resultValue.GetString() : null;
        string status = result == _abandonedResult ? _resetResult : result is _wonResult or _lostResult or _resetResult or _interruptedResult ? result : _unknownResult;
        long block = Block(_attemptSummaryKind, CollectionEncoding.Canonical(payload));
        CollectionQueries.CloseActualAttempt(_db, _transaction, attempt, Now(), status, block, CollectionEncoding.Revision(_revision));
    }

    /// <summary>
    /// Interns exact immutable non-progress context.
    /// </summary>
    /// <param name="kind">The immutable context category used to distinguish identical JSON in different domains.</param>
    /// <param name="value">The exact non-progress context value to intern.</param>
    /// <returns>The interned immutable context identifier for this category and exact value.</returns>
    private long Context(string kind, JsonElement value)
    {
        byte[] raw = CollectionEncoding.Canonical(value);
        long block = Block(_collectionContext, raw);
        byte[] key = _values.Encode(value);
        return CollectionQueries.InternContext(_db, _transaction, kind, key, SHA256.HashData(key), block);
    }

    /// <summary>
    /// Records an actual retained owned configuration, preserving partial evidence without invented stats.
    /// </summary>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void ApplyOwnedConfiguration(JsonElement occurrence, byte[] stream, ulong sequence)
    {
        JsonElement payload = occurrence.GetProperty(_payloadProperty);
        byte[]? attempt = OptionalIdentifier(occurrence, _attemptProperty);
        if (attempt is null || !payload.TryGetProperty(_ownedEntityProperty, out JsonElement entity))
            throw new InvalidDataException("Owned configuration has no actual attempt/entity identity.");

        string capture = RequiredString(payload, _captureKindProperty);
        if (capture is _firstObservedBaseline or _reconciliation)
            return;

        bool retained = payload.TryGetProperty(_teamCommitProperty, out JsonElement commit) && commit.TryGetProperty(_retainedProperty, out JsonElement retainedValue) && retainedValue.GetBoolean();
        byte[] entityKey = _values.Encode(entity);
        long? existing = CollectionQueries.FindOwned(_db, _transaction, attempt, entityKey)?.Id;
        if (!retained && existing is not long)
            return;

        long individual = existing ?? CollectionQueries.InsertOwned(_db, _transaction, attempt, entityKey, CollectionEncoding.Revision(_revision));
        long? generated = VectorProperty(payload, _generatedProperty);
        long? computed = VectorProperty(payload, _computedProperty);
        long? moveset = Moveset(payload);
        if (generated is null || computed is null || moveset is null || !payload.TryGetProperty(_speciesIdentityProperty, out JsonElement species) || !species.TryGetProperty(_speciesProperty, out JsonElement speciesId) || !payload.TryGetProperty(_levelProperty, out JsonElement level) || !payload.TryGetProperty(_experienceProperty, out JsonElement experience))
        {
            long partialDetail = Block(_ownedDetailKind, CollectionEncoding.Canonical(payload));
            CollectionQueries.SavePartialConfiguration(_db, _transaction, individual, stream, CollectionEncoding.Revision(sequence), partialDetail, CollectionEncoding.Revision(_revision));
            return;
        }

        byte[] fingerprint = SHA256.HashData(CollectionEncoding.Canonical(MeaningfulConfiguration(payload)));
        OwnedStateRecord? previousState = CollectionQueries.ReadOwnedState(_db, _transaction, individual);
        long previousRevision = previousState?.Revision ?? 0;
        long? previousConfiguration = previousState?.Configuration;
        if (previousState is not null && capture is not (_acquisition or _terminal or _runnerEntry or _runnerExit) && previousState.Fingerprint.AsSpan().SequenceEqual(fingerprint))
            return;

        long revision = checked(previousRevision + 1);
        long detail = Block(_ownedDetailKind, CollectionEncoding.Canonical(payload));
        JsonElement training = payload.TryGetProperty(_trainingProperty, out JsonElement traits) ? traits : default;
        long? checkpoint = payload.TryGetProperty(_checkpointResourcesProperty, out JsonElement checkpointValue) ? Block(_ownedCheckpointKind, CollectionEncoding.Canonical(checkpointValue)) : null;
        string normalizedCapture = capture is _acquisition or _terminal or _runnerEntry or _runnerExit ? capture : _configurationChange;
        long configuration = CollectionQueries.InsertConfiguration(_db, _transaction, individual, revision, previousRevision == 0 ? null : previousRevision, Identity(_branchNamespace, occurrence.TryGetProperty(_branchProperty, out JsonElement branch) ? branch : JsonSerializer.SerializeToElement(CollectionEncoding.Identifier(stream))), stream, CollectionEncoding.Revision(sequence), CollectionEncoding.Revision(_revision), normalizedCapture, payload.TryGetProperty(_roleProperty, out JsonElement role) ? role.GetString()! : _runnerRole, retained ? 1 : 0, Identity(_speciesNamespace, speciesId), OptionalIdentity(species, _formProperty, _formNamespace), OptionalIdentity(species, _headProperty, _speciesNamespace), OptionalIdentity(species, _bodyProperty, _speciesNamespace), level.GetInt64(), experience.GetInt64(), generated.Value, VectorProperty(payload, _referenceProperty), computed.Value, VectorProperty(training, _rawIvProperty), VectorProperty(training, _effectiveIvProperty), VectorProperty(training, _evProperty), moveset.Value, payload.TryGetProperty(_abilityProperty, out JsonElement ability) ? OptionalIdentity(ability, _abilityIdProperty, _abilityNamespace) : null, OptionalIdentity(training, _natureProperty, _natureNamespace), OptionalIdentity(training, _effectiveNatureProperty, _natureNamespace), payload.TryGetProperty(_heldProperty, out JsonElement held) ? OptionalIdentity(held, _itemIdProperty, _itemNamespace) : null, payload.TryGetProperty(_favoriteRevisionProperty, out JsonElement favorite) && favorite.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? Context(_favoriteContextKind, favorite) : null, detail, checkpoint);
        CollectionQueries.SaveOwnedState(_db, _transaction, individual, fingerprint, revision, configuration);
        long transaction = Context(_lineageContextKind, commit.ValueKind == JsonValueKind.Object ? commit : JsonSerializer.SerializeToElement(new { source_stream = CollectionEncoding.Identifier(stream), source_sequence = CollectionEncoding.Decimal(sequence) }));
        if (previousConfiguration is long previous)
            CollectionQueries.InsertContinuationLineage(_db, _transaction, configuration, previous, transaction);

        PreserveLineageParents(payload, attempt, entityKey, configuration, previousConfiguration, transaction);
    }

    /// <summary>
    /// Indexes actual source identities without inventing a retained configuration for unowned candidates.
    /// </summary>
    /// <param name="payload">The actual source payload to inspect or fold into sufficient state.</param>
    /// <param name="attempt">The actual attempt UUID in its 16-byte database representation.</param>
    /// <param name="entityKey">The canonical source identity of the retained Pokemon.</param>
    /// <param name="configuration">The newly retained owned configuration identifier.</param>
    /// <param name="previousConfiguration">The preceding retained configuration identifier, or null for its first configuration.</param>
    /// <param name="transaction">The interned ownership transaction context joining this configuration to its actual parents.</param>
    private void PreserveLineageParents(JsonElement payload, byte[] attempt, byte[] entityKey, long configuration, long? previousConfiguration, long transaction)
    {
        if (!payload.TryGetProperty(_lineageProperty, out JsonElement lineage) || !lineage.TryGetProperty(_parentEntitiesProperty, out JsonElement parents) || parents.ValueKind != JsonValueKind.Array)
            return;

        int ordinal = 0;
        foreach (JsonElement parent in parents.EnumerateArray())
        {
            if (parent.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            byte[] parentKey = _values.Encode(parent);
            long? source = parentKey.AsSpan().SequenceEqual(entityKey) ? previousConfiguration : CollectionQueries.FindParentConfiguration(_db, _transaction, attempt, parentKey)?.Configuration;
            long? sourceContext = source is null ? Context(_lineageParentContextKind, JsonSerializer.SerializeToElement(new { source_entity_id = parent, lineage })) : null;
            CollectionQueries.InsertParentLineage(_db, _transaction, configuration, ordinal++, source, sourceContext, transaction);
        }
    }

    /// <summary>
    /// Excludes polling, EXP-only and friendship-only changes from meaningful configuration history.
    /// </summary>
    /// <param name="payload">The owned configuration from which transient resources and polling-only differences are excluded.</param>
    /// <returns>An independent configuration value excluding resource-only and polling-only differences.</returns>
    private static JsonElement MeaningfulConfiguration(JsonElement payload)
    {
        string[] fields = [_speciesIdentityProperty, _levelProperty, _generatedProperty, _referenceProperty, _computedProperty, _trainingProperty, _abilityProperty, _ownedTypesProperty, _movesProperty, _heldProperty, _shinyTraitsProperty, _displayMetadataProperty, _roleProperty, _historyContext];
        Dictionary<string, JsonElement> meaningful = [];
        foreach (string key in fields)
        {
            if (!payload.TryGetProperty(key, out JsonElement value))
                continue;

            meaningful[key] = key == _trainingProperty && value.ValueKind == JsonValueKind.Object ? JsonSerializer.SerializeToElement(value.EnumerateObject().Where(property => property.Name != _friendshipProperty).ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)) : value.Clone();
        }

        return JsonSerializer.SerializeToElement(meaningful);
    }

    /// <summary>
    /// Resolves a nullable canonical native identity while preserving string-versus-number semantics.
    /// </summary>
    /// <param name="parent">The object containing the optional native identity.</param>
    /// <param name="property">The exact JSON property name to read.</param>
    /// <param name="nameSpace">The identity namespace that keeps unrelated native identifier domains distinct.</param>
    /// <returns>The interned native identity, or null when the property is absent or JSON null.</returns>
    private long? OptionalIdentity(JsonElement parent, string property, string nameSpace)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out JsonElement value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? Identity(nameSpace, value) : null;

    /// <summary>
    /// Interns an available complete six-stat vector.
    /// </summary>
    /// <param name="parent">The object containing a possible complete six-stat vector.</param>
    /// <param name="property">The exact JSON property name to read.</param>
    /// <returns>The interned six-stat vector identifier, or null when the vector is absent or incomplete.</returns>
    private long? VectorProperty(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return null;

        string[] fields = [_hpProperty, _attackProperty, _defenseProperty, _specialAttackProperty, _specialDefenseProperty, _speedProperty];
        long[] values = new long[fields.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            if (!value.TryGetProperty(fields[index], out JsonElement component) && !(index == 0 && value.TryGetProperty(_maximumHpProperty, out component)))
                return null;

            if (!component.TryGetInt64(out long number) || number < 0)
                return null;

            values[index] = number;
        }

        return CollectionQueries.InternStatVector(_db, _transaction, values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    /// <summary>
    /// Interns a simultaneous ordered moveset, retaining learning metadata in its configuration detail.
    /// </summary>
    /// <param name="payload">The owned configuration containing the simultaneous ordered move slots and PP capacity.</param>
    /// <returns>The interned simultaneous moveset identifier, or null when required slot evidence is unavailable.</returns>
    private long? Moveset(JsonElement payload)
    {
        if (!payload.TryGetProperty(_movesProperty, out JsonElement moves) || moves.ValueKind != JsonValueKind.Array || moves.GetArrayLength() > 4)
            return null;

        List<(int Slot, long Move, long Pp, long? Ups)> slots = [];
        foreach (JsonElement move in moves.EnumerateArray())
        {
            if (!move.TryGetProperty(_slotProperty, out JsonElement slot) || !move.TryGetProperty(_moveIdProperty, out JsonElement moveId) || !move.TryGetProperty(_maxPpProperty, out JsonElement pp) || !pp.TryGetInt64(out long maximum))
                return null;

            slots.Add((slot.GetInt32(), Identity(_moveNamespace, moveId), maximum, move.TryGetProperty(_ppUpProperty, out JsonElement ups) && ups.ValueKind == JsonValueKind.Number ? ups.GetInt64() : null));
        }

        if (slots.Select(slot => slot.Slot).Distinct().Count() != slots.Count || slots.Any(slot => slot.Slot is < 0 or > 3 || slot.Pp < 0))
            throw new InvalidDataException("Owned moveset slots are not a valid ordered configuration.");

        slots.Sort((left, right) => left.Slot.CompareTo(right.Slot));
        byte[] digest = SHA256.HashData(CollectionEncoding.Canonical(slots.Select(slot => new { slot = slot.Slot, move = slot.Move, max_pp = slot.Pp, pp_ups = slot.Ups })));
        MovesetIdentityRecord? found = CollectionQueries.FindMoveset(_db, _transaction, digest, slots.Count);
        if (found is not null)
            return found.Id;

        long id = CollectionQueries.InsertMoveset(_db, _transaction, slots.Count, digest);
        foreach ((int slot, long move, long pp, long? ups) in slots)
            CollectionQueries.InsertMovesetSlot(_db, _transaction, id, slot, move, pp, ups);

        return id;
    }
}
