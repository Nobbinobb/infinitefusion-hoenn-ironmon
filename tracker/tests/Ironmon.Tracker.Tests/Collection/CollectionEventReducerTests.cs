using Ironmon.Tracker.Collection.Reduction;
using System.Numerics;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies actual field routing and causal counting without fabricated outcomes.
/// </summary>
public sealed partial class CollectionEventReducerTests
{
    private const string _attemptId = "attempt-one";
    private const string _battleId = "battle-one";
    private const string _groupId = "item-group";
    private const string _familyDamage = "actual_hp_lost";
    private const string _familySuccessfulMoves = "successful_move_ids";
    private const string _familyCalls = "called_move_outcomes";
    private const string _familyTime = "time.seconds";
    private const string _familyStats = "owned.cumulative_generated_base_stats";
    private const string _familyItemHp = "item_activation_hp_by_context";
    private const string _familyFavorites = "runner.favorite_obtainments";
    private const string _familyFavoriteSlots = "runner.favorite_slot_occurrences";
    private const string _familyFavoriteRepresentations = "runner.favorite_obtainments_by_favorite";
    private const string _known = "known";
    private const string _unknown = "unknown";
    private const string _eventHp = "hp.loss_applied";
    private const string _eventMove = "move.finished";
    private const string _eventClock = "clock.interval_closed";
    private const string _eventOwned = "pokemon.owned_configuration_committed";
    private const string _eventRunner = "pokemon.runner_changed";
    private const string _eventFavorites = "pokemon.favorite_selection_changed";
    private const string _eventOpen = "collection.effect_group_opened";
    private const string _eventClose = "collection.effect_group_closed";
    private const string _eventItem = "item.use_resolved";
    private const string _eventHealing = "hp.healing_resolved";
    private const string _eventBattle = "battle.started";
    private const string _eventExecute = "move.executed";
    private const string _eventAction = "action.started";
    private const string _player = "player";
    private const string _opponent = "opponent";
    private const string _directInvocation = "direct";
    private const string _callInvocation = "move_call";
    private const string _effectResult = "executed_effect";
    private const string _noEffectResult = "executed_no_effect";
    private const string _metronome = "METRONOME";
    private const string _thunderbolt = "THUNDERBOLT";
    private const string _electricType = "ELECTRIC";
    private const string _normalType = "NORMAL";
    private const string _emptyObject = "{}";

    /// <summary>
    /// Checks all source routes and generic summary declarations remain present.
    /// </summary>
    [Fact]
    public void CatalogueRetainsAllGenericContracts()
    {
        CollectionCatalogue catalogue = CollectionCatalogue.LoadDefault();
        Assert.Equal(258, catalogue.Families.Count);
        Assert.Equal(103, catalogue.Events.Count);
        Assert.Equal(84, catalogue.DirectGroups.Count);
        Assert.Equal(19, catalogue.Relationships.Count);
        Assert.Equal(Enumerable.Range(1, 258), catalogue.Families.Select(family => family.Id));
        Assert.All(catalogue.Events, value => Assert.NotEmpty(catalogue.ForEvent(value.GetProperty(CollectionReducerTestTokens.Key).GetString()!)));
        Assert.DoesNotContain(CollectionReducerTestTokens.Achievement, catalogue.Definition.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks missing factual operands never create known zero damage.
    /// </summary>
    [Fact]
    public void MissingDamageIsUnknownRatherThanZero()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionProjection damage = Assert.Single(reducer.Reduce(Event(_eventHp, new { target = new { owner_role = _opponent } }, 1)), value => value.FamilyKey == _familyDamage);
        CollectionMeasureUpdate measure = Assert.Single(damage.Measures);
        Assert.Equal(_unknown, measure.Coverage);
        Assert.Null(measure.Scalar);
    }

    /// <summary>
    /// Checks the source's explicit unsupported class is retained rather than converted into missing or known zero.
    /// </summary>
    [Fact]
    public void ExplicitUnsupportedFieldRetainsItsCoverageClass()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionProjection damage = Assert.Single(reducer.Reduce(Event(_eventHp, new { actual_hp_lost = new { coverage = CollectionReducerTestTokens.Unsupported, runtime_class = CollectionReducerTestTokens.UnsupportedNumeric }, target = new { owner_role = _opponent } }, 1)), value => value.FamilyKey == _familyDamage);
        CollectionMeasureUpdate measure = Assert.Single(damage.Measures);
        Assert.Equal(CollectionReducerTestTokens.Unsupported, measure.Coverage);
        Assert.Null(measure.Scalar);
    }

