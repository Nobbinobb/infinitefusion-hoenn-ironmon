using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies future achievement currency is durable, independent of popups, and never backfilled.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _rewardConsumerIdentity = "0a74fe46-8d20-47c6-9d9a-bafc46b0bf59";
    private const string _removeRewardOutboxSql = "DELETE FROM private_consumers WHERE consumer_key=$consumer";
    private const string _rewardConsumerParameter = "$consumer";

    /// <summary>
    /// Preserves rewards across restart and popup acknowledgements, removing them only after game credit.
    /// </summary>
    /// <returns>Completion after exactly-once durable reward delivery has been verified.</returns>
    [Fact]
    public async Task WardrobeRewardsSurviveRestartAndPopupAcknowledgement()
    {
        using TestDirectory directory = new();
        Guid[] ids;
        JsonElement batch = Batch(_stream, AchievementTrainer(_rivalClass, 1));
        await using (GameplayCollectionStore first = new(directory.Path))
        {
            await first.AcceptBatchAsync(batch);
            ids = [.. (await first.GetAchievementsAsync()).Select(item => item.Id).Order()];
            Assert.NotEmpty(ids);
            Assert.Equal(ids, (await first.GetWardrobeRewardsAsync()).Order());
            await first.AcknowledgeAchievementsAsync(ids.ToHashSet());
            Assert.Empty(await first.GetAchievementsAsync(true));
            Assert.Equal(ids, (await first.GetWardrobeRewardsAsync()).Order());
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        Assert.Equal(ids, (await reopened.GetWardrobeRewardsAsync()).Order());
        await reopened.AcceptBatchAsync(batch);
        Assert.Equal(ids, (await reopened.GetWardrobeRewardsAsync()).Order());
        await reopened.AcknowledgeWardrobeRewardsAsync(new HashSet<Guid> { ids[0] });
        Assert.Equal(ids.Skip(1), (await reopened.GetWardrobeRewardsAsync()).Order());
        await reopened.AcknowledgeWardrobeRewardsAsync(ids.ToHashSet());
        await reopened.AcknowledgeWardrobeRewardsAsync(ids.ToHashSet());
        Assert.Empty(await reopened.GetWardrobeRewardsAsync());
        Assert.Equal(ids, (await reopened.GetAchievementsAsync()).Select(item => item.Id).Order());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Opens a pre-reward ledger without paying its existing unlocks, then rewards only future milestones.
    /// </summary>
    /// <returns>Completion after the legacy and future populations have been compared.</returns>
    [Fact]
    public async Task WardrobeRewardsDoNotBackfillExistingAchievements()
    {
        using TestDirectory directory = new();
        HashSet<Guid> original;
        await using (GameplayCollectionStore first = new(directory.Path))
        {
            await first.AcceptBatchAsync(Batch(_stream, AchievementTrainer(_rivalClass, 1)));
            original = [.. (await first.GetAchievementsAsync()).Select(item => item.Id)];
        }

        SqliteConnectionStringBuilder options = new() { DataSource = Path.Combine(directory.Path, _databaseName), Mode = SqliteOpenMode.ReadWrite, Pooling = false };
        using (SqliteConnection connection = new(options.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = _removeRewardOutboxSql;
            command.Parameters.AddWithValue(_rewardConsumerParameter, Guid.Parse(_rewardConsumerIdentity).ToByteArray());
            command.ExecuteNonQuery();
        }

        await using GameplayCollectionStore upgraded = new(directory.Path);
        Assert.Equal(original.Order(), (await upgraded.GetAchievementsAsync()).Select(item => item.Id).Order());
        Assert.Empty(await upgraded.GetWardrobeRewardsAsync());
        await upgraded.AcceptBatchAsync(Batch(_stream, [.. TrainerWins(50, 3)]));
        Guid[] future = [.. (await upgraded.GetAchievementsAsync()).Select(item => item.Id).Where(id => !original.Contains(id)).Order()];
        Assert.NotEmpty(future);
        Assert.Equal(future, (await upgraded.GetWardrobeRewardsAsync()).Order());
    }
}
