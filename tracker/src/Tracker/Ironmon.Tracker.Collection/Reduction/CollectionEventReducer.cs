using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Collection.Reduction;

/// <summary>
/// Folds authoritative facts into exact generic measurements and bounded causal summaries.
/// </summary>
/// <remarks>
/// Unavailable required inputs retain unknown coverage. This reducer never invents a successful effect from a requested operation.
/// </remarks>
public sealed partial class CollectionEventReducer
{
    private const string _known = "known";
    private const string _unknown = "unknown";
    private const string _scalarKind = "scalar";
    private const string _setKind = "exact_set";
    private const string _orderedKind = "ordered_payload";
    private const string _sumOperator = "sum";
    private const string _minOperator = "min";
    private const string _maxOperator = "max";
    private const string _replaceOperator = "ordered_replace";
    private const string _ratioKind = "rational_extremum";
    private const string _pathsKey = "paths";
    private const string _tupleKey = "tuple";
    private const string _keyKey = "key";
    private const string _kindKey = "kind";
    private const string _coverageKey = "coverage";
    private const string _mergeKey = "merge_operator";
    private const string _selectorKey = "selector";
    private const string _dimensionsKey = "dimensions";
    private const string _measuresKey = "measures";
    private const string _guardsKey = "guards";
    private const string _operatorKey = "operator";
    private const string _valueKey = "value";
    private const string _typeKey = "type";
    private const string _payloadKey = "payload";
    private const string _contextKey = "context";
    private const string _attemptKey = "attempt_id";
    private const string _battleKey = "battle_id";
    private const string _actionKey = "action_id";
    private const string _groupKey = "effect_group_id";
    private const string _sourceFamiliesKey = "source_families";
    private const string _idKey = "id";
    private const string _fanoutKey = "fanout";
    private const string _valueDimensionKey = "value_dimension";
    private const string _valueDimensionNameKey = "value_dimension_key";
    private const string _battlesState = "battles";
    private const string _actionsState = "actions";
    private const string _groupsState = "groups";
    private const string _attemptsState = "attempts";
    private const string _preferencesState = "preferences";
    private const string _familyState = "family_state";
    private const string _eventBattleStarted = "battle.started";
    private const string _eventBattleEnded = "battle.ended";
    private const string _eventBattleSummary = "battle.summary_finalized";
    private const string _eventAttemptStarted = "attempt.started";
    private const string _eventAttemptCompleted = "attempt.completed";
    private const string _eventMoveAttempted = "move.attempted";
    private const string _eventMoveExecuted = "move.executed";
    private const string _eventMoveFinished = "move.finished";
    private const string _eventActionStarted = "action.started";
    private const string _eventActionFinished = "action.finished";
    private const string _eventItemResolved = "item.use_resolved";
    private const string _eventHealing = "hp.healing_resolved";
    private const string _eventPpRestored = "pp.restoration_resolved";
    private const string _eventStatusCured = "status.major_cure_resolved";
    private const string _eventVolatileRemoved = "volatile.removal_resolved";
    private const string _eventGroupOpened = "collection.effect_group_opened";
    private const string _eventGroupClosed = "collection.effect_group_closed";
    private const string _eventCheckpoint = "collection.context_checkpoint";
    private const string _eventFavoriteChanged = "pokemon.favorite_selection_changed";
    private const string _eventOwnedCommit = "pokemon.owned_configuration_committed";
    private const string _eventBadge = "world.badge_state_changed";
    private const string _eventWardrobeOwnership = "wardrobe.ownership_changed";
    private const string _eventHitCalculated = "hit.damage_calculated";
    private const string _eventHitResolved = "hit.resolved";
    private const string _eventHpLoss = "hp.loss_applied";
    private const string _eventSubstituteChanged = "substitute.changed";
    private const string _eventStageChanged = "stat_stage.changed";
    private const string _familyActivationPrefix = "item_activation_";
    private const string _battlePrefix = "$battle.";
    private const string _actionPrefix = "$action.";
    private const string _hitPrefix = "$hit.";
    private const string _rootPrefix = "@";
    private const string _oneExpression = "$one";
    private const string _zeroExpression = "$zero";
    private const string _countPrefix = "$count:";
    private const string _positivePrefix = "$positive:";
    private const string _negativePrefix = "$negative:";
    private const string _nativeKind = "native_id";
    private const string _integerKind = "integer";
    private const string _booleanKind = "boolean";
    private const string _textKind = "text";
    private const string _setDimensionKind = "set";
    private const string _rationalDimensionKind = "rational";
    private const string _namespaceKey = "namespace";
    private const string _nativeValueKey = "id";
    private const string _numeratorKey = "numerator";
    private const string _denominatorKey = "denominator";
    private const string _nullJson = "null";
    private const string _sourcesKey = "sources";
    private const string _rowKey = "row";
    private const string _contextsKey = "contexts";
    private const string _recipientsKey = "recipients";
    private const string _itemKey = "item";
    private const string _parentPayloadKey = "parent_payload";
    private const string _hitState = "hit";
    private const string _countKey = "count";
    private const string _bestKey = "best";
    private const string _currentKey = "current";
    private const string _postEpochBadgesKey = "post_epoch_badges";
    private const string _ownedPiecesKey = "owned_pieces";
    private const string _normalSpeciesKey = "normal_species_ids";
    private const string _resultKey = "result";
    private const string _outcomeKey = "outcome";
    private const string _ownerRoleKey = "owner_role";
    private const string _invocationKey = "invocation_context";
    private const string _invocationKindKey = "invocation_kind";
    private const string _moveCallKind = "move_call";
    private const string _executedEffect = "executed_effect";
    private const string _delegatedResult = "delegated";
    private const string _delegatedSuccessKey = "delegated_success";
    private const string _playerRole = "player";
    private const string _opponentRole = "opponent";
    private const string _trainerKind = "trainer";
    private const string _winOutcome = "win";
    private const string _bagContext = "bag";
    private const string _itemSourceKind = "item";
    private const string _acquisitionKind = "acquisition";
    private const string _evolutionKind = "evolution";
    private const string _baselineCapture = "first_observed_baseline";
    private const string _reconciliationCapture = "reconciliation";
    private const string _retainedPopulation = "retained_team_progression_bst_contributions";
    private const string _observationPopulation = "meaningful_configuration_observations";
    private const string _occurrenceKey = "occurrence_count";
    private const string _membersKey = "members";
    private const string _actualTotalKey = "actual_total";
    private const string _maximumValueKey = "maximum_value";
    private const string _policyExpression = "$policy_version";
    private const string _successfulUsageExpression = "$successful_credited_usage";
    private const string _landedExpression = "$landed_damaging";
    private const string _favoriteCountExpression = "$favorite_match_count";
    private const string _statValueExpression = "$stat_value";
    private const string _statKeyExpression = "$stat_key";
    private const string _statValueKey = "stat_value";
    private const string _statKeyKey = "stat_key";
    private const string _statFanoutKey = "stat_fanout";
    private const string _clockState = "clock_remainders";
    private readonly CollectionCatalogue _catalogue;
    private JsonObject _state = CreateState();
    private string? _cachedIdentity;
    private CollectionReduction? _cachedReduction;
    private string? _completedAttemptIdentity;
    private JsonElement? _completedAttemptState;

