using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Collection.Reduction;
using Ironmon.Tracker.Collection.Storage;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies rival encounter identities, distinct victory populations and compatible definition updates.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _openingRivalIdentity = "trainer:18:RIVAL1:Rival";
    private const string _legacyOpeningRivalIdentity = "map:18:event:5:command:0";
    private const string _otherRivalIdentity = "trainer:999:RIVAL1:Rival";
    private const string _wallyEncounterIdentity = "trainer:999:RIVAL2:Wally";
    private const string _rivalLostResult = "lost";
    private const string _rivalWonResult = "won";
    private const string _rivalAbandonedResult = "abandoned";
    private const string _rivalLossOutcome = "loss";
    private const string _rivalBattleIdentity = "rival-loss-battle";
    private const string _rivalCompletionKind = "game_authoritative";
    private const string _rivalBattleEndIdentity = "rival-loss-end";
    private const string _unrelatedTerminalBattle = "different-terminal-battle";
    private const string _rivalPackField = "_achievementPack";
    private const string _rivalOpenedField = "_opened";
    private const string _rivalFirstCondition = "Win 5 main rival battles across your runs.";
    private const string _rivalOldFirstCondition = "Win your first main rival battle.";
    private const string _rivalLegacyLossFilter = """["and",["eq","attempt_completion_result","lost"],["eq","battle_kind","trainer"],["contains","authored_encounter_key","map:18:event:5:command:0"]]""";
    private const string _rivalAttemptProperty = "attempt_id";
    private const string _rivalAttemptFormat = "x32";
    private const string _rivalBattlePrefix = "rival-population-";
    private const string _rivalBattleProperty = "battle_id";
    private const string _rivalKnownCoverage = "known";

    /// <summary>
    /// Accepts the stable opening identity and historical fallback while excluding other battles and completions.
    /// </summary>
    /// <param name="encounter">The emitted authored encounter identity.</param>
    /// <param name="trainer">The actual native trainer class.</param>
    /// <param name="result">The authoritative attempt completion result.</param>
    /// <param name="matchingBattle">Whether completion refers to this exact terminal battle.</param>
    /// <param name="expected">Whether the opening-rival loss achievement must unlock.</param>
    /// <returns>Completion after durable unlocks and duplicate delivery are checked.</returns>
    [Theory]
    [InlineData(_openingRivalIdentity, _rivalClass, _rivalLostResult, true, true)]
    [InlineData(_legacyOpeningRivalIdentity, _rivalClass, _rivalLostResult, true, true)]
    [InlineData(_otherRivalIdentity, _rivalClass, _rivalLostResult, true, false)]
    [InlineData(_wallyEncounterIdentity, _wallyClass, _rivalLostResult, true, false)]
    [InlineData(_openingRivalIdentity, _wallyClass, _rivalLostResult, true, false)]
    [InlineData(_openingRivalIdentity, _rivalClass, _rivalWonResult, true, false)]
    [InlineData(_openingRivalIdentity, _rivalClass, _rivalAbandonedResult, true, false)]
    [InlineData(_openingRivalIdentity, _rivalClass, _rivalLostResult, false, false)]
    public async Task OpeningRivalLossRequiresItsActualIdentityAndTerminalBattle(string encounter, string trainer, string result, bool matchingBattle, bool expected)
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement batch = Batch(_stream, RivalLossOccurrences(encounter, trainer, result, matchingBattle, 1));
        await store.AcceptBatchAsync(batch);
        await store.AcceptBatchAsync(batch);
        Assert.Equal(expected, (await store.GetAchievementsAsync()).Any(item => item.Number == 300));
        Assert.Equal(expected, (await store.GetAchievementsAsync(true)).Any(item => item.Number == 300));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Accumulates five main-rival wins across attempts and restarts without counting Wally victories.
    /// </summary>
    /// <returns>Completion after the fourth, fifth and following rival milestone boundaries are checked.</returns>
    [Fact]
    public async Task RivalMilestoneStartsAtFiveAcrossAttemptsAndRestart()
    {
        using TestDirectory directory = new();
        await using (GameplayCollectionStore first = new(directory.Path))
        {
            JsonElement[] events = [.. Enumerable.Range(0, 4).SelectMany(index => RivalPopulationWin(_rivalClass, index * 2 + 1)), .. RivalPopulationWin(_wallyClass, 9)];
            await first.AcceptBatchAsync(Batch(_stream, events));
            Assert.Contains(await first.GetAchievementsAsync(), item => item.Number == 45);
            Assert.DoesNotContain(await first.GetAchievementsAsync(), item => item.Number == 31);
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        await reopened.AcceptBatchAsync(Batch(_stream, [.. RivalPopulationWin(_rivalClass, 11)]));
        UnlockedAchievement firstRival = Assert.Single(await reopened.GetAchievementsAsync(), item => item.Number == 31);
        Assert.Equal(_rivalFirstCondition, firstRival.Condition);
        Assert.DoesNotContain(await reopened.GetAchievementsAsync(), item => item.Number == 34);
        await reopened.AcceptBatchAsync(Batch(_stream, [.. Enumerable.Range(0, 20).SelectMany(index => RivalPopulationWin(_rivalClass, 13 + index * 2))]));
        Assert.Contains(await reopened.GetAchievementsAsync(), item => item.Number == 34);
        Assert.DoesNotContain(await reopened.GetAchievementsAsync(), item => item.Number == 35);
    }

    /// <summary>
    /// Keeps every subsequent approved rival tier in strictly increasing order after the new five-win start.
    /// </summary>
    [Fact]
    public void RivalVictoryThresholdsRetainTheirExistingProgression()
    {
        AchievementPack pack = new();
        HashSet<Guid> all = [.. pack.Nodes.Select(node => node.Id)];
        AchievementRule[] rules = [.. pack.Nodes.Select(node => pack.Open(node.Id, all)).Where(rule => rule.Number is >= 31 and <= 39).OrderBy(rule => rule.Threshold.GetInt32())];
        Assert.Equal([5, 25, 50, 100, 150, 250, 500, 750, 1000], rules.Select(rule => rule.Threshold.GetInt32()));
    }

    /// <summary>
    /// Verifies each Wally milestone uses actual trainer-class evidence and excludes victories over the main rival.
    /// </summary>
    /// <param name="number">The approved Wally milestone review number.</param>
    /// <param name="wins">The number of wins required by that milestone.</param>
    [Theory]
    [InlineData(40, 1)]
    [InlineData(41, 5)]
    [InlineData(42, 10)]
    [InlineData(43, 25)]
    [InlineData(44, 50)]
    public void WallyMilestonesUseTheirOwnTrainerClassPopulation(int number, int wins)
    {
        AchievementPack pack = new();
        HashSet<Guid> all = [.. pack.Nodes.Select(node => node.Id)];
        AchievementRule rule = pack.Nodes.Select(node => pack.Open(node.Id, all)).Single(rule => rule.Number == number);
        Assert.Equal(wins, rule.Threshold.GetInt32());
        foreach (string trainer in new[] { _wallyClass, _rivalClass })
        {
            CollectionEventReducer reducer = new(CollectionCatalogue.LoadDefault());
            List<AchievementEvidence> evidence = [];
            foreach (JsonElement source in Enumerable.Range(0, wins).SelectMany(index => RivalPopulationWin(trainer, index * 2 + 1)))
            {
                foreach (CollectionDirectProjection projection in reducer.ReduceDirect(source).Where(row => row.GroupId == rule.Group))
                    evidence.Add(RivalAchievementEvidence(projection));
            }

            Assert.Equal(trainer == _wallyClass, AchievementEvaluator.Satisfied(rule, evidence));
        }
    }

    /// <summary>
    /// Rebinds an earlier pack without revoking the old first-win unlock and recovers an already recorded opening loss.
    /// </summary>
    [Fact]
    public void RivalRuleUpdatePreservesEarnedDatesAndRecoversRetainedLossEvidence()
    {
        using TestDirectory directory = new();
        DateTimeOffset originalDate;
        using (CollectionDatabase previous = new(directory.Path))
        {
            AchievementPack oldPack = new();
            HashSet<Guid> all = [.. oldPack.Nodes.Select(node => node.Id)];
            foreach (SealedAchievement node in oldPack.Nodes)
                oldPack.Open(node.Id, all);
            Dictionary<Guid, AchievementRule> opened = (Dictionary<Guid, AchievementRule>)typeof(AchievementPack).GetField(_rivalOpenedField, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(oldPack)!;
            Guid first = oldPack.Nodes.Single(node => opened[node.Id].Number == 31).Id;
            Guid loss = oldPack.Nodes.Single(node => opened[node.Id].Number == 300).Id;
            opened[first] = opened[first] with { Threshold = JsonSerializer.SerializeToElement(1), Condition = _rivalOldFirstCondition };
            opened[loss] = opened[loss] with { Filter = JsonSerializer.Deserialize<JsonElement>(_rivalLegacyLossFilter) };
            oldPack.Binding[0] ^= 1;
            typeof(CollectionDatabase).GetField(_rivalPackField, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(previous, oldPack);
            previous.Accept(Batch(_stream, [.. AchievementTrainer(_rivalClass, 1), .. RivalLossOccurrences(_openingRivalIdentity, _rivalClass, _rivalLostResult, true, 3)]));
            UnlockedAchievement[] earned = previous.AchievementSnapshot(false).Deserialize<UnlockedAchievement[]>()!;
            originalDate = Assert.Single(earned, item => item.Number == 31).UnlockedAt;
            Assert.DoesNotContain(earned, item => item.Number == 300);
            previous.AcknowledgeAchievements(earned.Select(item => item.Id).ToHashSet());
        }

        using CollectionDatabase updated = new(directory.Path);
        UnlockedAchievement[] retained = updated.AchievementSnapshot(false).Deserialize<UnlockedAchievement[]>()!;
        Assert.Equal(originalDate, Assert.Single(retained, item => item.Number == 31).UnlockedAt);
        Assert.Contains(retained, item => item.Number == 300);
        Assert.Equal(300, Assert.Single(updated.AchievementSnapshot(true).Deserialize<UnlockedAchievement[]>()!).Number);
        Assert.Equal(retained, updated.AchievementSnapshot(false).Deserialize<UnlockedAchievement[]>()!);
    }

    /// <summary>
    /// Creates a native-shaped loss with an explicit encounter and the separate terminal completion reference.
    /// </summary>
    /// <param name="encounter">The actual or historical encounter identity.</param>
    /// <param name="trainer">The native trainer class.</param>
    /// <param name="result">The authoritative completion result.</param>
    /// <param name="matchingBattle">Whether completion references the same terminal battle.</param>
    /// <param name="sequence">The first contiguous source sequence.</param>
    /// <returns>Admission, loss and run-completion envelopes.</returns>
    private static JsonElement[] RivalLossOccurrences(string encounter, string trainer, string result, bool matchingBattle, int sequence)
        => [Occurrence(_battleStartedEvent, JsonSerializer.SerializeToElement(new { battle_kind = _trainerKind, encounter = new { authored_entry_ids = new[] { encounter }, trainer_class_ids = new[] { trainer } } }), sequence, _rivalBattleIdentity), Occurrence(_battleEndedEvent, JsonSerializer.SerializeToElement(new { event_id = _rivalBattleEndIdentity, engine_decision = 2, outcome = _rivalLossOutcome, authored_entry_ids = new[] { encounter } }), sequence + 1, _rivalBattleIdentity), Occurrence(_attemptEndEvent, JsonSerializer.SerializeToElement(new { result, completion_kind = _rivalCompletionKind, terminal_battle_ref = matchingBattle ? _rivalBattleIdentity : _unrelatedTerminalBattle }), sequence + 2, null!)];

    /// <summary>
    /// Places each trainer victory in a separate attempt while preserving its actual native class.
    /// </summary>
    /// <param name="trainer">The native trainer class.</param>
    /// <param name="sequence">The first contiguous source sequence.</param>
    /// <returns>Two source envelopes with independent attempt and battle identities.</returns>
    private static IEnumerable<JsonElement> RivalPopulationWin(string trainer, int sequence)
    {
        foreach (JsonElement source in AchievementTrainer(trainer, sequence))
        {
            JsonNode envelope = JsonNode.Parse(source.GetRawText())!;
            envelope[_rivalAttemptProperty] = sequence.ToString(_rivalAttemptFormat);
            envelope[_rivalBattleProperty] = _rivalBattlePrefix + sequence;
            yield return JsonSerializer.SerializeToElement(envelope);
        }
    }

    /// <summary>
    /// Retains only known dimensions and scalar measures from one actual reduced battle occurrence.
    /// </summary>
    /// <param name="projection">The production reducer's directly indexed battle evidence.</param>
    /// <returns>The correlated achievement evidence for that single occurrence.</returns>
    private static AchievementEvidence RivalAchievementEvidence(CollectionDirectProjection projection)
    {
        AchievementEvidence result = new();
        foreach (CollectionDimensionValue dimension in projection.Dimensions.Where(item => item.Coverage == _rivalKnownCoverage))
            result.Dimensions[dimension.Key] = dimension.Value;

        foreach (CollectionMeasureUpdate measure in projection.Measures.Where(item => item.Coverage == _rivalKnownCoverage && item.Scalar.HasValue))
            result.Scalars[measure.Key] = measure.Scalar!.Value;

        return result;
    }
}