    /// <summary>
    /// Checks actual wide amounts remain exact.
    /// </summary>
    [Fact]
    public void WideDamagePreservesExactMagnitudeAndSource()
    {
        CollectionEventReducer reducer = NewReducer();
        string amount = (BigInteger.One << 100).ToString();
        CollectionProjection damage = Assert.Single(reducer.Reduce(Event(_eventHp, new { actual_hp_lost = amount, target = new { owner_role = _opponent }, cause = new { kind = _directInvocation, mechanic_id = _thunderbolt, origin_instigator = new { owner_role = _player } } }, 1)), value => value.FamilyKey == _familyDamage);
        Assert.Equal(BigInteger.One << 100, Assert.Single(damage.Measures).Scalar);
        Assert.Equal(_known, Assert.Single(damage.Measures).Coverage);
        Assert.Equal(_player, damage.Dimensions.Single(value => value.Key == CollectionReducerTestTokens.SourceOwnerRole).Value.GetString());
    }

    /// <summary>
    /// Checks that misses retain mechanical observations without successful-use credit.
    /// </summary>
    [Fact]
    public void FailedMoveDoesNotGrantSuccessfulUsage()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionReduction result = reducer.ReduceAll(Event(_eventMove, Move(_thunderbolt, _noEffectResult, _directInvocation), 1));
        Assert.DoesNotContain(result.Families, value => value.FamilyKey == _familySuccessfulMoves);
        Assert.Contains(result.Families, value => value.FamilyKey == CollectionReducerTestTokens.MoveResults);
    }

    /// <summary>
    /// Checks direct successful moves retain their actual identity and type.
    /// </summary>
    [Fact]
    public void SuccessfulMoveUsesActualIdentity()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionProjection usage = Assert.Single(reducer.Reduce(Event(_eventMove, Move(_thunderbolt, _effectResult, _directInvocation), 1)), value => value.FamilyKey == _familySuccessfulMoves);
        Assert.Equal(_thunderbolt, Assert.Single(Assert.Single(usage.Measures).Members!).GetString());
    }

    /// <summary>
    /// Checks called moves retain call-frequency evidence without taking wrapper usage credit.
    /// </summary>
    [Fact]
    public void CalledMoveDoesNotReplaceMetronomeUsageIdentity()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(_eventAction, new { actor = new { owner_role = _player } }, 1, new { action_id = CollectionReducerTestTokens.Parent }));
        reducer.ReduceAll(Event(_eventExecute, new { move_id = _thunderbolt, actor = new { owner_role = _player } }, 2, new { action_id = CollectionReducerTestTokens.Child, parent_action_id = CollectionReducerTestTokens.Parent }));
        CollectionReduction child = reducer.ReduceAll(Event(_eventMove, Move(_thunderbolt, _effectResult, _callInvocation), 3, new { action_id = CollectionReducerTestTokens.Child, parent_action_id = CollectionReducerTestTokens.Parent }));
        Assert.DoesNotContain(child.Families, value => value.FamilyKey == _familySuccessfulMoves);
        CollectionProjection calls = Assert.Single(child.Families, value => value.FamilyKey == _familyCalls);
        Assert.Equal(BigInteger.One, calls.Measures.Single(value => value.Key == CollectionReducerTestTokens.CallCount).Scalar);
        CollectionProjection parent = Assert.Single(reducer.Reduce(Event(_eventMove, Move(_metronome, CollectionReducerTestTokens.Delegated, _directInvocation), 4, new { action_id = CollectionReducerTestTokens.Parent })), value => value.FamilyKey == _familySuccessfulMoves);
        Assert.Equal(_metronome, Assert.Single(Assert.Single(parent.Measures).Members!).GetString());
    }

    /// <summary>
    /// Checks exact decimal tails survive a durable reducer reopen.
    /// </summary>
    [Fact]
    public void SubnanosecondClockRemainderSurvivesReopen()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionProjection first = Assert.Single(reducer.Reduce(Event(_eventClock, new { duration_seconds = CollectionReducerTestTokens.Value00000000006, clock_domain = CollectionReducerTestTokens.GameLoadedSaveElapsed, activity_state = CollectionReducerTestTokens.Playing }, 1)), value => value.FamilyKey == _familyTime);
        Assert.Equal(BigInteger.Zero, Assert.Single(first.Measures).Scalar);
        JsonElement checkpoint = reducer.ExportState();
        CollectionEventReducer reopened = NewReducer();
        reopened.ImportState(checkpoint);
        CollectionProjection second = Assert.Single(reopened.Reduce(Event(_eventClock, new { duration_seconds = CollectionReducerTestTokens.Value00000000006, clock_domain = CollectionReducerTestTokens.GameLoadedSaveElapsed, activity_state = CollectionReducerTestTokens.Playing }, 2)), value => value.FamilyKey == _familyTime);
        Assert.Equal(BigInteger.One, Assert.Single(second.Measures).Scalar);
    }

    /// <summary>
    /// Checks actual item effects can precede final item result and survive reopen.
    /// </summary>
    [Fact]
    public void ItemActivationWaitsForAllRecipientEffects()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(_eventOpen, new { effect_group_id = _groupId, kind = CollectionReducerTestTokens.Item }, 1));
        reducer.ReduceAll(Event(_eventHealing, new { recipient = new { pokemon_instance_id = CollectionReducerTestTokens.RunnerOne, owner_role = _player, side = _player }, effect_source = new { kind = CollectionReducerTestTokens.Item, canonical_id = CollectionReducerTestTokens.POTION }, actual_restored_hp = 18, capacity_excess_hp = 2, healing_amount_kind = CollectionReducerTestTokens.Limited }, 2, new { effect_group_id = _groupId }));
        CollectionReduction item = reducer.ReduceAll(Event(_eventItem, new { item_id = CollectionReducerTestTokens.POTION, use_context = CollectionReducerTestTokens.Bag, effect_succeeded = true, outcome = CollectionReducerTestTokens.Success, consumed_quantity = 1 }, 3, new { effect_group_id = _groupId }));
        Assert.DoesNotContain(item.Families, value => value.FamilyKey == _familyItemHp);
        CollectionEventReducer reopened = NewReducer();
        reopened.ImportState(reducer.ExportState());
        CollectionReduction closed = reopened.ReduceAll(Event(_eventClose, new { effect_group_id = _groupId, last_member_sequence = CollectionReducerTestTokens.Value3, completion_kind = CollectionReducerTestTokens.Complete, outcome = CollectionReducerTestTokens.Success }, 4));
        CollectionProjection hp = Assert.Single(closed.Families, value => value.FamilyKey == _familyItemHp);
        Assert.Equal(new BigInteger(18), hp.Measures.Single(value => value.Key == CollectionReducerTestTokens.RecipientEffectsHpActualTotal).Scalar);
        Assert.Equal(new BigInteger(18), hp.Measures.Single(value => value.Key == CollectionReducerTestTokens.RecipientEffectsHpDirectItemTotal).Scalar);
        Assert.Empty(reopened.ExportState().GetProperty(CollectionReducerTestTokens.Groups).EnumerateObject());
    }

    /// <summary>
    /// Checks favorite fusion counts distinguish obtainments from head and body slots.
    /// </summary>
    [Fact]
    public void FavoriteFusionCountsOneObtainmentAndTwoMatchingSlots()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(_eventFavorites, new { normal_species_ids = new[] { 25 }, persisted = true, effective = true, operation = CollectionReducerTestTokens.Replace }, 1));
        CollectionReduction result = reducer.ReduceAll(Event(_eventRunner, new { cause_kind = CollectionReducerTestTokens.Acquisition, species_after_ref = new { canonical_id = CollectionReducerTestTokens.Value2525, fusion_body_id = 25, fusion_head_id = 25 } }, 2));
        Assert.Equal(BigInteger.One, Assert.Single(result.Families, value => value.FamilyKey == _familyFavorites).Measures.Single().Scalar);
        Assert.Single(result.Families, value => value.FamilyKey == _familyFavoriteRepresentations);
        Assert.Equal(2, result.Families.Count(value => value.FamilyKey == _familyFavoriteSlots));
    }

    /// <summary>
    /// Checks acquisition and evolution add full vectors, while ordinary level changes do not.
    /// </summary>
    [Fact]
    public void OwnedStatContributionsFollowRetainedGameplayCommits()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionProjection initial = Assert.Single(reducer.Reduce(Event(_eventOwned, Owned(CollectionReducerTestTokens.Acquisition, [CollectionReducerTestTokens.Starter]), 1)), value => value.FamilyKey == _familyStats);
        Assert.Equal(new BigInteger(600), initial.Measures.Single(value => value.Key == CollectionReducerTestTokens.SumGeneratedBst).Scalar);
        Assert.DoesNotContain(reducer.Reduce(Event(_eventOwned, Owned(CollectionReducerTestTokens.ConfigurationChange, [CollectionReducerTestTokens.Level]), 2)), value => value.FamilyKey == _familyStats);
        CollectionProjection evolved = Assert.Single(reducer.Reduce(Event(_eventOwned, Owned(CollectionReducerTestTokens.ConfigurationChange, [CollectionReducerTestTokens.Evolution]), 3)), value => value.FamilyKey == _familyStats);
        Assert.Equal(new BigInteger(600), evolved.Measures.Single(value => value.Key == CollectionReducerTestTokens.SumGeneratedBst).Scalar);
        Assert.Empty(reducer.Reduce(Event(_eventOwned, Owned(CollectionReducerTestTokens.FirstObservedBaseline, [CollectionReducerTestTokens.Load]), 4)));
    }

    /// <summary>
    /// Checks known empty sets are distinct from unavailable sets.
    /// </summary>
    [Fact]
    public void KnownEmptyIdentitySetRemainsKnown()
    {
        CollectionProjection types = Assert.Single(NewReducer().Reduce(Event(CollectionReducerTestTokens.BattlerIdentityChanged, new { actor = new { owner_role = _player }, change_kind = CollectionReducerTestTokens.Type, after = new { type_ids = Array.Empty<string>() } }, 1)), value => value.FamilyKey == CollectionReducerTestTokens.ObservedCombatTypes);
        CollectionMeasureUpdate update = Assert.Single(types.Measures);
        Assert.Equal(_known, update.Coverage);
        Assert.Empty(update.Members!);
    }

    /// <summary>
    /// Checks every declared source can reduce an incomplete record with explicit coverage.
    /// </summary>
    [Fact]
    public void AllEventRoutesHandleIncompleteEvidenceWithoutThrowing()
    {
        CollectionCatalogue catalogue = CollectionCatalogue.LoadDefault();
        CollectionEventReducer reducer = new(catalogue);
        int sequence = 0;
        foreach (JsonElement definition in catalogue.Events)
        {
            CollectionReduction reduction = reducer.ReduceAll(Event(definition.GetProperty(CollectionReducerTestTokens.Key).GetString()!, JsonSerializer.Deserialize<JsonElement>(_emptyObject), ++sequence));
            Assert.All(reduction.Families.SelectMany(family => family.Measures).Where(measure => measure.Coverage == _unknown), measure => Assert.Null(measure.Scalar));
            Assert.All(reduction.Direct, summary => Assert.Equal(catalogue.DirectGroups.Single(group => group.GetProperty(CollectionReducerTestTokens.Id).GetString() == summary.GroupId).GetProperty(CollectionReducerTestTokens.Dimensions).GetArrayLength(), summary.Dimensions.Count));
            Assert.All(reduction.Direct.SelectMany(summary => summary.Dimensions).Where(dimension => dimension.Coverage == _known), AssertTypedDimension);
        }
    }

    /// <summary>
    /// Checks actual terminal battle facts produce summaries and release bounded working state.
    /// </summary>
    [Fact]
    public void BattleEndedFinalizesActualActorSummaryWithoutSyntheticGameEvent()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(_eventBattle, new { battle_kind = CollectionReducerTestTokens.Trainer, encounter = new { authored_entry_ids = new[] { CollectionReducerTestTokens.OpeningRival } } }, 1));
        object player = new { appearance_id = CollectionReducerTestTokens.PlayerAppearance, pokemon_instance_id = CollectionReducerTestTokens.RunnerOne, owner_role = _player };
        object opponent = new { appearance_id = CollectionReducerTestTokens.OpponentAppearance, pokemon_instance_id = CollectionReducerTestTokens.EnemyOne, owner_role = _opponent };
        reducer.ReduceAll(Event(CollectionReducerTestTokens.BattlerEntered, new { actor = player, entry_kind = CollectionReducerTestTokens.Initial, entry_snapshot = new { hp = 20, max_hp = 20 } }, 2));
        reducer.ReduceAll(Event(CollectionReducerTestTokens.BattlerEntered, new { actor = opponent, entry_kind = CollectionReducerTestTokens.Initial, entry_snapshot = new { hp = 30, max_hp = 30 } }, 3));
        reducer.ReduceAll(Event(_eventHp, new { target = opponent, hp_before = 30, hp_after = 0, max_hp = 30, actual_hp_lost = 30, loss_kind = CollectionReducerTestTokens.MoveHit, cause = new { kind = CollectionReducerTestTokens.Move, mechanic_id = _thunderbolt, origin_instigator = player } }, 4));
        CollectionReduction ended = reducer.ReduceAll(Event(CollectionReducerTestTokens.BattleEnded, new { outcome = CollectionReducerTestTokens.Win, end_reason = CollectionReducerTestTokens.Judgement }, 5));
        Assert.Contains(ended.Families, family => family.FamilyKey == CollectionReducerTestTokens.BattleBestValues && family.Measures.Any(measure => measure.Key == CollectionReducerTestTokens.DamageDealtMax && measure.Scalar == 30));
        Assert.Empty(reducer.ExportState().GetProperty(CollectionReducerTestTokens.Battles).EnumerateObject());
        Assert.Empty(reducer.ExportState().GetProperty(CollectionReducerTestTokens.Actions).EnumerateObject());
        Assert.All(ended.Direct.SelectMany(summary => summary.Dimensions).Where(dimension => dimension.Coverage == _known), AssertTypedDimension);
    }

    /// <summary>
    /// Checks a reopened interrupted stream retires unfinished joins without fabricating a victory.
    /// </summary>
    [Fact]
    public void InterruptedPredecessorDoesNotCompleteAnUnfinishedBattle()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(_eventBattle, new { battle_kind = CollectionReducerTestTokens.Trainer }, 1));
        CollectionEventReducer reopened = NewReducer();
        reopened.ImportState(reducer.ExportState());
        CollectionReduction checkpoint = reopened.ReduceAll(Event(CollectionReducerTestTokens.CollectionContextCheckpoint, new { retirement_kind = CollectionReducerTestTokens.RecoveredInterrupted, predecessor_stream_id = CollectionReducerTestTokens.PriorStream, predecessor_final_sequence = CollectionReducerTestTokens.Value1 }, 2));
        Assert.Contains(checkpoint.Families, family => family.FamilyKey == CollectionReducerTestTokens.InterruptedBattles);
        Assert.DoesNotContain(checkpoint.Families, family => family.FamilyKey == CollectionReducerTestTokens.AuthoredVictories || family.FamilyKey == CollectionReducerTestTokens.BattleOutcomes);
        Assert.Empty(reopened.ExportState().GetProperty(CollectionReducerTestTokens.Battles).EnumerateObject());
    }

    /// <summary>
    /// Checks peaks use exact same-scope totals and include real and decoy damage before battle completion.
    /// </summary>
    [Fact]
    public void DamagePeaksCombineDisjointPoolsWithinTheActualBattle()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(_eventBattle, new { battle_kind = CollectionReducerTestTokens.Trainer }, 1));
        object target = new { owner_role = _opponent };
        object cause = new { origin_instigator = new { owner_role = _player } };
        CollectionReduction real = reducer.ReduceAll(Event(_eventHp, new { target, cause, actual_hp_lost = 30 }, 2));
        Assert.Equal(new BigInteger(30), Assert.Single(real.Direct, summary => summary.GroupId == CollectionReducerTestTokens.Value77d1e92c15e9).Measures.Single().Scalar);
        CollectionReduction decoy = reducer.ReduceAll(Event(CollectionReducerTestTokens.SubstituteChanged, new { target, cause, actual_substitute_hp_lost = 12, operation = CollectionReducerTestTokens.Damaged }, 3));
        Assert.Equal(new BigInteger(42), Assert.Single(decoy.Direct, summary => summary.GroupId == CollectionReducerTestTokens.Value77d1e92c15e9).Measures.Single().Scalar);
        Assert.Equal(new BigInteger(42), Assert.Single(decoy.Direct, summary => summary.GroupId == CollectionReducerTestTokens.Value24a5d2d32187).Measures.Single().Scalar);
        CollectionReduction won = reducer.ReduceAll(Event(CollectionReducerTestTokens.BattleEnded, new { outcome = CollectionReducerTestTokens.Win }, 4));
        Assert.Equal(BigInteger.One, Assert.Single(won.Direct, summary => summary.GroupId == CollectionReducerTestTokens.Value4d366754f7e3).Measures.Single().Scalar);
    }

    /// <summary>
    /// Checks post-release ownership excludes historical counts, preserves categories and resumes before actual removals.
    /// </summary>
    [Fact]
    public void WardrobeOwnershipTracksEligibleCurrentSetsAcrossRestart()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionReduction added = reducer.ReduceAll(Event(CollectionReducerTestTokens.WardrobeOwnershipChanged, new { cosmetic_profile_ref = CollectionReducerTestTokens.ProfileOne, added_piece_keys = new[] { CollectionReducerTestTokens.ClothesNew, CollectionReducerTestTokens.HatNew }, removed_piece_keys = Array.Empty<string>(), ownership_source = CollectionReducerTestTokens.Purchase, owned_counts_after = new { clothes = 100, hat = 50 } }, 1));
        CollectionProjection total = Assert.Single(added.Families, family => family.FamilyKey == CollectionReducerTestTokens.WardrobeOwnedPieceCount && family.Dimensions.Any(dimension => dimension.Key == CollectionReducerTestTokens.Category && dimension.Value.GetString() == CollectionReducerTestTokens.All));
        Assert.Equal(new BigInteger(2), total.Measures.Single().Scalar);
        Assert.All(added.Direct.Where(summary => summary.GroupId == CollectionReducerTestTokens.Value61e50dbfa1ca), summary => Assert.All(summary.Measures, measure => Assert.Equal(CollectionReducerTestTokens.Known, measure.Coverage)));
        CollectionEventReducer reopened = NewReducer();
        reopened.ImportState(reducer.ExportState());
        CollectionReduction removed = reopened.ReduceAll(Event(CollectionReducerTestTokens.WardrobeOwnershipChanged, new { cosmetic_profile_ref = CollectionReducerTestTokens.ProfileOne, added_piece_keys = Array.Empty<string>(), removed_piece_keys = new[] { CollectionReducerTestTokens.HatNew, CollectionReducerTestTokens.ClothesHistorical }, ownership_source = CollectionReducerTestTokens.ProfileEdit }, 2));
        CollectionProjection hats = Assert.Single(removed.Families, family => family.FamilyKey == CollectionReducerTestTokens.WardrobeOwnedPieceCount && family.Dimensions.Any(dimension => dimension.Key == CollectionReducerTestTokens.Category && dimension.Value.GetString() == CollectionReducerTestTokens.Hat));
        Assert.Equal(BigInteger.Zero, hats.Measures.Single().Scalar);
        CollectionProjection remaining = Assert.Single(removed.Families, family => family.FamilyKey == CollectionReducerTestTokens.WardrobeOwnedPieceCount && family.Dimensions.Any(dimension => dimension.Key == CollectionReducerTestTokens.Category && dimension.Value.GetString() == CollectionReducerTestTokens.All));
        Assert.Equal(BigInteger.One, remaining.Measures.Single().Scalar);
    }

    /// <summary>
    /// Checks confirmed appearance counts and every actual dye value are directly indexed without unknown synthetic dimensions.
    /// </summary>
    [Fact]
    public void WardrobeConfirmationRetainsEachDyeAndJointFeatures()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionReduction result = reducer.ReduceAll(Event(CollectionReducerTestTokens.WardrobeAppearanceConfirmationResolved, new { outcome = CollectionReducerTestTokens.Success, initial_free_claim = false, cosmetic_profile_ref = CollectionReducerTestTokens.ProfileOne, appearance_after = new { outfit_key = CollectionReducerTestTokens.ClothesOne, hair_style_key = CollectionReducerTestTokens.HairTwo, hair_version = 1, distinct_accessory_count = 0, non_default_dye_fields = new[] { CollectionReducerTestTokens.ClothesColor }, dye_reference_version = CollectionReducerTestTokens.NativeHueZero1, confirmed_dye_values = new[] { new { field = CollectionReducerTestTokens.ClothesColor, value = 20 }, new { field = CollectionReducerTestTokens.HairColor, value = 0 } }, canonical_piece_keys = new[] { CollectionReducerTestTokens.ClothesOne, CollectionReducerTestTokens.HairTwo } } }, 1));
        CollectionProjection joint = Assert.Single(result.Families, family => family.FamilyKey == CollectionReducerTestTokens.WardrobeConfirmedAppearanceJoint);
        Assert.Equal(BigInteger.One, joint.Measures.Single().Scalar);
        CollectionDirectProjection[] dyes = [.. result.Direct.Where(summary => summary.GroupId == CollectionReducerTestTokens.Value9a4892d3bb9e)];
        Assert.Equal(2, dyes.Length);
        Assert.All(dyes, summary => Assert.All(summary.Dimensions, dimension => Assert.Equal(CollectionReducerTestTokens.Known, dimension.Coverage)));
        Assert.Contains(dyes, summary => summary.Dimensions.Any(dimension => dimension.Key == CollectionReducerTestTokens.DyeValue && dimension.Value.GetInt32() == 20));
    }

    /// <summary>
    /// Checks a newly registered lot delta never masquerades as an authoritative current inventory balance.
    /// </summary>
    [Fact]
    public void RegisteredLotQuantitiesDoNotReplaceUnknownCurrentBalances()
    {
        CollectionEventReducer reducer = NewReducer();
        CollectionReduction first = reducer.ReduceAll(Event(CollectionReducerTestTokens.ItemProvenanceRegistered, new { item_id = CollectionReducerTestTokens.POTION, quantity = 5, acquisition_origin = CollectionReducerTestTokens.Found, current_controller = CollectionReducerTestTokens.Bag }, 1));
        CollectionReduction second = reducer.ReduceAll(Event(CollectionReducerTestTokens.ItemProvenanceRegistered, new { item_id = CollectionReducerTestTokens.POTION, quantity = 2, acquisition_origin = CollectionReducerTestTokens.Found, current_controller = CollectionReducerTestTokens.Bag }, 2));
        Assert.All(new[] { first, second }, reduction => Assert.Null(Assert.Single(reduction.Families, family => family.FamilyKey == CollectionReducerTestTokens.ActiveResourceOriginBalance).Measures.Single().Scalar));
        CollectionReduction known = reducer.ReduceAll(Event(CollectionReducerTestTokens.ItemProvenanceRegistered, new { item_id = CollectionReducerTestTokens.POTION, quantity = 2, acquisition_origin = CollectionReducerTestTokens.Found, current_controller = CollectionReducerTestTokens.Bag, active_resource_lots = new { quantity = 7 } }, 3));
        Assert.Equal(new BigInteger(7), Assert.Single(known.Families, family => family.FamilyKey == CollectionReducerTestTokens.ActiveResourceOriginBalance).Measures.Single().Scalar);
    }

    /// <summary>
    /// Checks initiating Bag healing stays distinct from a secondary held berry, with exact source amounts retained after restart.
    /// </summary>
    [Fact]
    public void BagItemAndSecondaryBerryKeepSeparateHealingSourceViews()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(CollectionReducerTestTokens.CollectionEffectGroupOpened, new { effect_group_id = CollectionReducerTestTokens.ItemSourceGroup, kind = CollectionReducerTestTokens.ItemUse }, 1));
        object recipient = new { pokemon_instance_id = CollectionReducerTestTokens.RunnerOne, owner_role = CollectionReducerTestTokens.Player, side = CollectionReducerTestTokens.Player };
        reducer.ReduceAll(Event(CollectionReducerTestTokens.HpHealingResolved, new { recipient, effect_source = new { kind = CollectionReducerTestTokens.Item, canonical_id = CollectionReducerTestTokens.POTION }, actual_restored_hp = 18, capacity_excess_hp = 2, healing_amount_kind = CollectionReducerTestTokens.Limited }, 2, new { effect_group_id = CollectionReducerTestTokens.ItemSourceGroup }));
        reducer.ReduceAll(Event(CollectionReducerTestTokens.HpHealingResolved, new { recipient, effect_source = new { kind = CollectionReducerTestTokens.Item, canonical_id = CollectionReducerTestTokens.SITRUSBERRY }, actual_restored_hp = 30, capacity_excess_hp = 0, healing_amount_kind = CollectionReducerTestTokens.Limited }, 3, new { effect_group_id = CollectionReducerTestTokens.ItemSourceGroup }));
        reducer.ReduceAll(Event(CollectionReducerTestTokens.ItemUseResolved, new { item_id = CollectionReducerTestTokens.POTION, use_context = CollectionReducerTestTokens.BagBattle, effect_succeeded = true, outcome = CollectionReducerTestTokens.Success, consumed_quantity = 1 }, 4, new { effect_group_id = CollectionReducerTestTokens.ItemSourceGroup }));
        CollectionEventReducer reopened = NewReducer();
        reopened.ImportState(reducer.ExportState());
        CollectionReduction closed = reopened.ReduceAll(Event(CollectionReducerTestTokens.CollectionEffectGroupClosed, new { effect_group_id = CollectionReducerTestTokens.ItemSourceGroup, last_member_sequence = CollectionReducerTestTokens.Value4, completion_kind = CollectionReducerTestTokens.Completed, outcome = CollectionReducerTestTokens.Success }, 5));
        CollectionProjection hp = Assert.Single(closed.Families, family => family.FamilyKey == CollectionReducerTestTokens.ItemActivationHpByContext);
        Assert.Equal(new BigInteger(48), Assert.Single(hp.Measures, measure => measure.Key == CollectionReducerTestTokens.RecipientEffectsHpActualTotal).Scalar);
        Assert.Equal(new BigInteger(18), Assert.Single(hp.Measures, measure => measure.Key == CollectionReducerTestTokens.RecipientEffectsHpDirectItemTotal).Scalar);
        Assert.Equal(new BigInteger(30), Assert.Single(hp.Measures, measure => measure.Key == CollectionReducerTestTokens.RecipientEffectsHpSecondaryTotal).Scalar);
        CollectionProjection joint = Assert.Single(closed.Families, family => family.FamilyKey == CollectionReducerTestTokens.ItemActivationEffectJoint);
        Assert.Equal(BigInteger.One, joint.Measures.Single().Scalar);
        CollectionDimensionValue sources = Assert.Single(joint.Dimensions, dimension => dimension.Key == CollectionReducerTestTokens.HpSecondaryBySource);
        JsonElement berry = Assert.Single(sources.Value.EnumerateArray());
        Assert.Equal(CollectionReducerTestTokens.SITRUSBERRY, berry.GetProperty(CollectionReducerTestTokens.SourceId).GetString());
        Assert.Equal(CollectionReducerTestTokens.Value30, berry.GetProperty(CollectionReducerTestTokens.ActualRestoredHp).GetString());
        CollectionDirectProjection direct = Assert.Single(closed.Direct, summary => summary.GroupId == CollectionReducerTestTokens.Value6816176429a0);
        JsonElement excess = Assert.Single(direct.Dimensions, dimension => dimension.Key == CollectionReducerTestTokens.DirectItemCapExcess).Value;
        Assert.Equal(new BigInteger(2), BigInteger.Parse(excess.ValueKind == JsonValueKind.String ? excess.GetString()! : excess.GetRawText()));
        Assert.Equal(CollectionReducerTestTokens.PositiveKnownExcess, Assert.Single(direct.Dimensions, dimension => dimension.Key == CollectionReducerTestTokens.HpDirectItemResult).Value.GetString());
        Assert.Equal(CollectionReducerTestTokens.Known, Assert.Single(direct.Dimensions, dimension => dimension.Key == CollectionReducerTestTokens.SecondarySourceResults).Coverage);
    }

    /// <summary>
    /// Checks an unidentified immediate healing source cannot become known direct or secondary item healing.
    /// </summary>
    [Fact]
    public void UnknownHealingSourceRetainsInclusiveAmountWithoutFalseDirectCredit()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.ReduceAll(Event(CollectionReducerTestTokens.CollectionEffectGroupOpened, new { effect_group_id = CollectionReducerTestTokens.UnknownSourceGroup, kind = CollectionReducerTestTokens.ItemUse }, 1));
        reducer.ReduceAll(Event(CollectionReducerTestTokens.HpHealingResolved, new { recipient = new { pokemon_instance_id = CollectionReducerTestTokens.RunnerOne, owner_role = CollectionReducerTestTokens.Player }, effect_source = new { kind = CollectionReducerTestTokens.Unknown }, actual_restored_hp = 12, capacity_excess_hp = 0, healing_amount_kind = CollectionReducerTestTokens.Unknown }, 2, new { effect_group_id = CollectionReducerTestTokens.UnknownSourceGroup }));
        reducer.ReduceAll(Event(CollectionReducerTestTokens.ItemUseResolved, new { item_id = CollectionReducerTestTokens.POTION, use_context = CollectionReducerTestTokens.BagBattle, effect_succeeded = true, outcome = CollectionReducerTestTokens.Success }, 3, new { effect_group_id = CollectionReducerTestTokens.UnknownSourceGroup }));
        CollectionReduction closed = reducer.ReduceAll(Event(CollectionReducerTestTokens.CollectionEffectGroupClosed, new { effect_group_id = CollectionReducerTestTokens.UnknownSourceGroup, last_member_sequence = CollectionReducerTestTokens.Value3, completion_kind = CollectionReducerTestTokens.Completed }, 4));
        CollectionProjection hp = Assert.Single(closed.Families, family => family.FamilyKey == CollectionReducerTestTokens.ItemActivationHpByContext);
        Assert.Equal(new BigInteger(12), Assert.Single(hp.Measures, measure => measure.Key == CollectionReducerTestTokens.RecipientEffectsHpActualTotal).Scalar);
        Assert.Null(Assert.Single(hp.Measures, measure => measure.Key == CollectionReducerTestTokens.RecipientEffectsHpDirectItemTotal).Scalar);
        Assert.Null(Assert.Single(hp.Measures, measure => measure.Key == CollectionReducerTestTokens.RecipientEffectsHpSecondaryTotal).Scalar);
    }

    /// <summary>
    /// Checks a typed direct value has the actual JSON representation required by SQL atom storage.
    /// </summary>
    /// <param name="dimension">Known direct dimension.</param>
    private static void AssertTypedDimension(CollectionDimensionValue dimension)
    {
        bool valid = dimension.Kind switch
        {
            CollectionReducerTestTokens.Text => dimension.Value.ValueKind == JsonValueKind.String,
            CollectionReducerTestTokens.Boolean => dimension.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            CollectionReducerTestTokens.Set => dimension.Value.ValueKind == JsonValueKind.Array,
            CollectionReducerTestTokens.NativeId => dimension.Value.ValueKind == JsonValueKind.Object,
            CollectionReducerTestTokens.Integer or CollectionReducerTestTokens.Uint64 => BigInteger.TryParse(dimension.Value.ValueKind == JsonValueKind.String ? dimension.Value.GetString() : dimension.Value.GetRawText(), out _),
            CollectionReducerTestTokens.Rational => dimension.Value.ValueKind == JsonValueKind.Object && dimension.Value.TryGetProperty(CollectionReducerTestTokens.Numerator, out _) && dimension.Value.TryGetProperty(CollectionReducerTestTokens.Denominator, out _),
            _ => false
        };
        Assert.True(valid, $"Known {dimension.Kind} dimension {dimension.Key} has an incompatible JSON shape.");
    }

    /// <summary>
    /// Creates a reducer with the actual embedded collection catalogue.
    /// </summary>
    /// <returns>The reducer.</returns>
    private static CollectionEventReducer NewReducer()
        => new(CollectionCatalogue.LoadDefault());

    /// <summary>
    /// Creates one ordered source event.
    /// </summary>
    /// <param name="type">Source event type.</param>
    /// <param name="payload">Actual factual fields.</param>
    /// <param name="sequence">Unique ordered sequence.</param>
    /// <param name="context">Optional causal context.</param>
    /// <returns>A standalone event envelope.</returns>
    private static JsonElement Event(string type, object payload, int sequence, object? context = null)
        => JsonSerializer.SerializeToElement(new { type, payload, sequence = sequence.ToString(), attempt_id = _attemptId, battle_id = _battleId, branch_id = _attemptId, occurred_at_us = sequence.ToString(), context = context ?? new { } });

    /// <summary>
    /// Creates an actual completed move payload.
    /// </summary>
    /// <param name="move">Mechanical move identity.</param>
    /// <param name="result">Actual effect outcome.</param>
    /// <param name="invocation">Calling classification.</param>
    /// <returns>Completed move fields.</returns>
    private static object Move(string move, string result, string invocation)
        => new { actor = new { owner_role = _player }, move_id = move, result, positive_hp_targets = result == _effectResult ? 1 : 0, positive_substitute_targets = 0, hit_iterations_executed = 1, resolved_move_metadata = new { type_id = move == _metronome ? _normalType : _electricType, category = CollectionReducerTestTokens.Special, execution_origin = invocation }, invocation_context = new { invocation_kind = invocation, immediate_caller_move_id = _metronome, requested_move_id = move, root_move_id = _metronome, call_depth = 1, caller_actor = new { owner_role = _player } } };

    /// <summary>
    /// Creates a retained team's actual generated six-stat configuration.
    /// </summary>
    /// <param name="capture">Commit kind.</param>
    /// <param name="reasons">Committed causes.</param>
    /// <returns>Actual owned configuration.</returns>
    private static object Owned(string capture, string[] reasons)
        => new { capture_kind = capture, change_reasons = reasons, configuration_revision = 1, owned_entity_id = CollectionReducerTestTokens.RunnerOne, ownership_role = CollectionReducerTestTokens.Runner, team_commit_context = new { retained_after_commit = true }, species_identity = new { canonical_species_id = 25 }, generated_base_stats = new { hp = 100, attack = 100, defense = 100, special_attack = 100, special_defense = 100, speed = 100 }, computed_stats = new { maximum_hp = 150, attack = 110, defense = 110, special_attack = 110, special_defense = 110, speed = 110 }, moves = Array.Empty<object>(), data_versions = new { generation_profile = CollectionReducerTestTokens.ProfileOne } };
}
