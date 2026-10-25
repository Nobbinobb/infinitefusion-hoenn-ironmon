using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Connection.CompletedRuns;
using System.Reflection;
using Ironmon.Tracker.Collection.Storage;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies Alpha eligibility, retirement, durable unlocks and the actual native victory capture.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _alphaIdentity = "a935e5cc-1685-566a-a661-61610fb03ef2";
    private const string _alphaMilestone = "first_alpha_wattson";
    private const string _alphaTrainer = "LEADER_Wattson";
    private const string _otherAlphaTrainer = "LEADER_Roxanne";
    private const string _otherAlphaMilestone = "later_content";
    private const string _alphaWon = "won";
    private const string _alphaLost = "lost";
    private const string _alphaCompletionKind = "game_authoritative";
    private const string _alphaTitle = "Wait, that's the ending?";
    private const string _alphaCondition = "Defeat Watson during the first alpha release. Thanks for testing.";
    private const string _alphaNativeFixture = "alpha-completion-native.json";
    private const string _alphaFixtureBatch = "batch";
    private const string _alphaFixtureRecipe = "recipe";
    private const string _alphaPackField = "_achievementPack";
    private const string _alphaOpenedField = "_opened";
    private const string _alphaResultKey = "result";
    private const string _alphaMilestoneKey = "completion_milestone";
    private const string _alphaTrainerKey = "trainer_class";

    /// <summary>
    /// Requires the current Alpha milestone and the canonical final trainer on a won attempt.
    /// </summary>
    /// <param name="result">The authoritative attempt result.</param>
    /// <param name="milestone">The content milestone, or null for an ordinary completion.</param>
    /// <param name="trainer">The native terminal trainer class.</param>
    /// <param name="expected">Whether the combination qualifies.</param>
    /// <returns>A task completing after committed eligibility is verified.</returns>
    [Theory]
    [InlineData(_alphaWon, _alphaMilestone, _alphaTrainer, true)]
    [InlineData(_alphaLost, _alphaMilestone, _alphaTrainer, false)]
    [InlineData(_alphaWon, null, _alphaTrainer, false)]
    [InlineData(_alphaWon, _otherAlphaMilestone, _alphaTrainer, false)]
    [InlineData(_alphaWon, _alphaMilestone, _otherAlphaTrainer, false)]
    public async Task AlphaUnlockRequiresItsAuthoritativeWonMilestone(string result, string? milestone, string trainer, bool expected)
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement payload = JsonSerializer.SerializeToElement(new { result, completion_kind = _alphaCompletionKind, completion_milestone = milestone, completion_trainer_class_ids = new[] { trainer }, active_seconds = 10 });
        JsonElement batch = Batch(_stream, [Occurrence(_attemptEndEvent, payload, 1)]);
        await store.AcceptBatchAsync(batch);
        await store.AcceptBatchAsync(batch);
        var earned = await store.GetAchievementsAsync();
        Assert.Equal(expected, earned.Any(item => item.Id == Guid.Parse(_alphaIdentity)));
        Assert.Equal(result, ReadScalar(directory.Path, _attemptStatusSql));
        if (expected)
        {
            UnlockedAchievement achievement = Assert.Single(earned, item => item.Id == Guid.Parse(_alphaIdentity));
            Assert.Equal(_alphaTitle, achievement.Name);
            Assert.Equal(_alphaCondition, achievement.Condition);
            Assert.NotNull(await store.GetAchievementArtworkAsync(achievement.Id));
        }
    }

    /// <summary>
    /// Retires awarding without deleting an earned node or permitting retained past evidence to earn it anew.
    /// </summary>
    [Fact]
    public void RetiredAlphaKeepsItsEarnedRecordAndRejectsNewUnlocks()
    {
        using TestDirectory directory = new();
        using CollectionDatabase database = new(directory.Path);
        JsonElement payload = JsonSerializer.SerializeToElement(new { result = _alphaWon, completion_kind = _alphaCompletionKind, completion_milestone = _alphaMilestone, completion_trainer_class_ids = new[] { _alphaTrainer } });
        database.Accept(Batch(_stream, [Occurrence(_attemptEndEvent, payload, 1)]));
        JsonElement original = database.AchievementSnapshot(false);
        AchievementPack pack = (AchievementPack)typeof(CollectionDatabase).GetField(_alphaPackField, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var opened = (Dictionary<Guid, AchievementRule>)typeof(AchievementPack).GetField(_alphaOpenedField, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pack)!;
        Guid id = Guid.Parse(_alphaIdentity);
        AchievementRule retired = opened[id] with { Available = false };
        opened[id] = retired;
        AchievementEvidence evidence = new();
        evidence.Dimensions[_alphaResultKey] = JsonSerializer.SerializeToElement(_alphaWon);
        evidence.Dimensions[_alphaMilestoneKey] = JsonSerializer.SerializeToElement(_alphaMilestone);
        evidence.Dimensions[_alphaTrainerKey] = JsonSerializer.SerializeToElement(new[] { _alphaTrainer });
        Assert.True(AchievementEvaluator.Satisfied(retired with { Available = true }, [evidence]));
        Assert.False(AchievementEvaluator.Satisfied(retired, [evidence]));
        Assert.Equal(original.GetRawText(), database.AchievementSnapshot(false).GetRawText());
        Assert.Contains(database.AchievementSnapshot(true).Deserialize<UnlockedAchievement[]>()!, item => item.Id == id);
    }

    /// <summary>
    /// Persists the native runtime's real final-trainer event into both achievement storage and run history.
    /// </summary>
    /// <returns>A task completing after replay and complete writer/archive reconstruction preserve one win.</returns>
    [Fact]
    public async Task NativeAlphaVictoryPersistsItsAchievementAndWonRunAcrossRestart()
    {
        using TestDirectory directory = new();
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, _alphaNativeFixture)));
        JsonElement batch = capture.RootElement.GetProperty(_alphaFixtureBatch);
        CompletedRunRecipePayload recipe = capture.RootElement.GetProperty(_alphaFixtureRecipe).Deserialize<CompletedRunRecipePayload>(TrackerJson.Options)!;
        CompletedRunArchive archive = new(new TrackerKnowledgeOptions(directory.Path));
        await using (GameplayCollectionStore store = new(directory.Path))
        {
            await store.AcceptBatchAsync(batch);
            await store.AcceptBatchAsync(batch);
            archive.Store(recipe);
            archive.Store(recipe);
            Assert.Equal(_alphaWon, ReadScalar(directory.Path, _attemptStatusSql));
            Assert.Single(await store.GetAchievementsAsync(), item => item.Id == Guid.Parse(_alphaIdentity));
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        Assert.Single(await reopened.GetAchievementsAsync(true), item => item.Id == Guid.Parse(_alphaIdentity));
        CompletedRunRecipePayload winner = Assert.Single(new CompletedRunArchive(new TrackerKnowledgeOptions(directory.Path)).Recipes);
        Assert.Equal(_alphaWon, winner.Result);
        Assert.Equal(1, winner.Statistics!.AttemptsWon);
        Assert.Equal(_alphaWon, winner.Statistics.Result);
        Assert.Equal(1, winner.Statistics.BattlesCompleted);
        Assert.Equal(3, winner.Statistics.BadgesEarned);
        AssertIntegrity(directory.Path);
    }
}
