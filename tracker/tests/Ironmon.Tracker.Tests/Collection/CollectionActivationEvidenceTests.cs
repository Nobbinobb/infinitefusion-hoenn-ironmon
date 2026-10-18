using System.Numerics;
using System.Text.Json;
using Ironmon.Tracker.Collection.Reduction;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks activation grain, source views and interruption retirement from real source-shaped facts.
/// </summary>
public sealed partial class CollectionEventReducerTests
{
    private const string _activationGroup = "group-multi";
    private const string _activationTransactionId = "cda15dcf832b";
    private const string _activationHpId = "b9e25377a1f5";
    private const string _activationPpId = "773a1526bb46";
    private const string _ppEvent = "pp.restoration_resolved";
    private const string _interruptedEvent = "battle.interrupted";
    private const string _seriesSourceView = "source_view";
    private const string _viewInclusive = "inclusive";
    private const string _viewDirect = "direct_item";
    private const string _viewSecondary = "secondary";
    private const string _ppActivationCount = "positive_pp_activation_count";
    private const string _ppDirectTotal = "actual_direct_item_pp_total";
    private const string _activationPlayer = "player";
    private const string _activationOther = "other-player-pokemon";
    private const string _elixirId = "ELIXIR";
    private const string _bagFieldContext = "bag_field";
    private const string _bagBattleContext = "bag_battle";
    private const string _attemptsStateKey = "attempts";
    private const string _currentStreakKey = "current";
    private const string _potionId = "POTION";
    private const string _successOutcome = "success";
    private const string _multiUse = """{"item_id":"ELIXIR","use_context":"bag_battle","consumed_quantity":1,"effect_succeeded":true,"outcome":"success"}""";

    /// <summary>
    /// A multi-recipient item produces one activation count and retains every recipient amount.
    /// </summary>
    [Fact]
    public void MultiRecipientItemKeepsOneTransactionAndExactImmediatePp()
    {
        CollectionEventReducer reducer = NewReducer();
        object context = new { effect_group_id = _activationGroup };
        reducer.Reduce(Event(_eventOpen, new { effect_group_id = _activationGroup }, 1, context));
        reducer.Reduce(Event(_ppEvent, new { recipient = new { pokemon_instance_id = _activationPlayer, owner_role = _activationPlayer }, actual_total_pp = 5, effect_source = new { kind = CollectionReducerTestTokens.Item, canonical_id = _elixirId } }, 2, context));
        reducer.Reduce(Event(_ppEvent, new { recipient = new { pokemon_instance_id = _activationOther, owner_role = _activationPlayer }, actual_total_pp = 7, effect_source = new { kind = CollectionReducerTestTokens.Item, canonical_id = _elixirId } }, 3, context));
        reducer.Reduce(Event(_eventItem, JsonSerializer.Deserialize<JsonElement>(_multiUse), 4, context));
        CollectionReduction closed = reducer.ReduceAll(Event(_eventClose, new { effect_group_id = _activationGroup }, 5, context));
        Assert.Single(closed.Direct, row => row.GroupId == _activationTransactionId);
        CollectionDirectProjection pp = Assert.Single(closed.Direct, row => row.GroupId == _activationPpId);
        Assert.Equal(BigInteger.One, Assert.Single(pp.Measures, row => row.Key == _ppActivationCount).Scalar);
        Assert.Equal(new BigInteger(12), Assert.Single(pp.Measures, row => row.Key == _ppDirectTotal).Scalar);
    }

    /// <summary>
    /// Inclusive, direct and secondary HP views carry their own amounts.
    /// </summary>
    [Fact]
    public void DirectHpSourceViewsDoNotReuseTheInclusiveMeasure()
    {
        CollectionEventReducer reducer = NewReducer();
        object context = new { effect_group_id = _parentGroup };
        reducer.Reduce(Event(_eventOpen, JsonSerializer.Deserialize<JsonElement>(_parentOpen), 1, context));
        reducer.Reduce(Event(_eventHealing, JsonSerializer.Deserialize<JsonElement>(_potionHealing), 2, context));
        reducer.Reduce(Event(_eventItem, JsonSerializer.Deserialize<JsonElement>(_potionUse), 3, context));
        CollectionReduction closed = reducer.ReduceAll(Event(_eventClose, new { effect_group_id = _parentGroup }, 4, context));
        CollectionDirectProjection[] hp = [.. closed.Direct.Where(row => row.GroupId == _activationHpId)];
        Assert.Equal(3, hp.Length);
        foreach (CollectionDirectProjection view in hp)
        {
            string? name = Assert.Single(view.Dimensions, dimension => dimension.Key == _seriesSourceView).Value.GetString();
            Assert.Contains(name, new[] { _viewInclusive, _viewDirect, _viewSecondary });
            Assert.Equal(new BigInteger(name == _viewSecondary ? 0 : 5), Assert.Single(view.Measures).Scalar);
        }
    }

    /// <summary>
    /// Loading another save retires interrupted working joins without creating a terminal victory.
    /// </summary>
    [Fact]
    public void InterruptedBattleRetiresOnlyItsWorkingState()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        CollectionReduction closed = reducer.ReduceAll(Event(_interruptedEvent, new { }, 3));
        Assert.Empty(reducer.ExportState().GetProperty(CollectionReducerTestTokens.Battles).EnumerateObject());
        Assert.DoesNotContain(closed.Direct, row => row.GroupId == _terminalCauseGroup);
    }

    /// <summary>
    /// Both native Bag contexts break the same-attempt streak after actual recovery.
    /// </summary>
    /// <param name="useContext">Native field or battle Bag context.</param>
    [Theory]
    [InlineData(_bagFieldContext)]
    [InlineData(_bagBattleContext)]
    public void FieldAndBattleBagRecoveryBothBreakTheTrainerStreak(string useContext)
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_endType, _perishWinSource, 3));
        Assert.Equal(1, reducer.ExportState().GetProperty(_attemptsStateKey).GetProperty(_attemptId).GetProperty(_currentStreakKey).GetInt32());
        object context = new { effect_group_id = _parentGroup };
        reducer.Reduce(Event(_eventOpen, JsonSerializer.Deserialize<JsonElement>(_parentOpen), 4, context));
        reducer.Reduce(Event(_eventHealing, JsonSerializer.Deserialize<JsonElement>(_potionHealing), 5, context));
        reducer.Reduce(Event(_eventItem, new { item_id = _potionId, use_context = useContext, consumed_quantity = 1, effect_succeeded = true, outcome = _successOutcome }, 6, context));
        reducer.Reduce(Event(_eventClose, new { effect_group_id = _parentGroup }, 7, context));
        Assert.Equal(0, reducer.ExportState().GetProperty(_attemptsStateKey).GetProperty(_attemptId).GetProperty(_currentStreakKey).GetInt32());
    }
}
