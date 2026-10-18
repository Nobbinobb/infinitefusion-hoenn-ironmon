using Ironmon.Tracker.Collection.Reduction;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks exact health summaries across changed maximum HP and retired battler slots.
/// </summary>
public sealed partial class CollectionEventReducerTests
{
    private const string _healthSummaryFamily = "completed_battle_summary_distributions";
    private const string _minimumRatioDimension = "min_hp_ratio";
    private const string _exitRatioDimension = "exit_hp_ratio";
    private const string _healthNumerator = "numerator";
    private const string _healthDenominator = "denominator";
    private const string _healthTen = "10";
    private const string _healthZero = "0";
    private const string _healthBattles = "battles";
    private const string _healthEvidence = "source_evidence";
    private const string _healthActors = "actors";
    private const string _healthRunner = "runner";
    private const string _healthKnown = "known";
    private const string _healthUnknown = "unknown";
    private const string _healthExitDimension = "exit_hp";
    private const string _healthJointFamily = "battle_actor_health_outcome_joint";
    private const string _healthLossSmall = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":20,"hp_after":5,"max_hp":20,"actual_hp_lost":15}""";
    private const string _healthLossLargerMaximum = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true},"hp_before":50,"hp_after":10,"max_hp":100,"actual_hp_lost":40}""";
    private const string _healthEndNoRoster = """{"outcome":"win","final_actor_states":[]}""";
    private const string _healthFaintedWithoutSnapshot = """{"target":{"appearance_id":"runner","owner_role":"player","usable_runner":true}}""";

    /// <summary>
    /// Uses the actual maximum at each observation instead of dividing the smallest absolute HP by a later maximum.
    /// </summary>
    [Fact]
    public void MinimumHealthRatioUsesEachObservationsMaximumAcrossCheckpoint()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_eventHp, _healthLossSmall, 3));
        JsonElement checkpoint = reducer.ExportState();
        reducer = NewReducer();
        reducer.ImportState(checkpoint);
        reducer.Reduce(SourceEvent(_eventHp, _healthLossLargerMaximum, 4));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_endType, _healthEndNoRoster, 5));
        CollectionProjection minimum = Assert.Single(completed.Families, row => row.FamilyKey == _healthSummaryFamily && row.Dimensions.Any(value => value.Key == _minimumRatioDimension));
        JsonElement ratio = Assert.Single(minimum.Dimensions, value => value.Key == _minimumRatioDimension).Value;
        Assert.Equal(CollectionReducerTestTokens.Value1, ratio.GetProperty(_healthNumerator).GetString());
        Assert.Equal(_healthTen, ratio.GetProperty(_healthDenominator).GetString());
    }

    /// <summary>
    /// Keeps lethal HP evidence when an appearance is absent from the final active roster.
    /// </summary>
    [Fact]
    public void RetiredFaintedActorKeepsZeroTerminalHealth()
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_eventHp, _poisonLoss, 3));
        reducer.Reduce(SourceEvent(_faintType, _healthFaintedWithoutSnapshot, 4));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_endType, _healthEndNoRoster, 5));
        CollectionProjection health = Assert.Single(completed.Families, row => row.FamilyKey == _healthJointFamily);
        Assert.Equal(0, Assert.Single(health.Dimensions, value => value.Key == _healthExitDimension).Value.GetInt32());
        CollectionProjection exit = Assert.Single(completed.Families, row => row.FamilyKey == _healthSummaryFamily && row.Dimensions.Any(value => value.Key == _exitRatioDimension));
        Assert.Equal(_healthZero, Assert.Single(exit.Dimensions, value => value.Key == _exitRatioDimension).Value.GetProperty(_healthNumerator).GetString());
    }

    /// <summary>
    /// Keeps incomplete historical minima unknown unless a new faint proves the exact minimum is zero.
    /// </summary>
    /// <param name="faints">Whether subsequent native HP evidence proves a zero-HP observation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyBattleCheckpointDoesNotInventMissingMinimumRatio(bool faints)
    {
        CollectionEventReducer reducer = NewReducer();
        reducer.Reduce(SourceEvent(_eventBattle, _battleSource, 1));
        reducer.Reduce(SourceEvent(_entryType, _entrySource, 2));
        reducer.Reduce(SourceEvent(_eventHp, _healthLossSmall, 3));
        System.Text.Json.Nodes.JsonObject checkpoint = System.Text.Json.Nodes.JsonNode.Parse(reducer.ExportState().GetRawText())!.AsObject();
        System.Text.Json.Nodes.JsonNode battle = checkpoint[_healthBattles]!.AsObject().First().Value!;
        battle[_healthEvidence]![_healthActors]![_healthRunner]!.AsObject().Remove(_minimumRatioDimension);
        reducer = NewReducer();
        reducer.ImportState(JsonSerializer.SerializeToElement(checkpoint));
        reducer.Reduce(SourceEvent(_eventHp, faints ? _poisonLoss : _healthLossLargerMaximum, 4));
        CollectionReduction completed = reducer.ReduceAll(SourceEvent(_endType, _healthEndNoRoster, 5));
        CollectionProjection summary = Assert.Single(completed.Families, row => row.FamilyKey == _healthSummaryFamily);
        Assert.Equal(faints ? _healthKnown : _healthUnknown, Assert.Single(summary.Dimensions, value => value.Key == _minimumRatioDimension).Coverage);
    }
}