    /// <summary>
    /// Creates a reducer for the versioned generic collection catalogue.
    /// </summary>
    /// <param name="catalogue">Generic family and summary contracts.</param>
    public CollectionEventReducer(CollectionCatalogue catalogue) => _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));

    /// <summary>
    /// Reduces a single ordered, receipt-qualified event exactly once.
    /// </summary>
    /// <param name="envelope">One member of a transport batch's events array.</param>
    /// <returns>Atomic persistent family and sufficient-summary effects.</returns>
    public CollectionReduction ReduceAll(JsonElement envelope)
    {
        string identity = envelope.GetRawText();
        if (_cachedIdentity == identity && _cachedReduction is not null)
            return _cachedReduction;

        string type = Text(envelope, _typeKey) ?? throw new InvalidDataException("A collection event needs a type.");
        JsonElement payload = Property(envelope, _payloadKey);
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A collection event needs an object payload.");

        ObserveBefore(type, envelope, payload);
        List<CollectionProjection> families = [];
        List<CollectionDirectProjection> direct = [];
        if (type == _eventGroupClosed)
        {
            string? groupId = Text(payload, _groupKey);
            if (groupId is not null && StateMap(_groupsState)[groupId] is JsonObject group && group[_itemKey] is JsonObject item)
            {
                JsonObject completed = (JsonObject)item.DeepClone();
                JsonObject completedPayload = (JsonObject)completed[_payloadKey]!;
                FinalizeItemRecipients(group, completedPayload);
                completedPayload[_recipientsKey] = group[_recipientsKey]?.DeepClone() ?? new JsonObject();
                FinalizeItemTransaction(completedPayload);
                Project(_eventItemResolved, Element(completed), Element(completedPayload), families, direct, true);
            }

            if (groupId is not null)
                StateMap(_groupsState).Remove(groupId);
        }
        else if (type != _eventGroupOpened && type != _eventCheckpoint)
        {
            bool deferActivation = type == _eventItemResolved && GroupId(envelope, payload) is not null;
            Project(type, envelope, payload, families, direct, false, deferActivation);
        }

        if (type == _eventBattleEnded)
            ProjectScopePeaks(type, envelope, payload, direct);

        if (type is _eventHpLoss or _eventSubstituteChanged)
            ProjectScopePeaks(type, envelope, payload, direct);

        if (type == _eventBattleEnded)
            FinalizeBattle(envelope, families, direct);

        if (type == _eventGroupClosed)
        {
            foreach (JsonObject pending in StateMap(_battlesState).Select(pair => pair.Value).OfType<JsonObject>().Where(battle => battle[CollectionTokens.PendingFinalization] is JsonObject).ToArray())
                FinalizeBattle(Element(pending[CollectionTokens.PendingFinalization]!), families, direct);
        }

        if (type == _eventCheckpoint && Text(payload, CollectionTokens.RetirementKind) is CollectionTokens.Closed or CollectionTokens.RecoveredInterrupted)
        {
            foreach ((string battleId, JsonNode? _) in StateMap(_battlesState))
            {
                JsonObject interrupted = JsonNode.Parse(envelope.GetRawText())!.AsObject();
                interrupted[_battleKey] = battleId;
                interrupted[_payloadKey] = new JsonObject { [CollectionTokens.Reason] = Text(payload, CollectionTokens.RetirementKind), [CollectionTokens.CompletionState] = CollectionTokens.Interrupted };
                Project(CollectionTokens.BattleInterrupted, Element(interrupted), Element(interrupted[_payloadKey]!), families, direct, false);
            }

            StateMap(_battlesState).Clear();
            StateMap(_actionsState).Clear();
            StateMap(_groupsState).Clear();
            StateMap(_familyState).Clear();
        }

        ObserveAfter(type, envelope, payload);
        _cachedIdentity = identity;
        _cachedReduction = new CollectionReduction(families, direct);
        return _cachedReduction;
    }

    /// <summary>
    /// Returns family effects, sharing the same reduction with direct summaries.
    /// </summary>
    /// <param name="envelope">One authoritative event.</param>
    /// <returns>Generic family effects.</returns>
    public IReadOnlyList<CollectionProjection> Reduce(JsonElement envelope)
        => ReduceAll(envelope).Families;

    /// <summary>
    /// Returns directly indexed effects from the same reduction.
    /// </summary>
    /// <param name="envelope">One authoritative event.</param>
    /// <returns>Typed sufficient-summary effects.</returns>
    public IReadOnlyList<CollectionDirectProjection> ReduceDirect(JsonElement envelope)
        => ReduceAll(envelope).Direct;

    /// <summary>
    /// Exports bounded working state for the same atomic transaction as the receipt.
    /// </summary>
    /// <returns>Current active joins, current sets, and ordered reducer state.</returns>
    public JsonElement ExportState()
        => Element(_state);

    /// <summary>
    /// Tests whether an actual attempt already has hydrated working state.
    /// </summary>
    /// <param name="attempt">The authoritative attempt identifier.</param>
    /// <returns>Whether this attempt is currently represented in bounded working state.</returns>
    public bool HasAttemptState(string attempt)
        => StateMap(_attemptsState)[attempt] is JsonObject;

    /// <summary>
    /// Restores one continued attempt's sufficient counters without replaying old events.
    /// </summary>
    /// <param name="attempt">The authoritative attempt identifier.</param>
    /// <param name="state">Previously archived sufficient state for exactly this attempt.</param>
    public void RestoreAttemptState(string attempt, JsonElement state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attempt);
        if (state.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Archived attempt reducer state must be an object.");

        if (!HasAttemptState(attempt))
            StateMap(_attemptsState)[attempt] = JsonNode.Parse(state.GetRawText());
    }

    /// <summary>
    /// Takes the most recently completed attempt's compact state for atomic archival.
    /// </summary>
    /// <param name="attempt">The actual completed attempt identifier.</param>
    /// <returns>Its sufficient state, or null when no matching completion was observed.</returns>
    public JsonElement? TakeCompletedAttemptState(string attempt)
    {
        if (_completedAttemptIdentity != attempt)
            return null;

        JsonElement? state = _completedAttemptState;
        _completedAttemptIdentity = null;
        _completedAttemptState = null;
        return state;
    }

    /// <summary>
    /// Restores working state after reopening or rolling back a persistence transaction.
    /// </summary>
    /// <param name="state">Previously durable reducer state.</param>
    public void ImportState(JsonElement state)
    {
        _state = state.ValueKind == JsonValueKind.Object ? JsonNode.Parse(state.GetRawText())!.AsObject() : CreateState();
        foreach (string key in new[] { _battlesState, _actionsState, _groupsState, _attemptsState, _preferencesState, _familyState, _clockState })
            _state[key] ??= new JsonObject();

        foreach (JsonObject battle in StateMap(_battlesState).Select(pair => pair.Value).OfType<JsonObject>())
        {
            if (battle[CollectionBattleKeys.SourceEvidence]?[CollectionBattleKeys.Actors] is not JsonObject actors)
                continue;

            foreach (JsonObject actor in actors.Select(pair => pair.Value).OfType<JsonObject>())
            {
                if (actor[CollectionBattleKeys.MinHpRatio] is null)
                    actor[CollectionBattleKeys.MinHpRatioIncomplete] = true;
            }
        }

        _cachedIdentity = null;
        _cachedReduction = null;
        _completedAttemptIdentity = null;
        _completedAttemptState = null;
    }

    /// <summary>
    /// Projects all declared families and their directly indexed counterparts.
    /// </summary>
    /// <param name="type">Source type.</param>
    /// <param name="envelope">Ordered factual envelope.</param>
    /// <param name="payload">Actual source payload.</param>
    /// <param name="families">Family output.</param>
    /// <param name="direct">Direct output.</param>
    /// <param name="activationOnly">Whether this is a deferred activation closure.</param>
    /// <param name="deferActivation">Whether activation families wait for their effect boundary.</param>
    private void Project(string type, JsonElement envelope, JsonElement payload, List<CollectionProjection> families, List<CollectionDirectProjection> direct, bool activationOnly, bool deferActivation = false)
    {
        foreach (CollectionFamilyDefinition family in _catalogue.ForEvent(type))
        {
            bool activation = family.Key.StartsWith(_familyActivationPrefix, StringComparison.Ordinal);
            if (activationOnly != activation && activationOnly || deferActivation && activation)
                continue;

            foreach (JsonElement row in Rows(family, payload))
            {
                Frame frame = CreateFrame(envelope, payload, row);
                bool? eligible = Guards(family.Definition, frame);
                if (eligible == false)
                    continue;

                List<CollectionDimensionValue> dimensions = [.. family.Definition.GetProperty(_dimensionsKey).EnumerateArray().Select(dimension => Dimension(dimension.GetProperty(_keyKey).GetString()!, Select(dimension.GetProperty(_selectorKey), frame)))];
                List<CollectionMeasureUpdate> measures = [];
                foreach (JsonElement measure in family.Definition.GetProperty(_measuresKey).EnumerateArray())
                {
                    JsonElement value = Select(measure.GetProperty(_selectorKey), frame);
                    if (measure.TryGetProperty(_valueDimensionKey, out JsonElement valueSelector))
                    {
                        JsonElement observed = Select(valueSelector, frame);
                        string name = measure.GetProperty(_valueDimensionNameKey).GetString()!;
                        dimensions.Add(Dimension(name, observed));
                        if (!Available(observed))
                            value = Null();
                    }

                    measures.Add(Measure(measure, eligible == true ? value : Null()));
                }

                if (family.StorageKind == CollectionTokens.Transient)
                {
                    StateMap(_familyState)[family.Key] = JsonSerializer.SerializeToNode(new { dimensions, measures });
                }
                else
                {
                    families.Add(new CollectionProjection(family.Key, dimensions, measures));
                }

                if (family.StorageKind != CollectionTokens.Transient)
                    ProjectDirect(family, dimensions, measures, frame, eligible, direct);
            }
        }
    }

    /// <summary>
    /// Projects sufficient summaries only from a routed real source family occurrence.
    /// </summary>
    /// <param name="family">The routed source family.</param>
    /// <param name="dimensions">Actual family dimensions.</param>
    /// <param name="measures">Actual family effects.</param>
    /// <param name="sourceFrame">Source and bounded context before any direct-view expansion.</param>
    /// <param name="eligible">Known eligibility.</param>
    /// <param name="output">Direct output.</param>
    /// <param name="onlyGroup">Optional specific joint receiving expanded source rows.</param>
    private void ProjectDirect(CollectionFamilyDefinition family, IReadOnlyList<CollectionDimensionValue> dimensions, IReadOnlyList<CollectionMeasureUpdate> measures, Frame sourceFrame, bool? eligible, List<CollectionDirectProjection> output, string? onlyGroup = null)
    {
        foreach (JsonElement group in _catalogue.DirectGroups)
        {
            string? groupIdentity = group.GetProperty(_idKey).GetString();
            if (onlyGroup is not null && groupIdentity != onlyGroup || onlyGroup is null && groupIdentity == CollectionBattleKeys.RecoveryDirectGroup && Bool(Property(sourceFrame.Payload, CollectionBattleKeys.SourceEvidenceFolded)) == true)
                continue;

            if (group.GetProperty(_idKey).GetString() is CollectionTokens.Value4d366754f7e3 or CollectionTokens.Value24a5d2d32187 or CollectionTokens.Value77d1e92c15e9)
                continue;

            string[] sources = [.. group.GetProperty(_sourceFamiliesKey).EnumerateArray().Select(value => value.GetString()!)];
            if (sources[0] != family.Key)
                continue;

            bool transaction = IsItemTransactionGroup(groupIdentity);
            if (transaction && output.Any(row => row.GroupId == groupIdentity))
                continue;

            foreach (Frame frame in DirectFrames(group, sourceFrame))
            {
                List<CollectionDimensionValue> directDimensions = [];
                foreach (JsonElement descriptor in group.GetProperty(_dimensionsKey).EnumerateArray())
                {
                    string key = descriptor.GetProperty(_keyKey).GetString()!;
                    CollectionDimensionValue? existing = transaction ? null : dimensions.FirstOrDefault(value => value.Key == key);
                    JsonElement value = existing?.Value ?? Select(descriptor.GetProperty(_selectorKey), frame);
                    directDimensions.Add(Dimension(key, value, descriptor.GetProperty(_kindKey).GetString()));
                }

                List<CollectionMeasureUpdate> directMeasures = [];
                foreach (JsonElement descriptor in group.GetProperty(_measuresKey).EnumerateArray())
                {
                    string key = descriptor.GetProperty(_keyKey).GetString()!;
                    string kind = descriptor.GetProperty(_kindKey).GetString()!;
                    string merge = descriptor.GetProperty(_mergeKey).GetString()!;
                    CollectionMeasureUpdate? source = transaction || HasSourceView(group) ? null : measures.FirstOrDefault(value => value.Key == key);
                    source ??= key == _occurrenceKey ? measures.FirstOrDefault(value => value.Key.StartsWith(_occurrenceKey, StringComparison.Ordinal)) : null;
                    source ??= key == _membersKey ? measures.FirstOrDefault(value => value.Kind == _setKind) : null;
                    source ??= key == _actualTotalKey && !HasSourceView(group) ? measures.FirstOrDefault(value => value.Kind == _scalarKind && value.MergeOperator == _sumOperator) : null;
                    source ??= key == _maximumValueKey && !HasSourceView(group) ? measures.FirstOrDefault(value => value.Kind == _scalarKind && value.MergeOperator == _maxOperator) : null;
                    if (source is not null && source.Kind == kind)
                    {
                        directMeasures.Add(source with { Key = key, MergeOperator = merge });
                    }
                    else
                    {
                        JsonElement selected = ResolveDirectMeasure(key, frame);
                        directMeasures.Add(Measure(descriptor, eligible == true ? selected : Null()));
                    }
                }

                output.Add(new CollectionDirectProjection(group.GetProperty(_idKey).GetString()!, directDimensions, directMeasures));
            }
        }
    }

    /// <summary>
    /// Reads an explicitly named direct measure from actual summary state.
    /// </summary>
    /// <param name="key">Declared measure.</param>
    /// <param name="frame">Actual source frame.</param>
    /// <returns>Known value or explicit null.</returns>
    private JsonElement ResolveDirectMeasure(string key, Frame frame)
    {
        string path = key switch
        {
            CollectionItemTransactionKeys.ActualCreditTotal => CollectionTokens.ActualCreditExpression,
            CollectionItemTransactionKeys.ActualDebitTotal => CollectionTokens.ActualDebitExpression,
            CollectionTokens.CurrentCardinality or CollectionTokens.MaximumSimultaneousCardinality => CollectionTokens.WardrobeEligibleCountExpression,
            CollectionTokens.HitCount => _oneExpression,
            CollectionTokens.MaxActualPoolHpLost => CollectionTokens.ActualPoolHpLost,
            CollectionTokens.MinimumEntryFraction => CollectionTokens.EntryHpEntryMaxHp,
            CollectionTokens.CompletedPositiveActivationCount => CollectionTokens.CompletedActivationCountWherePositiveGainExpression,
            CollectionTokens.ActualPositiveStageTotal => CollectionTokens.PositiveStagesGained,
            CollectionTokens.RaisedComponentCount => CollectionTokens.RaisedStatComponents,
            CollectionTokens.CurrentConsecutiveCount => CollectionTokens.CurrentConsecutiveTrainerWinsExpression,
            CollectionTokens.BestSameAttemptCount => CollectionTokens.BestConsecutiveTrainerWinsExpression,
            CollectionTokens.LifetimeBestCount => CollectionTokens.BestConsecutiveTrainerWinsExpression,
            CollectionTokens.ElapsedNanosecondsTotal => CollectionTokens.ElapsedNanosecondsExpression,
            CollectionTokens.SubnanosecondRemainderNumerator => CollectionTokens.SubnanosecondNumeratorExpression,
            CollectionTokens.SubnanosecondRemainderDenominator => CollectionTokens.SubnanosecondDenominatorExpression,
            _ => key
        };

        return Resolve(path, frame);
    }

    /// <summary>
    /// Enumerates declared actual subrecords without inventing absent array members.
    /// </summary>
    /// <param name="family">Routed family.</param>
    /// <param name="payload">Source payload.</param>
    /// <returns>Actual rows or one unavailable row for absent required subrecords.</returns>
    private IEnumerable<JsonElement> Rows(CollectionFamilyDefinition family, JsonElement payload)
    {
        if (family.Key is CollectionTokens.WardrobeOwnedPieceCount or CollectionTokens.WardrobeNewOwnershipKeys)
        {
            foreach (JsonElement row in WardrobeRows(family.Key, payload))
                yield return row;

            yield break;
        }

        if (family.Definition.TryGetProperty(_statFanoutKey, out JsonElement statFanout))
        {
            JsonElement vector = Path(payload, statFanout.GetString()!);
            if (vector.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty stat in vector.EnumerateObject())
                {
                    JsonObject row = new() { [_statKeyKey] = stat.Name, [_statValueKey] = JsonNode.Parse(stat.Value.GetRawText()) };
                    yield return Element(row);
                }

                yield break;
            }
        }

        if (family.Key.StartsWith(_familyActivationPrefix, StringComparison.Ordinal) && payload.TryGetProperty(_recipientsKey, out JsonElement recipients))
        {
            foreach (JsonProperty recipient in recipients.EnumerateObject())
                yield return recipient.Value;

            if (!recipients.EnumerateObject().Any())
                yield return Null();

            yield break;
        }

        JsonElement fanout = family.Definition.GetProperty(_fanoutKey);
        if (fanout.ValueKind == JsonValueKind.String)
        {
            string path = fanout.GetString()!;
            JsonElement values = path is CollectionTokens.FavoriteMatchesExpression or CollectionTokens.FavoriteSlotsExpression ? FavoriteMatches(payload, path == CollectionTokens.FavoriteSlotsExpression) : Path(payload, path);
            if (values.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement value in values.EnumerateArray())
                    yield return value;

                yield break;
            }

            if (values.ValueKind == JsonValueKind.Object)
            {
                yield return values;
                yield break;
            }

            yield return Null();
            yield break;
        }

        yield return payload;
    }

    /// <summary>
    /// Builds a bounded immutable view of the actual event's related contexts.
    /// </summary>
    /// <param name="envelope">Envelope.</param>
    /// <param name="payload">Source payload.</param>
    /// <param name="row">Actual fanout row.</param>
    /// <returns>Read context for explicit selectors.</returns>
    private Frame CreateFrame(JsonElement envelope, JsonElement payload, JsonElement row)
    {
        string? battleId = Text(envelope, _battleKey);
        string? actionId = Text(Property(envelope, _contextKey), _actionKey);
        JsonElement battle = battleId is not null && StateMap(_battlesState)[battleId] is JsonObject battleNode ? Element(battleNode) : Null();
        JsonElement action = actionId is not null && StateMap(_actionsState)[actionId] is JsonObject actionNode ? Element(actionNode) : Null();
        JsonElement hit = Property(action, _hitState);
        if (Available(Property(payload, CollectionTokens.HitIndex)) && Available(hit) && (!Equal(Property(payload, CollectionTokens.HitIndex), Property(hit, CollectionTokens.HitIndex)) || !Equal(Path(payload, CollectionTokens.TargetAppearanceId), Path(hit, CollectionTokens.TargetAppearanceId))))
            hit = Null();

        return new Frame(envelope, payload, row, battle, action, hit);
    }

    /// <summary>
    /// Applies tri-state explicit eligibility guards.
    /// </summary>
    /// <param name="definition">Family routing.</param>
    /// <param name="frame">Actual inputs.</param>
    /// <returns>True, false, or unknown.</returns>
    private bool? Guards(JsonElement definition, Frame frame)
    {
        bool unknown = false;
        foreach (JsonElement guard in definition.GetProperty(_guardsKey).EnumerateArray())
        {
            JsonElement actual = Select(guard.GetProperty(_selectorKey), frame);
            string op = guard.GetProperty(_operatorKey).GetString()!;
            JsonElement expected = guard.GetProperty(_valueKey);
            if (op == CollectionTokens.Exists)
            {
                if (!Available(actual))
                    unknown = true;

                continue;
            }

            if (op == CollectionTokens.DifferentPath)
                expected = Resolve(expected.GetString()!, frame);

            if (!Available(actual) || !Available(expected))
            {
                unknown = true;
                continue;
            }

            bool? comparison = op switch
            {
                CollectionTokens.Eq => Equal(actual, expected),
                CollectionTokens.Ne or CollectionTokens.DifferentPath => !Equal(actual, expected),
                CollectionTokens.In => expected.EnumerateArray().Any(value => Equal(actual, value)),
                CollectionTokens.NotIn => !expected.EnumerateArray().Any(value => Equal(actual, value)),
                CollectionTokens.Gt => Integer(actual) is BigInteger left && Integer(expected) is BigInteger right ? left > right : null,
                _ => null
            };
            if (comparison == false)
                return false;

            if (comparison is null)
                unknown = true;
        }

        return unknown ? null : true;
    }

    /// <summary>
    /// Reads an explicitly compiled selector or correlated exact tuple.
    /// </summary>
    /// <param name="selector">Compiled selector metadata.</param>
    /// <param name="frame">Actual source frame.</param>
    /// <returns>Selected actual value or null.</returns>
    private JsonElement Select(JsonElement selector, Frame frame)
    {
        if (selector.TryGetProperty(_tupleKey, out JsonElement tuple))
        {
            JsonElement[] values = [.. tuple.EnumerateArray().Select(part => Select(part, frame))];
            return values.All(Available) ? JsonSerializer.SerializeToElement(new[] { values }) : Null();
        }

        foreach (JsonElement candidate in selector.GetProperty(_pathsKey).EnumerateArray())
        {
            JsonElement value = Resolve(candidate.GetString()!, frame);
            if (Available(value) || AvailabilityWrapper(value) is not null)
                return value;
        }

        return Null();
    }

    /// <summary>
    /// Resolves one explicit field expression without searching unrelated payload fields.
    /// </summary>
    /// <param name="path">Compiled field or exact arithmetic expression.</param>
    /// <param name="frame">Source frame.</param>
    /// <returns>Actual selected value or null.</returns>
    private JsonElement Resolve(string path, Frame frame)
    {
        if (path == _oneExpression)
            return JsonSerializer.SerializeToElement(1);

        if (path == _zeroExpression)
            return JsonSerializer.SerializeToElement(0);

        if (path == CollectionTokens.WardrobeEligibleCountExpression)
            return Property(frame.Row, CollectionTokens.EligibleCount);

        if (path.StartsWith(_rootPrefix, StringComparison.Ordinal))
            return Path(frame.Envelope, path[1..]);

        if (path.StartsWith(_battlePrefix, StringComparison.Ordinal))
            return Path(frame.Battle, path[_battlePrefix.Length..]);

        if (path.StartsWith(_actionPrefix, StringComparison.Ordinal))
            return Path(frame.Action, path[_actionPrefix.Length..]);

        if (path.StartsWith(_hitPrefix, StringComparison.Ordinal))
            return Path(frame.Hit, path[_hitPrefix.Length..]);

        if (path.StartsWith(_countPrefix, StringComparison.Ordinal))
        {
            JsonElement array = Resolve(path[_countPrefix.Length..], frame);
            return array.ValueKind == JsonValueKind.Array ? JsonSerializer.SerializeToElement(array.GetArrayLength()) : Null();
        }

        if (path.StartsWith(_positivePrefix, StringComparison.Ordinal) || path.StartsWith(_negativePrefix, StringComparison.Ordinal))
        {
            bool negative = path.StartsWith(_negativePrefix, StringComparison.Ordinal);
            BigInteger? number = Integer(Resolve(path[(negative ? _negativePrefix.Length : _positivePrefix.Length)..], frame));
            return number is BigInteger value ? ExactInteger(BigInteger.Max(negative ? -value : value, BigInteger.Zero)) : Null();
        }

        int slash = path.IndexOf('/');
        if (slash > 0)
        {
            BigInteger? numerator = Integer(Resolve(path[..slash], frame));
            BigInteger? denominator = Integer(Resolve(path[(slash + 1)..], frame));
            if (numerator.HasValue && denominator > BigInteger.Zero)
                return JsonSerializer.SerializeToElement(new Dictionary<string, string> { [_numeratorKey] = numerator.Value.ToString(CultureInfo.InvariantCulture), [_denominatorKey] = denominator.Value.ToString(CultureInfo.InvariantCulture) });

            return Null();
        }

        JsonElement computed = Computed(path, frame);
        if (Available(computed))
            return computed;

        JsonElement row = Path(frame.Row, path);
        return Available(row) ? row : Path(frame.Payload, path);
    }

    /// <summary>
    /// Computes named generic quantities from explicitly identified actual operands.
    /// </summary>
    /// <param name="path">Named expression.</param>
    /// <param name="frame">Actual source frame.</param>
    /// <returns>Exact actual result or null.</returns>
    private JsonElement Computed(string path, Frame frame)
    {
        JsonElement payload = frame.Payload;
        JsonElement invocation = Property(payload, _invocationKey);
        string? attempt = Text(frame.Envelope, _attemptKey);
        JsonObject attemptState = AttemptState(attempt);
        BigInteger? delta = Integer(Property(payload, CollectionTokens.ActualDelta));
        if (path == _statKeyExpression)
            return Property(frame.Row, _statKeyKey);

        if (path == _statValueExpression)
            return Property(frame.Row, _statValueKey);

        if (path == CollectionTokens.BoundedSummaryTupleExpression)
            return frame.Row;

        if (path == CollectionTokens.StatContributionExpression)
            return JsonSerializer.SerializeToElement(IsStatContribution(payload));

        if (path == _successfulUsageExpression)
        {
            string? result = Text(payload, _resultKey);
            bool delegated = Bool(Property(frame.Action, _delegatedSuccessKey)) == true;
            bool successful = result == _executedEffect || result == _delegatedResult && delegated;
            return JsonSerializer.SerializeToElement(successful && Text(invocation, _invocationKindKey) != _moveCallKind);
        }

        if (path == _landedExpression)
        {
            BigInteger? hp = Integer(Property(payload, CollectionTokens.PositiveHpTargets));
            BigInteger? substitute = Integer(Property(payload, CollectionTokens.PositiveSubstituteTargets));
            return hp.HasValue && substitute.HasValue ? JsonSerializer.SerializeToElement(hp > 0 || substitute > 0) : Null();
        }

        if (path is CollectionTokens.CreditedUsageMoveId or CollectionTokens.CreditedUsageTypeId)
            return Bool(Computed(_successfulUsageExpression, frame)) == true ? path == CollectionTokens.CreditedUsageMoveId ? Property(payload, CollectionTokens.MoveId) : Path(payload, CollectionTokens.ResolvedMoveMetadataTypeId) : Null();

        if (path is CollectionTokens.CallerMoveId or CollectionTokens.CalledMoveId or CollectionTokens.RootMoveId or CollectionTokens.CallDepth or CollectionTokens.InvocationKind)
            return Property(invocation, path == CollectionTokens.CallerMoveId ? CollectionTokens.ImmediateCallerMoveId : path == CollectionTokens.CalledMoveId ? CollectionTokens.RequestedMoveId : path);

        if (path == CollectionTokens.ImmediateCallerMoveId)
            return Property(invocation, CollectionTokens.ImmediateCallerMoveId);

        if (path is CollectionTokens.CallerOwnerRole or CollectionTokens.ChildOwnerRole)
            return path == CollectionTokens.CallerOwnerRole ? Path(invocation, CollectionTokens.CallerActorOwnerRole) : Path(payload, CollectionTokens.ActorOwnerRole);

        if (path is CollectionTokens.ExecutedChildMoveId or CollectionTokens.ChildResult or CollectionTokens.ChildAttemptOutcome or CollectionTokens.ChildEffectiveType or CollectionTokens.ChildCategory)
        {
            return path switch
            {
                CollectionTokens.ExecutedChildMoveId => Property(frame.Action, CollectionTokens.MoveId),
                CollectionTokens.ChildResult => Property(payload, _resultKey),
                CollectionTokens.ChildAttemptOutcome => Property(frame.Action, CollectionTokens.AttemptOutcome),
                CollectionTokens.ChildEffectiveType => Path(payload, CollectionTokens.ResolvedMoveMetadataTypeId),
                _ => Path(payload, CollectionTokens.ResolvedMoveMetadataCategory)
            };
        }
        if (path is CollectionTokens.ChildExecuted or CollectionTokens.ChildEffectSuccess or CollectionTokens.CallCount or CollectionTokens.ChildExecutedCount or CollectionTokens.ChildSuccessCount)
        {
            bool executed = Available(Property(frame.Action, CollectionTokens.MoveId));
            bool success = Text(payload, _resultKey) == _executedEffect;
            return path switch
            {
                CollectionTokens.ChildExecuted => JsonSerializer.SerializeToElement(executed),
                CollectionTokens.ChildEffectSuccess => JsonSerializer.SerializeToElement(success),
                CollectionTokens.CallCount => JsonSerializer.SerializeToElement(1),
                CollectionTokens.ChildExecutedCount => JsonSerializer.SerializeToElement(executed ? 1 : 0),
                _ => JsonSerializer.SerializeToElement(success ? 1 : 0)
            };
        }

        if (path is CollectionTokens.GeneratedBstExpression or CollectionTokens.BaseStatsAfterTotal)
        {
            JsonElement stats = Property(payload, path == CollectionTokens.GeneratedBstExpression ? CollectionTokens.GeneratedBaseStats : CollectionTokens.BaseStatsAfter);
            if (stats.ValueKind != JsonValueKind.Object)
                return Null();
            BigInteger[] values = [.. stats.EnumerateObject().Select(value => Integer(value.Value)).OfType<BigInteger>()];
            return values.Length == 6 ? ExactInteger(values.Aggregate(BigInteger.Zero, (sum, value) => sum + value)) : Null();
        }

        if (path == CollectionTokens.GeneratedBaseStatsEachStat)
            return Property(payload, CollectionTokens.GeneratedBaseStats);

        if (path == CollectionTokens.ComputedStatsEachStat)
            return Property(payload, CollectionTokens.ComputedStats);

        if (path == CollectionTokens.SamplePopulationExpression)
            return JsonSerializer.SerializeToElement(IsStatContribution(payload) ? _retainedPopulation : _observationPopulation);

        if (path == CollectionTokens.ContributionKindExpression)
        {
            JsonElement reasons = Property(payload, CollectionTokens.ChangeReasons);
            string[] observed = reasons.ValueKind == JsonValueKind.Array ? [.. reasons.EnumerateArray().Select(value => value.GetString()!).Where(value => value is not null)] : [];
            return JsonSerializer.SerializeToElement(new[] { _evolutionKind, CollectionTokens.Fusion, CollectionTokens.Reverse, CollectionTokens.Unfusion }.FirstOrDefault(observed.Contains) ?? _acquisitionKind);
        }

        if (path == CollectionTokens.ActualCreditExpression)
            return delta.HasValue ? ExactInteger(BigInteger.Max(delta.Value, BigInteger.Zero)) : Null();

        if (path == CollectionTokens.ActualDebitExpression)
            return delta.HasValue ? ExactInteger(BigInteger.Max(-delta.Value, BigInteger.Zero)) : Null();

        if (path is CollectionTokens.AtCapExpression or CollectionTokens.AtFloorExpression or CollectionTokens.CappedTransactionCountExpression)
        {
            BigInteger? requested = Integer(Property(payload, CollectionTokens.RequestedDelta));
            if (!requested.HasValue || !delta.HasValue)
                return Null();

            BigInteger unapplied = requested.Value - delta.Value;
            return ExactInteger(path == CollectionTokens.AtCapExpression ? BigInteger.Max(unapplied, BigInteger.Zero) : path == CollectionTokens.AtFloorExpression ? BigInteger.Max(-unapplied, BigInteger.Zero) : unapplied != 0 ? BigInteger.One : BigInteger.Zero);
        }

        if (path is CollectionTokens.ActualCapDeltaExpression or CollectionTokens.ActualPpupStageDeltaExpression or CollectionTokens.SuccessfulCapIncreaseUsesExpression)
        {
            BigInteger? before = Integer(Property(payload, path == CollectionTokens.ActualPpupStageDeltaExpression ? CollectionTokens.PpUpsBefore : CollectionTokens.MaxPpBefore));
            BigInteger? after = Integer(Property(payload, path == CollectionTokens.ActualPpupStageDeltaExpression ? CollectionTokens.PpUpsAfter : CollectionTokens.MaxPpAfter));
            return before.HasValue && after.HasValue ? ExactInteger(path == CollectionTokens.SuccessfulCapIncreaseUsesExpression ? after > before ? BigInteger.One : BigInteger.Zero : after.Value - before.Value) : Null();
        }

        if (path is CollectionTokens.PositiveSlotGainCountExpression or CollectionTokens.PositiveDebitCountExpression)
        {
            BigInteger? value = Integer(path == CollectionTokens.PositiveDebitCountExpression ? Property(payload, CollectionTokens.ActualPpLost) : Property(frame.Row, CollectionTokens.ActualPp));
            return value.HasValue ? JsonSerializer.SerializeToElement(value > 0 ? 1 : 0) : Null();
        }

        if (path is CollectionTokens.PositiveComponentChangesExpression or CollectionTokens.NegativeComponentChangesExpression)
        {
            BigInteger? stageDelta = Integer(Property(payload, CollectionTokens.Delta));
            return stageDelta.HasValue ? ExactInteger(path == CollectionTokens.PositiveComponentChangesExpression ? stageDelta > 0 ? BigInteger.One : BigInteger.Zero : stageDelta < 0 ? BigInteger.One : BigInteger.Zero) : Null();
        }

        if (path is CollectionTokens.KnownCalculationCountExpression or CollectionTokens.UnknownCalculationCountExpression)
        {
            bool known = Integer(Property(frame.Hit, CollectionTokens.CalculatedDamage)).HasValue;
            return JsonSerializer.SerializeToElement(path == CollectionTokens.KnownCalculationCountExpression ? known ? 1 : 0 : known ? 0 : 1);
        }

        if (path is CollectionTokens.HpCapExcessExpression or CollectionTokens.NonCapMitigationExpression)
        {
            BigInteger? calculated = Integer(Property(frame.Hit, CollectionTokens.CalculatedDamage));
            BigInteger? poolBefore = Integer(Path(payload, CollectionTokens.ApplicationSnapshotPoolHpBefore));
            BigInteger? actual = Integer(Path(payload, CollectionTokens.ApplicationSnapshotActualPoolHpLost));
            if (!calculated.HasValue || !poolBefore.HasValue || !actual.HasValue)
                return Null();

            return ExactInteger(path == CollectionTokens.HpCapExcessExpression ? BigInteger.Max(calculated.Value - poolBefore.Value, BigInteger.Zero) : BigInteger.Max(BigInteger.Min(calculated.Value, poolBefore.Value) - actual.Value, BigInteger.Zero));
        }

        if (path is CollectionTokens.CompletedActivationCountWherePositiveGainExpression or CollectionTokens.PreventedComponentRequestsExpression or CollectionTokens.PreventedGroupCountExpression)
        {
            if (path == CollectionTokens.CompletedActivationCountWherePositiveGainExpression)
            {
                BigInteger? stages = Integer(Path(payload, CollectionTokens.EffectEvidencePositiveStagesGained));
                return stages.HasValue ? ExactInteger(stages > 0 ? BigInteger.One : BigInteger.Zero) : Null();
            }

            JsonElement outcome = Property(payload, CollectionTokens.Outcome);
            if (outcome.ValueKind != JsonValueKind.String)
                return Null();

            bool prevented = outcome.GetString() is CollectionTokens.Prevented or CollectionTokens.Blocked or CollectionTokens.AbilityPrevented;
            if (path == CollectionTokens.PreventedComponentRequestsExpression)
                return JsonSerializer.SerializeToElement(prevented ? 1 : 0);

            return Null();
        }

        if (path is CollectionTokens.CurrentConsecutiveTrainerWinsExpression or CollectionTokens.BestConsecutiveTrainerWinsExpression)
            return Element(attemptState[path == CollectionTokens.CurrentConsecutiveTrainerWinsExpression ? _currentKey : _bestKey] ?? JsonValue.Create(0)!);

        if (path == CollectionTokens.PostEpochBadgeCountExpression)
            return Element(attemptState[_postEpochBadgesKey] ?? JsonValue.Create(0)!);

        if (path == _policyExpression)
            return JsonSerializer.SerializeToElement(1);

        if (path == CollectionTokens.PositiveCreditExpression)
            return delta.HasValue ? JsonSerializer.SerializeToElement(delta > 0) : Null();

        if (path == CollectionTokens.ConsumedFlag)
        {
            BigInteger? consumed = Integer(Property(payload, CollectionTokens.ConsumedQuantity));
            return consumed.HasValue ? JsonSerializer.SerializeToElement(consumed > 0) : Null();
        }

        if (path == CollectionTokens.OrdinaryBattleExpression)
            return frame.Battle.ValueKind == JsonValueKind.Object ? JsonSerializer.SerializeToElement(Bool(Path(frame.Battle, CollectionTokens.EligibilityTutorial)) != true && Bool(Path(frame.Battle, CollectionTokens.EligibilityDebug)) != true && Bool(Path(frame.Battle, CollectionBattleKeys.EligibilityRecordedReplay)) != true && Bool(Path(frame.Battle, CollectionBattleKeys.EligibilityAttemptActive)) != false) : Null();

        if (path == CollectionTokens.ActualBattleDisplayExpression)
            return JsonSerializer.SerializeToElement(Text(payload, CollectionTokens.ObservationKind) is CollectionTokens.BattleDisplay or CollectionTokens.Battle or CollectionTokens.OpponentBattle);

        if (path == CollectionTokens.HadPreviousRunnerExpression)
            return JsonSerializer.SerializeToElement(Available(Property(payload, CollectionTokens.PreviousRunnerRef)));

        if (path == CollectionTokens.TargetAliveExpression)
        {
            BigInteger? hp = Integer(Property(frame.Row, CollectionTokens.TargetHp));
            return hp.HasValue ? JsonSerializer.SerializeToElement(hp > 0) : Null();
        }

        if (path == CollectionTokens.PerTargetAffectedNotMissedCountExpression || path == CollectionTokens.PerTargetAffectedNotMissedCount)
        {
            JsonElement indexes = Property(frame.Row, CollectionTokens.AffectedNotMissedIterationIndexes);
            return indexes.ValueKind == JsonValueKind.Array ? JsonSerializer.SerializeToElement(indexes.GetArrayLength()) : Null();
        }

        if (path == CollectionTokens.PerTargetOutcomeCounts)
            return Property(frame.Row, CollectionTokens.OutcomeCounts);

        if (path == CollectionTokens.PerTargetPositiveRealHpHitCount)
            return Property(frame.Row, CollectionTokens.PositiveRealHpHitCount);

        if (path == CollectionTokens.PerTargetSubstituteDamageHitCount)
            return Property(frame.Row, CollectionTokens.SubstituteDamageHitCount);

        if (path is CollectionTokens.ElapsedNanosecondsExpression or CollectionTokens.SubnanosecondNumeratorExpression or CollectionTokens.SubnanosecondDenominatorExpression)
        {
            string clockKey = (Text(payload, CollectionTokens.ClockDomain) ?? string.Empty) + CollectionTokens.Colon + (Text(payload, CollectionTokens.ActivityState) ?? string.Empty);
            JsonElement clock = StateMap(_clockState)[clockKey] is JsonObject clockNode ? Element(clockNode) : Null();
            return Property(clock, path == CollectionTokens.ElapsedNanosecondsExpression ? CollectionTokens.LastIntegralDelta : path == CollectionTokens.SubnanosecondNumeratorExpression ? _numeratorKey : _denominatorKey);
        }

        if (path == CollectionTokens.FavoriteMatchCountExpression)
        {
            JsonElement matches = FavoriteMatches(payload);
            return matches.ValueKind == JsonValueKind.Array ? JsonSerializer.SerializeToElement(matches.GetArrayLength()) : Null();
        }

        if (path == CollectionTokens.MatchedFavoriteIds)
        {
            JsonElement matches = FavoriteMatches(payload);
            return matches.ValueKind == JsonValueKind.Array ? JsonSerializer.SerializeToElement(matches.EnumerateArray().Select(value => Property(value, CollectionTokens.FavoriteSpeciesId))) : Null();
        }

        if (path is CollectionTokens.Representation or CollectionTokens.MatchShape)
        {
            JsonElement matches = FavoriteMatches(payload);
            if (matches.ValueKind != JsonValueKind.Array)
                return Null();

            JsonElement[] entries = [.. matches.EnumerateArray()];
            bool fusion = entries.Any(value => Text(value, CollectionTokens.Representation) == CollectionTokens.Fusion);
            return JsonSerializer.SerializeToElement(path == CollectionTokens.Representation ? fusion ? CollectionTokens.Fusion : CollectionTokens.Normal : string.Join(CollectionTokens.Plus, entries.Select(value => Text(value, CollectionTokens.MatchedRoleMask)).Where(value => value is not null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }

        return AdvancedComputed(path, frame);
    }

    /// <summary>
    /// Captures bounded semantic state before the current event is projected.
    /// </summary>
    /// <param name="type">Event type.</param>
    /// <param name="envelope">Event envelope.</param>
    /// <param name="payload">Source payload.</param>
    private void ObserveBefore(string type, JsonElement envelope, JsonElement payload)
    {
        string? battle = Text(envelope, _battleKey);
        string? action = Text(Property(envelope, _contextKey), _actionKey);
        string? attempt = Text(envelope, _attemptKey);
        if (battle is not null && type == _eventBattleStarted)
            StateMap(_battlesState)[battle] = JsonNode.Parse(payload.GetRawText());

        if (battle is not null && type == _eventBattleEnded && StateMap(_battlesState)[battle] is JsonObject battleState)
            Merge(battleState, payload);

        if (action is not null && type is _eventActionStarted or _eventMoveAttempted or _eventMoveExecuted)
        {
            JsonObject actionState = StateMap(_actionsState)[action] as JsonObject ?? [];
            Merge(actionState, payload);
            actionState[_battleKey] = battle;
            StateMap(_actionsState)[action] = actionState;
        }

        if (action is not null && type == _eventHitCalculated && StateMap(_actionsState)[action] is JsonObject hitAction)
            hitAction[_hitState] = JsonNode.Parse(payload.GetRawText());

        if (type == _eventFavoriteChanged && Bool(Property(payload, CollectionTokens.Effective)) == true)
            StateMap(_preferencesState)[_normalSpeciesKey] = JsonNode.Parse(Property(payload, _normalSpeciesKey).GetRawText());

        if (type == _eventWardrobeOwnership)
            ObserveWardrobe(payload);

        if (type == CollectionTokens.ClockIntervalClosed)
            ObserveClock(payload);

        if (type == _eventCheckpoint && Property(payload, CollectionTokens.FavoriteSnapshot) is JsonElement favorite && Property(favorite, _normalSpeciesKey).ValueKind == JsonValueKind.Array)
            StateMap(_preferencesState)[_normalSpeciesKey] = JsonNode.Parse(Property(favorite, _normalSpeciesKey).GetRawText());

        if (type == _eventAttemptStarted && attempt is not null)
            StateMap(_attemptsState)[attempt] ??= new JsonObject();

        if (type == _eventBadge && Bool(Property(payload, CollectionTokens.OwnedBefore)) == false && Bool(Property(payload, CollectionTokens.OwnedAfter)) == true)
        {
            JsonObject state = AttemptState(attempt);
            state[_postEpochBadgesKey] = ReadNodeInteger(state[_postEpochBadgesKey]) + 1;
        }

        if (type == _eventBattleSummary && battle is not null)
        {
            JsonObject state = AttemptState(attempt);
            JsonElement saved = StateMap(_battlesState)[battle] is JsonObject savedBattle ? Element(savedBattle) : Null();
            if (Text(saved, CollectionTokens.BattleKind) == _trainerKind && Text(saved, _outcomeKey) == _winOutcome)
            {
                int current = ReadNodeInteger(state[_currentKey]) + 1;
                state[_currentKey] = current;
                state[_bestKey] = Math.Max(ReadNodeInteger(state[_bestKey]), current);
            }
        }

        ObserveBattleFact(type, envelope, payload);
        ObserveAdvancedBattle(type, envelope, payload);
        ObserveScopeStatistics(type, envelope, payload);
        ObserveGroup(type, envelope, payload);
    }

    /// <summary>
    /// Maintains only ownership added during this collection epoch, independently for each cosmetic profile.
    /// </summary>
    /// <param name="payload">Actual committed additions and removals; current pre-release ownership is never backfilled.</param>
    private void ObserveWardrobe(JsonElement payload)
    {
        string? profile = Text(payload, CollectionTokens.CosmeticProfileRef);
        JsonElement added = Property(payload, CollectionTokens.AddedPieceKeys);
        JsonElement removed = Property(payload, CollectionTokens.RemovedPieceKeys);
        if (profile is null || added.ValueKind != JsonValueKind.Array || removed.ValueKind != JsonValueKind.Array)
            return;

        JsonObject profiles = StateMap(_preferencesState)[_ownedPiecesKey] as JsonObject ?? new JsonObject();
        StateMap(_preferencesState)[_ownedPiecesKey] ??= profiles;
        JsonObject owned = profiles[profile] as JsonObject ?? new JsonObject();
        profiles[profile] ??= owned;
        foreach (JsonElement key in added.EnumerateArray().Concat(removed.EnumerateArray()))
        {
            if (key.ValueKind != JsonValueKind.String || !key.GetString()!.Contains(':'))
                continue;

            string category = key.GetString()!.Split(':')[0];
            owned[category] ??= new JsonArray();
        }
        foreach (JsonElement key in added.EnumerateArray())
        {
            if (key.ValueKind != JsonValueKind.String || !key.GetString()!.Contains(':'))
                continue;

            JsonArray members = owned[key.GetString()!.Split(':')[0]]!.AsArray();
            if (!members.Any(member => member?.GetValue<string>() == key.GetString()))
                members.Add(key.GetString());
        }
        foreach (JsonElement key in removed.EnumerateArray())
        {
            if (key.ValueKind != JsonValueKind.String || !key.GetString()!.Contains(':'))
                continue;

            JsonArray members = owned[key.GetString()!.Split(':')[0]]!.AsArray();
            foreach (JsonNode member in members.Where(member => member?.GetValue<string>() == key.GetString()).OfType<JsonNode>().ToArray())
                members.Remove(member);
        }
    }

    /// <summary>
    /// Projects exact total and per-category eligible ownership after each real profile change.
    /// </summary>
    /// <param name="family">Current ownership cardinality or newly owned identity family.</param>
    /// <param name="payload">Committed ownership mutation.</param>
    /// <returns>Category rows including known zero after removal, or one unavailable row.</returns>
    private IEnumerable<JsonElement> WardrobeRows(string family, JsonElement payload)
    {
        string? profile = Text(payload, CollectionTokens.CosmeticProfileRef);
        JsonObject? owned = profile is null ? null : (StateMap(_preferencesState)[_ownedPiecesKey] as JsonObject)?[profile] as JsonObject;
        if (owned is null)
        {
            yield return Null();
            yield break;
        }

        if (family == CollectionTokens.WardrobeOwnedPieceCount)
        {
            int total = owned.Sum(pair => pair.Value is JsonArray array ? array.Count : 0);
            yield return JsonSerializer.SerializeToElement(new { category = CollectionTokens.All, eligible_count = total, ownership_source_eligibility = CollectionTokens.PostEpochAdditions });
            foreach ((string category, JsonNode? value) in owned)
                yield return JsonSerializer.SerializeToElement(new { category, eligible_count = value!.AsArray().Count, ownership_source_eligibility = CollectionTokens.PostEpochAdditions });
            yield break;
        }

        JsonElement added = Property(payload, CollectionTokens.AddedPieceKeys);
        if (added.ValueKind != JsonValueKind.Array)
        {
            yield return Null();
            yield break;
        }

        string[] keys = [.. added.EnumerateArray().Where(key => key.ValueKind == JsonValueKind.String && key.GetString()!.Contains(':')).Select(key => key.GetString()!)];
        foreach (IGrouping<string, string> group in keys.GroupBy(key => key.Split(':')[0]))
            yield return JsonSerializer.SerializeToElement(new { category = group.Key, category_added_keys = group.ToArray() });

        yield return JsonSerializer.SerializeToElement(new { category = CollectionTokens.All, category_added_keys = keys });
    }

    /// <summary>
    /// Maintains exact live scope subtotals before recording cross-scope maxima.
    /// </summary>
    /// <param name="type">Actual source event.</param>
    /// <param name="envelope">Attempt and battle identity.</param>
    /// <param name="payload">Committed outcome.</param>
    private void ObserveScopeStatistics(string type, JsonElement envelope, JsonElement payload)
    {
        JsonObject attempt = AttemptState(Text(envelope, _attemptKey));
        string? battleId = Text(envelope, _battleKey);
        JsonObject? battle = battleId is null ? null : StateMap(_battlesState)[battleId] as JsonObject;
        if (type == _eventBattleEnded && battle is not null && Text(Element(battle), CollectionTokens.BattleKind) == _trainerKind && Text(payload, _outcomeKey) == _winOutcome)
            AddExact(attempt, CollectionTokens.TrainerWinCount, JsonSerializer.SerializeToElement(1));

        if (type is not (_eventHpLoss or _eventSubstituteChanged) || Text(Path(payload, CollectionTokens.CauseOriginInstigator), _ownerRoleKey) != _playerRole || Text(Property(payload, CollectionTokens.Target), _ownerRoleKey) != _opponentRole)
            return;

        JsonElement amount = Property(payload, type == _eventHpLoss ? CollectionTokens.ActualHpLost : CollectionTokens.ActualSubstituteHpLost);
        if (Integer(amount) is not BigInteger positive || positive <= 0)
            return;

        AddExact(attempt, CollectionTokens.EligiblePlayerDamageHp, amount);
        if (battle is not null)
            AddExact(battle, CollectionTokens.EligiblePlayerDamageHp, amount);
    }

    /// <summary>
    /// Indexes maxima of actual same-scope subtotals, including open battle prefixes.
    /// </summary>
    /// <param name="type">Current actual source event.</param>
    /// <param name="envelope">Scope identity.</param>
    /// <param name="payload">Committed factual payload.</param>
    /// <param name="output">Sufficient-summary output.</param>
    private void ProjectScopePeaks(string type, JsonElement envelope, JsonElement payload, List<CollectionDirectProjection> output)
    {
        JsonObject attempt = AttemptState(Text(envelope, _attemptKey));
        string? battleId = Text(envelope, _battleKey);
        JsonObject? battle = battleId is null ? null : StateMap(_battlesState)[battleId] as JsonObject;
        if (type == _eventBattleEnded && battle is not null && Text(Element(battle), CollectionTokens.BattleKind) == _trainerKind && Text(payload, _outcomeKey) == _winOutcome)
            AddScopePeak(CollectionTokens.Value4d366754f7e3, CollectionTokens.TrainerWinCount, CollectionTokens.Attempt, ReadNodeBigInteger(attempt[CollectionTokens.TrainerWinCount]), null, output);

        if (type is _eventHpLoss or _eventSubstituteChanged && Text(Path(payload, CollectionTokens.CauseOriginInstigator), _ownerRoleKey) == _playerRole && Text(Property(payload, CollectionTokens.Target), _ownerRoleKey) == _opponentRole)
        {
            JsonElement actual = Property(payload, type == _eventHpLoss ? CollectionTokens.ActualHpLost : CollectionTokens.ActualSubstituteHpLost);
            if (Integer(actual) is not BigInteger positive || positive <= 0)
                return;

            AddScopePeak(CollectionTokens.Value24a5d2d32187, CollectionTokens.EligiblePlayerDamageHp, CollectionTokens.Attempt, ReadNodeBigInteger(attempt[CollectionTokens.EligiblePlayerDamageHp]), null, output);
            if (battle is not null && Text(Element(battle), CollectionTokens.BattleKind) == _trainerKind)
                AddScopePeak(CollectionTokens.Value77d1e92c15e9, CollectionTokens.EligiblePlayerDamageHp, CollectionTokens.Battle, ReadNodeBigInteger(battle[CollectionTokens.EligiblePlayerDamageHp]), _trainerKind, output);
        }
    }

    /// <summary>
    /// Builds a complete typed generic scope-peak key without consumer-specific thresholds.
    /// </summary>
    /// <param name="groupId">Generic sufficient-summary group.</param>
    /// <param name="statistic">Collected statistic.</param>
    /// <param name="scope">Scope whose subtotal is compared.</param>
    /// <param name="value">Actual exact subtotal.</param>
    /// <param name="battleKind">Actual battle kind when relevant.</param>
    /// <param name="output">Projection output.</param>
    private void AddScopePeak(string groupId, string statistic, string scope, BigInteger value, string? battleKind, List<CollectionDirectProjection> output)
    {
        JsonElement group = _catalogue.DirectGroups.Single(definition => definition.GetProperty(_idKey).GetString() == groupId);
        Dictionary<string, string?> values = new()
        {
            [CollectionTokens.StatisticId] = statistic, [CollectionTokens.CompletedScopeKind] = scope,
            [CollectionTokens.SourceRole] = _playerRole, [CollectionTokens.RecipientRole] = statistic == CollectionTokens.TrainerWinCount ? CollectionTokens.NotApplicable : _opponentRole,
            [CollectionTokens.SourceOwnerRole] = _playerRole, [CollectionTokens.BattleKind] = battleKind, [CollectionTokens.PoolPolicyVersion] = CollectionTokens.Value1
        };

        CollectionDimensionValue[] dimensions = [.. group.GetProperty(_dimensionsKey).EnumerateArray().Select(definition => Dimension(definition.GetProperty(_keyKey).GetString()!, values.TryGetValue(definition.GetProperty(_keyKey).GetString()!, out string? actual) && actual is not null ? JsonSerializer.SerializeToElement(actual) : Null(), definition.GetProperty(_kindKey).GetString()))];
        output.Add(new CollectionDirectProjection(groupId, dimensions, [new CollectionMeasureUpdate(_maximumValueKey, _scalarKind, _maxOperator, _known, value, null, null)]));
    }

    /// <summary>
    /// Updates bounded actor, health, and execution summaries from committed engine facts.
    /// </summary>
    /// <param name="type">Actual event type.</param>
    /// <param name="envelope">Receipt-qualified context.</param>
    /// <param name="payload">Actual engine outcome.</param>
    private void ObserveBattleFact(string type, JsonElement envelope, JsonElement payload)
    {
        string? battleId = Text(envelope, _battleKey);
        if (battleId is null || StateMap(_battlesState)[battleId] is not JsonObject battle)
            return;

        battle[_attemptKey] ??= Text(envelope, _attemptKey);
        JsonObject actors = battle[CollectionTokens.ActorSummaries] as JsonObject ?? new JsonObject();
        if (battle[CollectionTokens.ActorSummaries] is null)
            battle[CollectionTokens.ActorSummaries] = actors;

        if (type == CollectionTokens.RoundFinished)
            battle[CollectionTokens.CompletedRoundCount] = ReadNodeInteger(battle[CollectionTokens.CompletedRoundCount]) + 1;

        JsonElement actor = Property(payload, type is _eventHealing or _eventPpRestored ? CollectionTokens.Recipient : type is _eventHpLoss or _eventSubstituteChanged or _eventHitResolved or CollectionTokens.BattlerFainted or CollectionTokens.StatusMajorApplicationResolved or _eventStatusCured ? CollectionTokens.Target : CollectionTokens.Actor);
        string? actorId = Text(actor, CollectionTokens.AppearanceId) ?? Text(actor, CollectionTokens.PokemonInstanceId) ?? Text(actor, CollectionTokens.ActorId);
        if (actorId is null)
            return;

        JsonObject row = actors[actorId] as JsonObject ?? new JsonObject { [CollectionTokens.Actor] = JsonNode.Parse(actor.GetRawText()), [_ownerRoleKey] = Text(actor, _ownerRoleKey) };
        if (actors[actorId] is null)
            actors[actorId] = row;

        if (type == CollectionTokens.BattlerEntered)
        {
            JsonElement snapshot = Property(payload, CollectionTokens.EntrySnapshot);
            foreach ((string target, string source) in new[] { (CollectionTokens.EntryHp, CollectionTokens.Hp), (CollectionTokens.ExitHp, CollectionTokens.Hp), (CollectionTokens.MinHp, CollectionTokens.Hp), (CollectionTokens.EntryMaxHp, CollectionTokens.MaxHp), (CollectionTokens.ExitMaxHp, CollectionTokens.MaxHp), (CollectionTokens.TerminalMajorStatus, CollectionTokens.StatusId) })
                row[target] = JsonNode.Parse(Property(snapshot, source).GetRawText());

            row[CollectionTokens.EntryKind] = JsonNode.Parse(Property(payload, CollectionTokens.EntryKind).GetRawText());
        }

        if (type is _eventHpLoss or _eventHealing)
        {
            JsonElement hp = Property(payload, CollectionTokens.HpAfter);
            if (Integer(hp) is BigInteger amount)
            {
                row[CollectionTokens.ExitHp] = JsonNode.Parse(hp.GetRawText());
                row[CollectionTokens.ExitMaxHp] = JsonNode.Parse(Property(payload, CollectionTokens.MaxHp).GetRawText());
                if (row[CollectionTokens.MinHp] is null || amount < ReadNodeBigInteger(row[CollectionTokens.MinHp]))
                    row[CollectionTokens.MinHp] = amount.ToString(CultureInfo.InvariantCulture);
            }

            if (type == _eventHealing && Integer(Property(payload, CollectionTokens.ActualRestoredHp)) > 0)
            {
                row[CollectionTokens.LastRecoveryStartHp] = JsonNode.Parse(Property(payload, CollectionTokens.HpBefore).GetRawText());
                row[CollectionTokens.ObservedFloorBeforeRecovery] = row[CollectionTokens.MinHp]?.DeepClone();
                row[CollectionTokens.LastRecoverySourceKind] = JsonNode.Parse(Path(payload, CollectionTokens.EffectSourceKind).GetRawText());
            }
        }

        if (type is _eventHpLoss or _eventSubstituteChanged)
        {
            JsonElement amount = Property(payload, type == _eventHpLoss ? CollectionTokens.ActualHpLost : CollectionTokens.ActualSubstituteHpLost);
            if (Integer(amount) is BigInteger actual && actual > 0)
            {
                AddExact(row, CollectionTokens.DamageTaken, amount);
                if (type == _eventSubstituteChanged)
                    AddExact(row, CollectionTokens.SubstituteDamageTaken, amount);

                JsonElement source = Path(payload, CollectionTokens.CauseOriginInstigator);
                string? sourceId = Text(source, CollectionTokens.AppearanceId) ?? Text(source, CollectionTokens.PokemonInstanceId) ?? Text(source, CollectionTokens.ActorId);
                if (sourceId is not null && actors[sourceId] is JsonObject sourceRow && Text(actor, _ownerRoleKey) != Text(source, _ownerRoleKey))
                {
                    AddExact(sourceRow, CollectionTokens.DamageDealt, amount);
                    if (type == _eventSubstituteChanged)
                        AddExact(sourceRow, CollectionTokens.SubstituteDamageDealt, amount);
                }
            }
        }

        if (type == _eventHitResolved && Bool(Path(payload, CollectionTokens.ApplicationSnapshotCritical)) == true && Integer(Path(payload, CollectionTokens.ApplicationSnapshotActualPoolHpLost)) > 0)
        {
            JsonElement source = Property(payload, CollectionTokens.Actor);
            if (Text(source, _ownerRoleKey) != Text(actor, _ownerRoleKey))
                row[CollectionTokens.ResolvedCriticalHitsReceived] = ReadNodeInteger(row[CollectionTokens.ResolvedCriticalHitsReceived]) + 1;
        }

        if (type == _eventMoveFinished && (Integer(Property(payload, CollectionTokens.PositiveHpTargets)) > 0 || Integer(Property(payload, CollectionTokens.PositiveSubstituteTargets)) > 0))
            row[CollectionTokens.LandedExecutions] = ReadNodeInteger(row[CollectionTokens.LandedExecutions]) + 1;

        if (type == CollectionTokens.StatusMajorApplicationResolved && Text(payload, CollectionTokens.ApplicationOutcome) == CollectionTokens.Applied)
            row[CollectionTokens.TerminalMajorStatus] = JsonNode.Parse(Property(payload, CollectionTokens.StatusAfterApplication).GetRawText());

        if (type == _eventStatusCured && Text(payload, CollectionTokens.CureOutcome) == CollectionTokens.Cured)
            row[CollectionTokens.TerminalMajorStatus] = JsonNode.Parse(Property(payload, CollectionTokens.StatusAfter).GetRawText());
    }

    /// <summary>
    /// Folds a terminal battle only after all known causal groups have closed.
    /// </summary>
    /// <param name="envelope">Actual terminal event context.</param>
    /// <param name="families">Accumulated effects.</param>
    /// <param name="direct">Direct sufficient summaries.</param>
    private void FinalizeBattle(JsonElement envelope, List<CollectionProjection> families, List<CollectionDirectProjection> direct)
    {
        string? battleId = Text(envelope, _battleKey);
        if (battleId is null || StateMap(_battlesState)[battleId] is not JsonObject battle)
            return;

        if (StateMap(_groupsState).Any(pair => pair.Value is JsonObject group && group[_battleKey]?.GetValue<string>() == battleId))
        {
            battle[CollectionTokens.PendingFinalization] = JsonNode.Parse(envelope.GetRawText());
            return;
        }

        JsonArray actors = new();
        if (battle[CollectionTokens.ActorSummaries] is JsonObject actorMap)
        {
            foreach (JsonObject actor in actorMap.Select(pair => pair.Value).OfType<JsonObject>())
            {
                JsonObject final = (JsonObject)actor.DeepClone();
                final[CollectionTokens.Rounds] = ReadNodeInteger(battle[CollectionTokens.CompletedRoundCount]);
                final[_outcomeKey] = battle[_outcomeKey]?.DeepClone();
                final[CollectionTokens.BattleKind] = battle[CollectionTokens.BattleKind]?.DeepClone();
                final[CollectionTokens.TerminalHp] = final[CollectionTokens.ExitHp]?.DeepClone();
                final[CollectionTokens.TerminalSnapshotPhase] = CollectionTokens.BeforeCleanup;
                final[CollectionTokens.ResolvedCriticalHitsReceived] ??= 0;
                foreach (string amount in new[] { CollectionTokens.DamageDealt, CollectionTokens.DamageTaken, CollectionTokens.LandedExecutions })
                    final[amount] ??= CollectionTokens.Value0;

                actors.Add(final);
            }
        }

        JsonObject summary = new()
        {
            [CollectionTokens.SummaryVersion] = 1, [CollectionTokens.PerActor] = actors,
            [CollectionTokens.ActorHealthOutcomeJoints] = actors.DeepClone(),
            [CollectionTokens.CompoundContext] = actors.DeepClone(),
            [CollectionTokens.Outcome] = battle[_outcomeKey]?.DeepClone(),
            [CollectionTokens.Rounds] = ReadNodeInteger(battle[CollectionTokens.CompletedRoundCount])
        };

        CompleteAdvancedSummary(battle, envelope, actors, summary);
        JsonObject derived = JsonNode.Parse(envelope.GetRawText())!.AsObject();
        derived[_typeKey] = _eventBattleSummary;
        derived[_payloadKey] = summary;
        JsonElement derivedEvent = Element(derived);
        JsonElement derivedPayload = Element(summary);
        ObserveBefore(_eventBattleSummary, derivedEvent, derivedPayload);
        Project(_eventBattleSummary, derivedEvent, derivedPayload, families, direct, false);
        ProjectRecoveryDirect(battle, derivedEvent, derivedPayload, direct);
        ObserveAfter(_eventBattleSummary, derivedEvent, derivedPayload);
    }

    /// <summary>
    /// Matches current canonical favorites against the actual retained species composition.
    /// </summary>
    /// <param name="payload">Committed runner payload.</param>
    /// <param name="slots">Whether to emit one observation per actual matching component slot.</param>
    /// <returns>Exact matches or unknown if the preference context is unavailable.</returns>
    private JsonElement FavoriteMatches(JsonElement payload, bool slots = false)
    {
        JsonElement favorites = StateMap(_preferencesState)[_normalSpeciesKey] is JsonArray favoriteArray ? Element(favoriteArray) : Null();
        if (favorites.ValueKind != JsonValueKind.Array)
            return Null();

        JsonElement species = Property(payload, CollectionTokens.SpeciesAfterRef);
        if (species.ValueKind != JsonValueKind.Object)
            return Null();

        JsonElement normal = Property(species, CollectionTokens.CanonicalId);
        JsonElement body = Property(species, CollectionTokens.FusionBodyId);
        JsonElement head = Property(species, CollectionTokens.FusionHeadId);
        if (!Available(body))
            body = Property(species, CollectionTokens.BodyId);

        if (!Available(head))
            head = Property(species, CollectionTokens.HeadId);

        bool fusion = Available(body) || Available(head);
        List<JsonElement> matches = [];
        foreach (JsonElement favorite in favorites.EnumerateArray())
        {
            List<string> roles = [];
            if (!fusion && Available(normal) && Equal(favorite, normal))
                roles.Add(CollectionTokens.Normal);

            if (Available(body) && Equal(favorite, body))
                roles.Add(CollectionTokens.Body);

            if (Available(head) && Equal(favorite, head))
                roles.Add(CollectionTokens.Head);

            if (roles.Count == 0)
                continue;

            foreach (string role in slots ? roles : [string.Join(CollectionTokens.Plus, roles)])
                matches.Add(JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [CollectionTokens.FavoriteSpeciesId] = favorite, [CollectionTokens.Representation] = JsonSerializer.SerializeToElement(fusion ? CollectionTokens.Fusion : CollectionTokens.Normal), [CollectionTokens.MatchedRoleMask] = JsonSerializer.SerializeToElement(string.Join(CollectionTokens.Plus, roles)), [CollectionTokens.SlotRole] = JsonSerializer.SerializeToElement(role) }));
        }

        return JsonSerializer.SerializeToElement(matches);
    }

    /// <summary>
    /// Accumulates exact subnanosecond remainders before emitting an integral duration delta.
    /// </summary>
    /// <param name="payload">Actual closed clock interval.</param>
    private void ObserveClock(JsonElement payload)
    {
        string key = (Text(payload, CollectionTokens.ClockDomain) ?? string.Empty) + CollectionTokens.Colon + (Text(payload, CollectionTokens.ActivityState) ?? string.Empty);
        JsonObject clocks = StateMap(_clockState);
        JsonObject clock = clocks[key] as JsonObject ?? new JsonObject();
        if (!TryDecimalFraction(Property(payload, CollectionTokens.DurationSeconds), out BigInteger numerator, out BigInteger denominator))
        {
            clock[CollectionTokens.LastIntegralDelta] = null;
            if (clocks[key] is null)
                clocks[key] = clock;

            return;
        }
        BigInteger oldNumerator = ReadNodeBigInteger(clock[_numeratorKey]);
        BigInteger oldDenominator = BigInteger.Max(ReadNodeBigInteger(clock[_denominatorKey]), BigInteger.One);
        numerator = numerator * 1_000_000_000 * oldDenominator + oldNumerator * denominator;
        denominator *= oldDenominator;
        BigInteger integral = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(remainder), denominator);
        clock[CollectionTokens.LastIntegralDelta] = integral.ToString(CultureInfo.InvariantCulture);
        clock[_numeratorKey] = (remainder / divisor).ToString(CultureInfo.InvariantCulture);
        clock[_denominatorKey] = (denominator / divisor).ToString(CultureInfo.InvariantCulture);
        if (clocks[key] is null)
            clocks[key] = clock;
    }

    /// <summary>
    /// Parses a finite decimal or exponent representation into an exact rational.
    /// </summary>
    /// <param name="value">Actual decimal source.</param>
    /// <param name="numerator">Exact signed numerator.</param>
    /// <param name="denominator">Exact positive denominator.</param>
    /// <returns>Whether the value is a supported finite decimal.</returns>
    private static bool TryDecimalFraction(JsonElement value, out BigInteger numerator, out BigInteger denominator)
    {
        numerator = BigInteger.Zero;
        denominator = BigInteger.One;
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        if (text is null)
            return false;

        string[] exponentParts = text.Split(['e', 'E']);
        if (exponentParts.Length > 2 || exponentParts.Length == 2 && !int.TryParse(exponentParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return false;

        int exponent = exponentParts.Length == 2 ? int.Parse(exponentParts[1], CultureInfo.InvariantCulture) : 0;
        string[] decimalParts = exponentParts[0].Split('.');
        if (decimalParts.Length > 2 || Math.Abs((long)exponent) > 10_000)
            return false;

        string digits = string.Concat(decimalParts);
        if (!BigInteger.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator))
            return false;

        int fractionalDigits = decimalParts.Length == 2 ? decimalParts[1].Length : 0;
        int scale = fractionalDigits - exponent;
        if (scale >= 0)
        {
            denominator = BigInteger.Pow(10, scale);
        }
        else
        {
            numerator *= BigInteger.Pow(10, -scale);
        }

        return numerator >= BigInteger.Zero;
    }

    /// <summary>
    /// Updates one bounded item activation from real committed recipient effects.
    /// </summary>
    /// <param name="type">Event type.</param>
    /// <param name="envelope">Envelope.</param>
    /// <param name="payload">Actual effect fields.</param>
    private void ObserveGroup(string type, JsonElement envelope, JsonElement payload)
    {
        if (type == _eventBattleSummary)
            return;

        string? id = GroupId(envelope, payload);
        if (id is null)
            return;

        JsonObject groups = StateMap(_groupsState);
        JsonObject group;
        if (groups[id] is JsonObject existingGroup)
        {
            group = existingGroup;
        }
        else
        {
            group = new JsonObject { [_recipientsKey] = new JsonObject() };
            groups[id] = group;
        }
        group[_battleKey] ??= Text(envelope, _battleKey);
        if (type == _eventGroupOpened)
            group[CollectionItemGroupKeys.Parent] = JsonNode.Parse(Property(payload, CollectionItemGroupKeys.Parent).GetRawText());

        if (type == _eventGroupClosed)
            MergeChildItemGroup(group, groups);

        if (type == _eventGroupClosed && group[_itemKey] is JsonObject resolvedItem && resolvedItem[_payloadKey] is JsonObject resolvedPayload && IsBagRecoveryContext(Element(resolvedPayload)))
        {
            bool recovered = group[_recipientsKey] is JsonObject actualRecipients && actualRecipients.Any(pair => pair.Value is JsonObject recipientRow && recipientRow[CollectionTokens.RecipientEffects] is JsonObject actualEffects && (ReadNodeBigInteger(actualEffects[CollectionTokens.HpActualTotal]) > 0 || ReadNodeBigInteger(actualEffects[CollectionTokens.PpActualTotal]) > 0 || actualEffects[CollectionTokens.MajorCuredStatusIds] is JsonArray { Count: > 0 } || actualEffects[CollectionTokens.VolatileCuredEffectIds] is JsonArray { Count: > 0 }));
            if (recovered)
                AttemptState(Text(envelope, _attemptKey))[_currentKey] = 0;
        }

        if (type == _eventGroupOpened)
            group[_kindKey] = JsonNode.Parse(Property(payload, _kindKey).GetRawText());

        if (type == _eventItemResolved)
        {
            group[_itemKey] = JsonNode.Parse(envelope.GetRawText());
            return;
        }

        if (type is not (_eventHealing or _eventPpRestored or _eventStatusCured or _eventVolatileRemoved))
            return;

        JsonElement recipient = Property(payload, type is _eventStatusCured or _eventVolatileRemoved ? CollectionTokens.Target : CollectionTokens.Recipient);
        string? recipientId = Text(recipient, CollectionTokens.PokemonInstanceId) ?? Text(recipient, CollectionTokens.OwnedEntityId) ?? Text(recipient, CollectionTokens.OwnedEntityIdIfOwned);
        if (recipientId is null)
            return;

        JsonObject recipients = group[_recipientsKey]!.AsObject();
        JsonObject row;
        if (recipients[recipientId] is JsonObject existingRecipient)
        {
            row = existingRecipient;
        }
        else
        {
            row = new JsonObject { [CollectionTokens.Recipient] = JsonNode.Parse(recipient.GetRawText()), [CollectionTokens.RecipientEffects] = new JsonObject() };
            recipients[recipientId] = row;
        }

        JsonObject effects = row[CollectionTokens.RecipientEffects]!.AsObject();
        if (type == _eventHealing)
        {
            AddExact(effects, CollectionTokens.HpActualTotal, Property(payload, CollectionTokens.ActualRestoredHp));
            ObserveHealingSource(effects, payload);

            AddExact(effects, CollectionTokens.HpCapExcessTotal, Property(payload, CollectionTokens.CapacityExcessHp));
            effects[CollectionTokens.HpCapExcessKnown] = (effects[CollectionTokens.HpCapExcessKnown] is null || effects[CollectionTokens.HpCapExcessKnown]!.GetValue<bool>()) && Available(Property(payload, CollectionTokens.CapacityExcessHp));
            string? amountKind = Text(payload, CollectionTokens.HealingAmountKind);
            effects[CollectionTokens.HpHealingAmountKind] = effects[CollectionTokens.HpHealingAmountKind] is JsonValue previousKind && previousKind.GetValue<string>() != amountKind ? CollectionTokens.Mixed : amountKind;
        }

        if (type == _eventPpRestored)
        {
            AddExact(effects, CollectionTokens.PpActualTotal, Property(payload, CollectionTokens.ActualTotalPp));
            ObservePpSource(effects, payload);
        }

        if (type == _eventStatusCured && Text(payload, CollectionTokens.CureOutcome) == CollectionTokens.Cured)
            AddMember(effects, CollectionTokens.MajorCuredStatusIds, Property(payload, CollectionTokens.StatusBefore));

        if (type == _eventVolatileRemoved && Text(payload, _outcomeKey) == CollectionTokens.Success)
            AddMember(effects, CollectionTokens.VolatileCuredEffectIds, Property(payload, CollectionTokens.EffectId));

        bool positiveHp = ReadNodeBigInteger(effects[CollectionTokens.HpActualTotal]) > 0;
        bool directHp = ReadNodeBigInteger(effects[CollectionTokens.HpDirectItemTotal]) > 0;
        effects[CollectionTokens.DirectItemHpPositive] = directHp;
        effects[CollectionTokens.DirectItemHpResult] = directHp ? CollectionTokens.Positive : CollectionTokens.NoGain;
        effects[CollectionTokens.HpResult] = !positiveHp ? CollectionTokens.NoGain : effects[CollectionTokens.HpCapExcessKnown] is JsonValue capKnown && capKnown.GetValue<bool>() ? ReadNodeBigInteger(effects[CollectionTokens.HpCapExcessTotal]) > 0 ? CollectionTokens.PositiveKnownExcess : CollectionTokens.PositiveKnownZeroExcess : CollectionTokens.PositiveUnknownExcess;
        effects[CollectionTokens.PpResult] = ReadNodeBigInteger(effects[CollectionTokens.PpActualTotal]) > 0 ? CollectionTokens.Positive : CollectionTokens.NoGain;
        if (positiveHp || ReadNodeBigInteger(effects[CollectionTokens.PpActualTotal]) > 0 || effects[CollectionTokens.MajorCuredStatusIds] is JsonArray { Count: > 0 } || effects[CollectionTokens.VolatileCuredEffectIds] is JsonArray { Count: > 0 })
        {
            JsonObject? item = group[_itemKey] as JsonObject;
            JsonElement itemPayload = item?[_payloadKey] is JsonObject itemPayloadNode ? Element(itemPayloadNode) : Null();
            if (IsBagRecoveryContext(itemPayload))
                AttemptState(Text(envelope, _attemptKey))[_currentKey] = 0;
        }
    }

    /// <summary>
    /// Accumulates each immediate healing source without mistaking another item for the initiating Bag item.
    /// </summary>
    /// <param name="effects">Bounded recipient totals.</param>
    /// <param name="payload">Actual committed healing.</param>
    private static void ObserveHealingSource(JsonObject effects, JsonElement payload)
    {
        JsonElement source = Property(payload, CollectionTokens.EffectSource);
        JsonElement sourceId = Property(source, CollectionTokens.CanonicalId);
        if (!Available(sourceId))
            sourceId = Property(source, CollectionTokens.MechanicId);

        string? kind = Text(source, _kindKey);
        string key = JsonSerializer.Serialize(new { kind, source_id = sourceId });
        JsonObject buckets = effects[CollectionTokens.HealingSourceBuckets] as JsonObject ?? new JsonObject();
        effects[CollectionTokens.HealingSourceBuckets] ??= buckets;
        JsonObject bucket = buckets[key] as JsonObject ?? new JsonObject { [CollectionTokens.SourceKind] = kind, [CollectionTokens.SourceId] = JsonNode.Parse(sourceId.GetRawText()), [CollectionTokens.ActualKnown] = true, [CollectionTokens.CapKnown] = true };
        buckets[key] ??= bucket;
        AddExact(bucket, CollectionTokens.ActualTotal, Property(payload, CollectionTokens.ActualRestoredHp));
        AddExact(bucket, CollectionTokens.CapTotal, Property(payload, CollectionTokens.CapacityExcessHp));
        bucket[CollectionTokens.ActualKnown] = bucket[CollectionTokens.ActualKnown]!.GetValue<bool>() && Integer(Property(payload, CollectionTokens.ActualRestoredHp)).HasValue;
        bucket[CollectionTokens.CapKnown] = bucket[CollectionTokens.CapKnown]!.GetValue<bool>() && Integer(Property(payload, CollectionTokens.CapacityExcessHp)).HasValue;
        string? amountKind = Text(payload, CollectionTokens.HealingAmountKind);
        bucket[CollectionTokens.HealingAmountKind] = bucket[CollectionTokens.HealingAmountKind] is JsonValue previous && previous.GetValue<string>() != amountKind ? CollectionTokens.Mixed : amountKind;
    }

    /// <summary>
    /// Finalizes direct versus secondary source views after the real item outcome supplies its canonical identity.
    /// </summary>
    /// <param name="group">Completed bounded causal group.</param>
    /// <param name="itemPayload">Actual initiating item outcome.</param>
    private static void FinalizeItemRecipients(JsonObject group, JsonObject itemPayload)
    {
        JsonElement initiatingId = Property(Element(itemPayload), CollectionTokens.ItemId);
        foreach (JsonObject recipient in group[_recipientsKey]!.AsObject().Select(pair => pair.Value).OfType<JsonObject>())
        {
            JsonObject effects = recipient[CollectionTokens.RecipientEffects]!.AsObject();
            FinalizePpSources(effects, initiatingId);
            JsonObject buckets = effects[CollectionTokens.HealingSourceBuckets] as JsonObject ?? new JsonObject();
            BigInteger directTotal = BigInteger.Zero;
            BigInteger directCap = BigInteger.Zero;
            BigInteger secondaryTotal = BigInteger.Zero;
            bool classificationKnown = true;
            bool directKnown = true;
            bool directCapKnown = true;
            bool secondaryKnown = true;
            string? directAmountKind = null;
            JsonArray secondary = [];
            foreach ((string _, JsonNode? node) in buckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                JsonObject bucket = node!.AsObject();
                JsonElement observed = Element(bucket);
                string? kind = Text(observed, CollectionTokens.SourceKind);
                JsonElement sourceId = Property(observed, CollectionTokens.SourceId);
                bool sourceKnown = kind is not null && kind != _unknown && kind != CollectionTokens.Unsupported && (kind != _itemSourceKind || Available(initiatingId) && Available(sourceId));
                bool direct = sourceKnown && kind == _itemSourceKind && Equal(sourceId, initiatingId);
                bool actualKnown = bucket[CollectionTokens.ActualKnown]!.GetValue<bool>();
                bool capKnown = bucket[CollectionTokens.CapKnown]!.GetValue<bool>();
                BigInteger amount = ReadNodeBigInteger(bucket[CollectionTokens.ActualTotal]);
                classificationKnown &= sourceKnown;
                if (direct)
                {
                    directTotal += amount;
                    directCap += ReadNodeBigInteger(bucket[CollectionTokens.CapTotal]);
                    directKnown &= actualKnown;
                    directCapKnown &= capKnown;
                    string? amountKind = Text(observed, CollectionTokens.HealingAmountKind);
                    directAmountKind = directAmountKind is null || directAmountKind == amountKind ? amountKind : CollectionTokens.Mixed;
                }
                else
                {
                    if (sourceKnown)
                        secondaryTotal += amount;

                    secondaryKnown &= actualKnown && sourceKnown;
                    secondary.Add(new JsonObject { [CollectionTokens.SourceKind] = kind, [CollectionTokens.SourceId] = JsonNode.Parse(sourceId.GetRawText()), [CollectionTokens.SourceClassification] = sourceKnown ? CollectionTokens.Secondary : _unknown, [CollectionTokens.ActualRestoredHp] = actualKnown ? amount.ToString(CultureInfo.InvariantCulture) : null, [CollectionTokens.CapacityExcessHp] = capKnown ? ReadNodeBigInteger(bucket[CollectionTokens.CapTotal]).ToString(CultureInfo.InvariantCulture) : null, [CollectionTokens.CapacityExcessKnown] = capKnown, [CollectionTokens.HealingAmountKind] = bucket[CollectionTokens.HealingAmountKind]?.DeepClone() });
                }
            }
            directKnown &= classificationKnown;
            directCapKnown &= classificationKnown;
            effects[CollectionTokens.HpDirectItemTotal] = directKnown ? directTotal.ToString(CultureInfo.InvariantCulture) : null;
            effects[CollectionTokens.HpDirectItemCapExcess] = directCapKnown ? directCap.ToString(CultureInfo.InvariantCulture) : null;
            effects[CollectionTokens.HpDirectItemCapExcessKnown] = directCapKnown;
            effects[CollectionTokens.HpDirectItemHealingAmountKind] = directAmountKind;
            effects[CollectionTokens.DirectItemHpPositive] = directKnown ? directTotal > 0 : null;
            effects[CollectionTokens.DirectItemHpResult] = !directKnown ? null : directTotal <= 0 ? CollectionTokens.NoGain : !directCapKnown ? CollectionTokens.PositiveUnknownExcess : directCap > 0 ? CollectionTokens.PositiveKnownExcess : CollectionTokens.PositiveKnownZeroExcess;
            effects[CollectionTokens.HpSecondaryTotal] = secondaryKnown ? secondaryTotal.ToString(CultureInfo.InvariantCulture) : null;
            effects[CollectionTokens.SecondarySourceResult] = !secondaryKnown ? null : secondaryTotal > 0 ? CollectionTokens.Positive : CollectionTokens.NoGain;
            effects[CollectionTokens.HpSecondaryBySource] = secondary;
            effects[CollectionTokens.AnyEffect] = ReadNodeBigInteger(effects[CollectionTokens.HpActualTotal]) > 0 || ReadNodeBigInteger(effects[CollectionTokens.PpActualTotal]) > 0 || effects[CollectionTokens.MajorCuredStatusIds] is JsonArray { Count: > 0 } || effects[CollectionTokens.VolatileCuredEffectIds] is JsonArray { Count: > 0 };
            effects[CollectionTokens.MajorCuredStatusIds] ??= new JsonArray();
            effects[CollectionTokens.VolatileCuredEffectIds] ??= new JsonArray();
        }
    }

    /// <summary>
    /// Recognizes actual Bag recovery contexts on both sides of a battle boundary.
    /// </summary>
    /// <param name="payload">Actual finalized item-use context.</param>
    /// <returns>Whether the item was used from the player's Bag.</returns>
    private static bool IsBagRecoveryContext(JsonElement payload)
        => Text(payload, CollectionTokens.UseContext) is _bagContext or CollectionTokens.BagBattle or CollectionTokens.BagOutsideBattle or CollectionTokens.BagField;

    /// <summary>
    /// Discards completed causal working state after its durable effects have been produced.
    /// </summary>
    /// <param name="type">Event type.</param>
    /// <param name="envelope">Envelope.</param>
    /// <param name="payload">Source payload.</param>
    private void ObserveAfter(string type, JsonElement envelope, JsonElement payload)
    {
        string? action = Text(Property(envelope, _contextKey), _actionKey);
        if (type == _eventMoveFinished)
        {
            string? parent = Text(Property(envelope, _contextKey), CollectionTokens.ParentActionId);
            if (parent is not null && Text(Property(payload, _invocationKey), _invocationKindKey) == _moveCallKind && Text(payload, _resultKey) == _executedEffect && StateMap(_actionsState)[parent] is JsonObject parentState)
                parentState[_delegatedSuccessKey] = true;
        }

        if (type == _eventActionFinished && action is not null)
            StateMap(_actionsState).Remove(action);

        if (type is _eventBattleSummary or CollectionTokens.BattleInterrupted && Text(envelope, _battleKey) is string battle)
        {
            StateMap(_battlesState).Remove(battle);
            string[] closedActions = [.. StateMap(_actionsState).Where(pair => pair.Value is JsonObject value && value[_battleKey]?.GetValue<string>() == battle).Select(pair => pair.Key)];
            foreach (string closedAction in closedActions)
                StateMap(_actionsState).Remove(closedAction);

            string[] closedGroups = [.. StateMap(_groupsState).Where(pair => pair.Value is JsonObject value && Text(Element(value), _battleKey) == battle).Select(pair => pair.Key)];
            foreach (string closedGroup in closedGroups)
                StateMap(_groupsState).Remove(closedGroup);

            StateMap(_familyState).Clear();
        }

        if (type == _eventAttemptCompleted && Text(envelope, _attemptKey) is string attempt)
        {
            _completedAttemptIdentity = attempt;
            _completedAttemptState = StateMap(_attemptsState)[attempt] is JsonObject completed ? Element(completed) : null;
            StateMap(_attemptsState).Remove(attempt);
        }
    }

    /// <summary>
    /// Creates an exact scalar, set, or ordered update from actual selected evidence.
    /// </summary>
    /// <param name="definition">Measure definition.</param>
    /// <param name="value">Actual selected value.</param>
    /// <returns>A typed known or unknown update.</returns>
    private static CollectionMeasureUpdate Measure(JsonElement definition, JsonElement value)
    {
        string key = definition.GetProperty(_keyKey).GetString()!;
        string kind = definition.GetProperty(_kindKey).GetString()!;
        string merge = definition.GetProperty(_mergeKey).GetString()!;
        if (AvailabilityWrapper(value) is string coverage)
        {
            if (coverage != _known)
                return new CollectionMeasureUpdate(key, kind, merge, coverage, null, null, null);
            value = Property(value, _valueKey);
        }
        if (!Available(value))
            return new CollectionMeasureUpdate(key, kind, merge, _unknown, null, null, null);

        if (kind == _scalarKind)
        {
            BigInteger? scalar = Integer(value);
            return new CollectionMeasureUpdate(key, kind, merge, scalar.HasValue ? _known : _unknown, scalar, null, null);
        }

        if (kind == _setKind)
        {
            JsonElement[] members = value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray().Select(member => member.Clone())] : [value.Clone()];
            return new CollectionMeasureUpdate(key, kind, merge, _known, null, members, null);
        }

        return new CollectionMeasureUpdate(key, kind, merge, _known, null, null, value.Clone());
    }

    /// <summary>
    /// Creates a typed dimension without conflating absent, false, empty, and zero values.
    /// </summary>
    /// <param name="key">Dimension key.</param>
    /// <param name="value">Actual value.</param>
    /// <param name="declaredKind">Optional explicit summary kind.</param>
    /// <returns>The typed dimension.</returns>
    private static CollectionDimensionValue Dimension(string key, JsonElement value, string? declaredKind = null)
    {
        string kind = declaredKind ?? (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(_numeratorKey, out _) && value.TryGetProperty(_denominatorKey, out _) ? _rationalDimensionKind : value.ValueKind == JsonValueKind.Array || value.ValueKind == JsonValueKind.Object ? _setDimensionKind : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? _booleanKind : Integer(value).HasValue ? _integerKind : _textKind);
        if (AvailabilityWrapper(value) is string coverage)
        {
            if (coverage != _known)
                return new CollectionDimensionValue(key, kind, coverage, Null());
            value = Property(value, _valueKey);
        }

        if (!Available(value))
            return new CollectionDimensionValue(key, kind, _unknown, Null());

        if (kind == _textKind && value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            value = JsonSerializer.SerializeToElement(value.GetRawText());

        if (kind == _textKind && value.ValueKind != JsonValueKind.String)
            return new CollectionDimensionValue(key, kind, _unknown, Null());

        if (kind == _integerKind && (Integer(value) is not BigInteger integer || integer < long.MinValue || integer > long.MaxValue))
            return new CollectionDimensionValue(key, kind, _unknown, Null());

        if (kind == CollectionTokens.Uint64 && (Integer(value) is not BigInteger unsigned || unsigned < ulong.MinValue || unsigned > ulong.MaxValue))
            return new CollectionDimensionValue(key, kind, _unknown, Null());

        if (kind == _booleanKind && Bool(value) is null)
            return new CollectionDimensionValue(key, kind, _unknown, Null());

        if (kind == _rationalDimensionKind && (Integer(Property(value, _numeratorKey)) is not BigInteger numerator || Integer(Property(value, _denominatorKey)) is not BigInteger denominator || numerator < 0 || numerator > long.MaxValue || denominator <= 0 || denominator > long.MaxValue))
            return new CollectionDimensionValue(key, kind, _unknown, Null());

        if (kind == _setDimensionKind && value.ValueKind != JsonValueKind.Array)
            value = JsonSerializer.SerializeToElement(new[] { value });

        if (kind == _nativeKind && value.ValueKind != JsonValueKind.Object)
            value = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [_namespaceKey] = JsonSerializer.SerializeToElement(key), [_nativeValueKey] = value });

        return new CollectionDimensionValue(key, kind, _known, value.Clone());
    }

    /// <summary>
    /// Reads an exact path, preserving array membership rather than selecting an arbitrary element.
    /// </summary>
    /// <param name="value">Source value.</param>
    /// <param name="path">Dot-separated exact field path.</param>
    /// <returns>Selected value or null.</returns>
    private static JsonElement Path(JsonElement value, string path)
    {
        if (!Available(value))
            return Null();

        if (path.Length == 0)
            return value;

        string[] parts = path.Split('.', 2);
        if (value.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] values = [.. value.EnumerateArray().Select(item => Path(item, path)).Where(Available)];
            return JsonSerializer.SerializeToElement(values);
        }

        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[0], out JsonElement next))
            return Null();

        return parts.Length == 1 ? next : Path(next, parts[1]);
    }

    /// <summary>
    /// Determines whether a retained commit contributes its full generated base vector.
    /// </summary>
    /// <param name="payload">Actual owned commit.</param>
    /// <returns>Whether this is an actual retained acquisition or committed evolution.</returns>
    private static bool IsStatContribution(JsonElement payload)
    {
        string? capture = Text(payload, CollectionTokens.CaptureKind);
        if (capture is _baselineCapture or _reconciliationCapture || Bool(Path(payload, CollectionTokens.TeamCommitContextRetainedAfterCommit)) != true)
            return false;

        JsonElement reasons = Property(payload, CollectionTokens.ChangeReasons);
        return capture == _acquisitionKind || reasons.ValueKind == JsonValueKind.Array && reasons.EnumerateArray().Any(value => value.GetString() == _evolutionKind);
    }

    /// <summary>
    /// Gets a state object for the actual current attempt.
    /// </summary>
    /// <param name="attempt">Authoritative attempt identity.</param>
    /// <returns>Bounded attempt working state.</returns>
    private JsonObject AttemptState(string? attempt)
    {
        if (attempt is null)
            return [];

        JsonObject attempts = StateMap(_attemptsState);
        if (attempts[attempt] is not JsonObject state)
        {
            state = [];
            attempts[attempt] = state;
        }

        return state;
    }

    /// <summary>
    /// Gets one named working-state map.
    /// </summary>
    /// <param name="key">State map key.</param>
    /// <returns>The mutable bounded map.</returns>
    private JsonObject StateMap(string key)
        => _state[key]!.AsObject();

    /// <summary>
    /// Creates empty release-onward working state.
    /// </summary>
    /// <returns>Empty maps without historical progress.</returns>
    private static JsonObject CreateState()
        => new() { [_battlesState] = new JsonObject(), [_actionsState] = new JsonObject(), [_groupsState] = new JsonObject(), [_attemptsState] = new JsonObject(), [_preferencesState] = new JsonObject(), [_familyState] = new JsonObject(), [_clockState] = new JsonObject() };

    /// <summary>
    /// Reads an item's active effect-group identity.
    /// </summary>
    /// <param name="envelope">Event envelope.</param>
    /// <param name="payload">Payload.</param>
    /// <returns>Actual group identifier, when supplied.</returns>
    private static string? GroupId(JsonElement envelope, JsonElement payload)
        => Text(Property(envelope, _contextKey), _groupKey) ?? Text(payload, _groupKey);

    /// <summary>
    /// Reads a string field without coercing null.
    /// </summary>
    /// <param name="value">Source object.</param>
    /// <param name="key">Field name.</param>
    /// <returns>String or null.</returns>
    private static string? Text(JsonElement value, string key)
        => Property(value, key) is JsonElement property && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    /// <summary>
    /// Reads a field or returns an explicit missing value.
    /// </summary>
    /// <param name="value">Source object.</param>
    /// <param name="key">Field name.</param>
    /// <returns>Field value or null.</returns>
    private static JsonElement Property(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement property) ? property : Null();

    /// <summary>
    /// Tests whether a selected field actually exists and is nonnull.
    /// </summary>
    /// <param name="value">Selected value.</param>
    /// <returns>Whether it is actual evidence.</returns>
    private static bool Available(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && (AvailabilityWrapper(value) is not string coverage || coverage == _known);

    /// <summary>
    /// Recognizes explicit typed availability wrappers without confusing ordinary metadata objects with values.
    /// </summary>
    /// <param name="value">Actual source field.</param>
    /// <returns>The explicit coverage class, when the source supplied a wrapper.</returns>
    private static string? AvailabilityWrapper(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(_coverageKey, out JsonElement coverage) || coverage.ValueKind != JsonValueKind.String || !value.TryGetProperty(_valueKey, out _) && !value.TryGetProperty(CollectionTokens.RuntimeClass, out _))
            return null;

        string? kind = coverage.GetString();
        return kind is _known or _unknown or CollectionTokens.Unsupported or CollectionTokens.NotApplicable ? kind : null;
    }

    /// <summary>
    /// Reads an exact integer, accepting decimal strings for wide wire values.
    /// </summary>
    /// <param name="value">Numeric or decimal string value.</param>
    /// <returns>Exact integer or null.</returns>
    private static BigInteger? Integer(JsonElement value)
    {
        string? text = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger integer) ? integer : null;
    }

    /// <summary>
    /// Reads a boolean without treating unknown as false.
    /// </summary>
    /// <param name="value">Boolean value.</param>
    /// <returns>Boolean or null.</returns>
    private static bool? Bool(JsonElement value)
        => value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null;

    /// <summary>
    /// Creates a wide exact integer JSON representation.
    /// </summary>
    /// <param name="value">Exact integer.</param>
    /// <returns>A decimal-string JSON value.</returns>
    private static JsonElement ExactInteger(BigInteger value)
        => JsonSerializer.SerializeToElement(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Creates an independent JSON element for durable state.
    /// </summary>
    /// <param name="node">Working-state node.</param>
    /// <returns>An independent element.</returns>
    private static JsonElement Element(JsonNode node)
        => JsonSerializer.SerializeToElement(node);

    /// <summary>
    /// Creates an explicit null selected value.
    /// </summary>
    /// <returns>JSON null.</returns>
    private static JsonElement Null()
        => JsonSerializer.Deserialize<JsonElement>(_nullJson);

    /// <summary>
    /// Compares exact scalar or structured values.
    /// </summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>Exact equality with integer string/number equivalence.</returns>
    private static bool Equal(JsonElement left, JsonElement right)
        => Integer(left) is BigInteger first && Integer(right) is BigInteger second ? first == second : left.GetRawText() == right.GetRawText();

    /// <summary>
    /// Merges actual current fields into bounded working context.
    /// </summary>
    /// <param name="destination">Current context.</param>
    /// <param name="source">New actual fields.</param>
    private static void Merge(JsonObject destination, JsonElement source)
    {
        foreach (JsonProperty property in source.EnumerateObject())
            destination[property.Name] = JsonNode.Parse(property.Value.GetRawText());
    }

    /// <summary>
    /// Adds one known exact amount, leaving missing amounts unavailable.
    /// </summary>
    /// <param name="destination">Current summary.</param>
    /// <param name="key">Amount key.</param>
    /// <param name="value">Actual amount.</param>
    private static void AddExact(JsonObject destination, string key, JsonElement value)
    {
        if (Integer(value) is BigInteger number)
            destination[key] = (ReadNodeBigInteger(destination[key]) + number).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Unions an actual exact member into a bounded effect summary.
    /// </summary>
    /// <param name="destination">Summary object.</param>
    /// <param name="key">Set key.</param>
    /// <param name="value">Actual member.</param>
    private static void AddMember(JsonObject destination, string key, JsonElement value)
    {
        if (!Available(value))
            return;

        JsonArray members;
        if (destination[key] is JsonArray existingMembers)
        {
            members = existingMembers;
        }
        else
        {
            members = [];
            destination[key] = members;
        }

        if (!members.Any(member => member is not null && Equal(Element(member), value)))
            members.Add(JsonNode.Parse(value.GetRawText()));

    }

    /// <summary>
    /// Reads a bounded sequence count from current state.
    /// </summary>
    /// <param name="node">Stored count.</param>
    /// <returns>Current integer count.</returns>
    private static int ReadNodeInteger(JsonNode? node)
        => node is null ? 0 : checked((int)ReadNodeBigInteger(node));

    /// <summary>
    /// Reads an exact accumulated amount from current state.
    /// </summary>
    /// <param name="node">Stored amount.</param>
    /// <returns>Current amount, with absent initial state treated as empty.</returns>
    private static BigInteger ReadNodeBigInteger(JsonNode? node)
        => node is null ? BigInteger.Zero : Integer(Element(node)) ?? BigInteger.Zero;

    /// <summary>
    /// Holds immutable actual source views for one routed row.
    /// </summary>
    /// <remarks>
    /// Initializes immutable envelope, payload, fanout-row and bounded battle/action/hit views for one projection.
    /// </remarks>
    /// <param name="Envelope">Receipt-qualified envelope.</param>
    /// <param name="Payload">Actual event payload.</param>
    /// <param name="Row">Actual correlated fanout member.</param>
    /// <param name="Battle">Current admitted battle context.</param>
    /// <param name="Action">Current execution context.</param>
    /// <param name="Hit">Current calculated hit context.</param>
    private sealed record Frame(JsonElement Envelope, JsonElement Payload, JsonElement Row, JsonElement Battle, JsonElement Action, JsonElement Hit);
}
