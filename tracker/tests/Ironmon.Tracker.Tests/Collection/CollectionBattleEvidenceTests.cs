using Ironmon.Tracker.Collection.Reduction;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Exercises compound conditions from source events rather than fabricated finalized summaries.
/// </summary>
public sealed partial class CollectionEventReducerTests
{
    private const string _entryType = "battler.entered";
    private const string _endType = "battle.ended";
    private const string _statusType = "status.major_application_resolved";
    private const string _faintType = "battler.fainted";
    private const string _hitType = "hit.resolved";
    private const string _attemptEndType = "attempt.completed";
    private const string _battleSource = """{"battle_kind":"trainer","eligibility":{"internal":true,"tutorial":false,"debug":false,"recorded_replay":false,"attempt_active":true}}""";
    private const string _entrySource = """{"actor":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"entry_kind":"initial","entry_snapshot":{"hp":20,"max_hp":20,"status_id":"NONE"}}""";
    private const string _poisonSource = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"status_requested":"POISON","status_after_application":"POISON","status_mode":"bad_poison","status_episode_id":"poison-one","application_outcome":"applied","effect_source":{"kind":"move","mechanic_id":"TOXIC","source_actor":{"owner_role":"opponent"}}}""";
    private const string _perishWinSource = """{"event_id":"end-one","engine_decision":1,"outcome":"win","final_actor_states":[{"actor":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp":1,"max_hp":20,"status_id":"POISON","active_effects":[{"effect_id":"PerishSong","remaining_value":1,"counter_unit":"phase_ticks"}]}],"terminal_side_states":[{"side":{"side_id":0},"engine_able_party_count":1,"reserve_able_count":0},{"side":{"side_id":1},"engine_able_party_count":0,"reserve_able_count":0}]}""";
    private const string _poisonLoss = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":20,"hp_after":0,"max_hp":20,"actual_hp_lost":20,"loss_kind":"residual","cause":{"kind":"status","mechanic_id":"POISON","status_episode_id":"poison-one","origin_instigator":{"owner_role":"opponent"}}}""";
    private const string _poisonFaint = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"terminal_hp_loss_ref":"4","cause":{"kind":"status","mechanic_id":"POISON","status_episode_id":"poison-one"}}""";
    private const string _defeatSource = """{"event_id":"end-one","engine_decision":2,"outcome":"loss","final_actor_states":[{"actor":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp":0,"max_hp":20,"status_id":"POISON","active_effects":[]}],"terminal_side_states":[{"side":{"side_id":0},"engine_able_party_count":0,"reserve_able_count":0},{"side":{"side_id":1},"engine_able_party_count":1,"reserve_able_count":0}]}""";
    private const string _firstHitLoss = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":20,"hp_after":10,"max_hp":20,"actual_hp_lost":10,"hit_ref":"old-hit","cause":{"kind":"move","mechanic_id":"TACKLE","origin_instigator":{"owner_role":"opponent"}}}""";
    private const string _firstHit = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hit_id":"old-hit","application_snapshot":{"pool":"pokemon","hp_before":20,"hp_after":10,"actual_pool_hp_lost":10,"critical":true,"mechanical_move_id":"TACKLE","calculation_kind":"formula"}}""";
    private const string _selfKoSource = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":10,"hp_after":0,"max_hp":20,"actual_hp_lost":10,"loss_kind":"self_ko","cause":{"kind":"move","mechanic_id":"EXPLOSION","origin_instigator":{"owner_role":"player"}}}""";
    private const string _selfKoFaint = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"terminal_hp_loss_ref":"5","cause":{"kind":"move","mechanic_id":"EXPLOSION"}}""";
    private const string _terminalAttemptSource = """{"result":"lost","terminal_battle_ref":"battle-one"}""";
    private const string _terminalEffects = "terminal_effect_outcome_joint";
    private const string _statusOutcomes = "status_episode_outcome_joint";
    private const string _effectRemaining = "remaining_value";
    private const string _faintRelation = "faint_relation";
    private const string _causedByEpisode = "caused_by_same_episode";
    private const string _terminalCauseGroup = "17ebf03b7b29";
    private const string _criticalDimension = "critical";
    private const string _mechanicalMoveDimension = "mechanical_move_id";
    private const string _explosion = "EXPLOSION";
    private const string _parentGroup = "parent-potion";
    private const string _childGroup = "child-berry";
    private const string _parentOpen = """{"effect_group_id":"parent-potion","kind":"item_use"}""";
    private const string _childOpen = """{"effect_group_id":"child-berry","kind":"held_item_activation","parent_effect_group_id":"parent-potion"}""";
    private const string _potionHealing = """{"recipient":{"pokemon_instance_id":"runner","appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":1,"hp_after":6,"max_hp":20,"actual_restored_hp":5,"capacity_excess_hp":0,"healing_amount_kind":"limited","effect_source":{"kind":"item","canonical_id":"POTION"}}""";
    private const string _berryHealing = """{"recipient":{"pokemon_instance_id":"runner","appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":6,"hp_after":16,"max_hp":20,"actual_restored_hp":10,"capacity_excess_hp":0,"healing_amount_kind":"limited","effect_source":{"kind":"item","canonical_id":"ORANBERRY"}}""";
    private const string _berryUse = """{"item_id":"ORANBERRY","use_context":"forced_effect","activation_kind":"own_held","consumed_quantity":1,"effect_succeeded":true,"outcome":"success"}""";
    private const string _potionUse = """{"item_id":"POTION","use_context":"bag_battle","consumed_quantity":1,"effect_succeeded":true,"outcome":"success"}""";
    private const string _floorLoss = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":20,"hp_after":1,"max_hp":20,"actual_hp_lost":19,"cause":{"kind":"move","origin_instigator":{"owner_role":"opponent"}}}""";
    private const string _recoveryGroup = "cd3c884a0a24";
    private const string _floorBeforeRecovery = "floor_before_recovery";
    private const string _observedFloor = "observed_floor_hp";
    private const string _priorityActionType = "action.started";
    private const string _priorityActionId = "priority-final";
    private const string _prioritySource = """{"actor":{"appearance_id":"enemy","owner_role":"opponent"},"priority_context":{"effective_priority":1,"base_priority":1}}""";
    private const string _priorityLoss = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":20,"hp_after":0,"max_hp":20,"actual_hp_lost":20,"hit_ref":"final-hit","loss_kind":"move_hit","cause":{"kind":"move","mechanic_id":"QUICKATTACK","origin_instigator":{"owner_role":"opponent"}}}""";
    private const string _priorityHit = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hit_id":"final-hit","application_snapshot":{"pool":"pokemon","hp_before":20,"hp_after":0,"actual_pool_hp_lost":20,"hp_before_equals_max":true,"critical":true,"mechanical_move_id":"QUICKATTACK","calculation_kind":"formula"}}""";
    private const string _priorityFaint = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"terminal_hp_loss_ref":"4","cause":{"kind":"move","mechanic_id":"QUICKATTACK"}}""";
    private const string _enemyEntry = """{"actor":{"appearance_id":"enemy","owner_role":"opponent"},"entry_kind":"initial","entry_snapshot":{"hp":1,"max_hp":20,"status_id":"NONE"}}""";
    private const string _rosterEnd = """{"event_id":"end-one","engine_decision":2,"outcome":"loss","final_actor_states":[{"actor":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp":0,"max_hp":20,"active_effects":[]},{"actor":{"appearance_id":"enemy","owner_role":"opponent"},"hp":1,"max_hp":20,"active_effects":[]}],"terminal_side_states":[{"side":{"side_id":0},"engine_able_party_count":0,"reserve_able_count":0},{"side":{"side_id":1},"engine_able_party_count":1,"reserve_able_count":0}]}""";
    private const string _rosterGroup = "8e378d68ce65";
    private const string _priorityDimension = "effective_priority";
    private const string _fullLossDimension = "single_hit_complete_loss";
    private const string _lastOpponentHpDimension = "terminal_surviving_opponent_hp";
    private const string _compoundFamily = "battle_compound_metrics";

    /// <summary>
    /// Verifies a real terminal counter creates directly queried Perish Song evidence after state recovery.
    /// </summary>
    [Fact]
    public void TerminalPerishCounterAndHealthSurviveReducerRecovery()
    {
        CollectionEventReducer original = NewReducer();
        original.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        original.Reduce(SourceEvent(_entryType, _entrySource, 2));
        CollectionEventReducer recovered = NewReducer();
        recovered.ImportState(original.ExportState());
        CollectionReduction completed = recovered.ReduceAll(SourceEvent(_endType, _perishWinSource, 3));
        CollectionProjection effect = Assert.Single(completed.Families, row => row.FamilyKey == _terminalEffects);
        CollectionDimensionValue remaining = Assert.Single(effect.Dimensions, row => row.Key == _effectRemaining);
        Assert.Equal(_known, remaining.Coverage);
        Assert.Equal(1, remaining.Value.GetInt32());
        CollectionProjection compound = Assert.Single(completed.Families, row => row.FamilyKey == _compoundFamily);
        Assert.Equal(System.Numerics.BigInteger.One, Assert.Single(compound.Measures).Scalar);
    }

    /// <summary>
    /// Checks the actual status episode causing the terminal loss remains linked through finalization.
    /// </summary>
    [Fact]
    public void PoisonEpisodeKeepsItsConfirmedFaintRelationship()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_statusType, _poisonSource, 3));
        reducer.Reduce(SourceEvent(_eventHp, _poisonLoss, 4));
        reducer.Reduce(SourceEvent(_faintType, _poisonFaint, 5));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_endType, _defeatSource, 6));
        CollectionProjection episode = Assert.Single(completed.Families, row => row.FamilyKey == _statusOutcomes);
        Assert.Equal(_causedByEpisode, Assert.Single(episode.Dimensions, value => value.Key == _faintRelation).Value.GetString());
    }

    /// <summary>
    /// Rejects borrowing an earlier critical hit when a later self-KO actually ended the run.
    /// </summary>
    [Fact]
    public void SelfKoDoesNotInheritAnEarlierHitsCriticalFlag()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_eventHp, _firstHitLoss, 3));
        reducer.Reduce(SourceEvent(_hitType, _firstHit, 4));
        reducer.Reduce(SourceEvent(_eventHp, _selfKoSource, 5));
        reducer.Reduce(SourceEvent(_faintType, _selfKoFaint, 6));
        reducer.Reduce(SourceEvent(_endType, _defeatSource, 7));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_attemptEndType, _terminalAttemptSource, 8));
        CollectionDirectProjection cause = Assert.Single(completed.Direct, row => row.GroupId == _terminalCauseGroup);
        Assert.Equal(_unknown, Assert.Single(cause.Dimensions, value => value.Key == _criticalDimension).Coverage);
        Assert.Equal(_explosion, Assert.Single(cause.Dimensions, value => value.Key == _mechanicalMoveDimension).Value.GetProperty(CollectionReducerTestTokens.Id).GetString());
    }

    /// <summary>
    /// Checks nested held recovery contributes once to inclusive Bag totals and preserves both immediate sources.
    /// </summary>
    [Fact]
    public void NestedBerryHealingKeepsDirectAndInclusiveTotalsDistinct()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(Event(_eventOpen, JsonSerializer.Deserialize<JsonElement>(_parentOpen), 1, new { effect_group_id = _parentGroup }));
        reducer.Reduce(Event(_eventHealing, JsonSerializer.Deserialize<JsonElement>(_potionHealing), 2, new { effect_group_id = _parentGroup }));
        reducer.Reduce(Event(_eventOpen, JsonSerializer.Deserialize<JsonElement>(_childOpen), 3, new { effect_group_id = _childGroup }));
        reducer.Reduce(Event(_eventHealing, JsonSerializer.Deserialize<JsonElement>(_berryHealing), 4, new { effect_group_id = _childGroup }));
        reducer.Reduce(Event(_eventItem, JsonSerializer.Deserialize<JsonElement>(_berryUse), 5, new { effect_group_id = _childGroup }));
        reducer.Reduce(Event(_eventClose, new { effect_group_id = _childGroup }, 6, new { effect_group_id = _childGroup }));
        reducer.Reduce(Event(_eventItem, JsonSerializer.Deserialize<JsonElement>(_potionUse), 7, new { effect_group_id = _parentGroup }));
        CollectionProjection hp = Assert.Single(reducer.Reduce(Event(_eventClose, new { effect_group_id = _parentGroup }, 8, new { effect_group_id = _parentGroup })), row => row.FamilyKey == _familyItemHp);
        Assert.Equal(new System.Numerics.BigInteger(5), Assert.Single(hp.Measures, row => row.Key == CollectionReducerTestTokens.RecipientEffectsHpDirectItemTotal).Scalar);
        Assert.Equal(new System.Numerics.BigInteger(10), Assert.Single(hp.Measures, row => row.Key == CollectionReducerTestTokens.RecipientEffectsHpSecondaryTotal).Scalar);
        Assert.Equal(new System.Numerics.BigInteger(15), Assert.Single(hp.Measures, row => row.Key == CollectionReducerTestTokens.RecipientEffectsHpActualTotal).Scalar);
    }

    /// <summary>
    /// Checks the directly queried recovery relationship retains actual sequence order and a typed Boolean.
    /// </summary>
    [Fact]
    public void RecoveredOneHpFloorProducesAnIndexedOrderedJoint()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_eventHp, _floorLoss, 3));
        reducer.Reduce(SourceEvent(_eventHealing, _potionHealing, 4));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_endType, _perishWinSource, 5));
        CollectionDirectProjection recovery = Assert.Single(completed.Direct, row => row.GroupId == _recoveryGroup);
        Assert.True(Assert.Single(recovery.Dimensions, value => value.Key == _floorBeforeRecovery).Value.GetBoolean());
        Assert.Equal(1, int.Parse(Assert.Single(recovery.Dimensions, value => value.Key == _observedFloor).Value.GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Preserves the actual lethal hit priority, full-health critical loss and surviving last opponent together.
    /// </summary>
    [Fact]
    public void TerminalPriorityAndLastOpponentHealthRemainDirectlyQueryable()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        object context = new { action_id = _priorityActionId };
        reducer.Reduce(Event(_priorityActionType, JsonSerializer.Deserialize<JsonElement>(_prioritySource), 3, context));
        reducer.Reduce(Event(_eventHp, JsonSerializer.Deserialize<JsonElement>(_priorityLoss), 4, context));
        reducer.Reduce(Event(_hitType, JsonSerializer.Deserialize<JsonElement>(_priorityHit), 5, context));
        reducer.Reduce(SourceEvent(_faintType, _priorityFaint, 6));
        reducer.Reduce(SourceEvent(_entryType, _enemyEntry, 7));
        reducer.Reduce(SourceEvent(_endType, _rosterEnd, 8));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_attemptEndType, _terminalAttemptSource, 9));
        CollectionDirectProjection cause = Assert.Single(completed.Direct, row => row.GroupId == _terminalCauseGroup);
        Assert.Equal(1, Assert.Single(cause.Dimensions, dimension => dimension.Key == _priorityDimension).Value.GetInt32());
        Assert.True(Assert.Single(cause.Dimensions, dimension => dimension.Key == _criticalDimension).Value.GetBoolean());
        Assert.True(Assert.Single(cause.Dimensions, dimension => dimension.Key == _fullLossDimension).Value.GetBoolean());
        CollectionDirectProjection roster = Assert.Single(completed.Direct, row => row.GroupId == _rosterGroup);
        Assert.Equal(1, Assert.Single(roster.Dimensions, dimension => dimension.Key == _lastOpponentHpDimension).Value.GetInt32());
    }

    /// <summary>
    /// Creates an ordered source envelope from a native-shaped payload.
    /// </summary>
    /// <param name="type">The native gameplay event type.</param>
    /// <param name="payload">The JSON source payload using actual producer field names.</param>
    /// <param name="sequence">The unique ordered sequence assigned to this fixture occurrence.</param>
    /// <returns>The ordered immutable fixture envelope with the native-shaped source payload.</returns>
    private static JsonElement SourceEvent(string type, string payload, int sequence)
        => Event(type, JsonSerializer.Deserialize<JsonElement>(payload), sequence);
}
