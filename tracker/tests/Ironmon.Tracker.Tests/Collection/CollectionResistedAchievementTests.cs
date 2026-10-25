using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks approved resisted-KO thresholds and compatible encrypted-ledger updates.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _resistedHitEvent = "hit.resolved";
    private const string _resistedHitPayload = """{"source_owner_role":"player","target_owner_role":"opponent","mechanical_move_id":"VINEWHIP","mechanics_class":"move","effective_type":"GRASS","effective_category":"physical","critical":false,"result":"real_hp_damage","pool":"pokemon","hp_before_equals_max":true,"hp_after_is_zero":true,"lethal_application":true,"terminal_faint_relation":"confirmed","hit_count":1,"actual_pool_hp_lost":1,"application_snapshot":{"type_modifier":1,"type_modifier_normal":2,"pool":"pokemon","actual_pool_hp_lost":1}}""";
    private const string _resistedSnapshotProperty = "application_snapshot";
    private const string _resistedTypeModifierProperty = "type_modifier";
    private const string _resistedLethalProperty = "lethal_application";
    private const string _resistedRelationProperty = "terminal_faint_relation";
    private const string _resistedConfirmed = "confirmed";
    private const string _resistedUnconfirmed = "unconfirmed";
    private const string _resistedLegacyPackHash = "03559e9398f3c369987415f8b0abae0c6220e2c635fce49b4c19348e867bf8f1";
    private const string _resistedRewriteSql = "UPDATE private_consumers SET pack_binding_token=$binding,nonce=$nonce,auth_tag=$tag,ciphertext=$ciphertext" + _achievementConsumerFilter;
    private const string _resistedBindingParameter = "$binding";
    private const string _resistedNonceParameter = "$nonce";
    private const string _resistedTagParameter = "$tag";
    private const string _resistedCipherParameter = "$ciphertext";
    private static readonly DateTimeOffset _resistedLegacyUnlockTime = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    /// <summary>
    /// Applies only confirmed resisted KOs to the cumulative 1, 25 and 100 milestones across a restart.
    /// </summary>
    /// <returns>A task completing after both new boundaries and retained accumulated evidence are checked.</returns>
    [Fact]
    public async Task ResistedKoAchievementsUseApprovedThresholdsAndRetainCounters()
    {
        AchievementPack pack = new();
        HashSet<Guid> known = [.. pack.Nodes.Select(node => node.Id)];
        AchievementRule[] rules = [.. pack.Nodes.Select(node => pack.Open(node.Id, known)).Where(rule => rule.Number is >= 158 and <= 160).OrderBy(rule => rule.Number)];
        Assert.Equal([1, 25, 100], rules.Select(rule => rule.Threshold.GetInt32()));
        using TestDirectory directory = new();
        await using (GameplayCollectionStore store = new(directory.Path))
        {
            await store.AcceptBatchAsync(Batch(_stream, [ResistedKoHit(1, 1), ResistedKoHit(2, lethal: false), ResistedKoHit(3, relation: _resistedUnconfirmed)]));
            Assert.DoesNotContain(await store.GetAchievementsAsync(), item => item.Number is >= 158 and <= 160);
            await store.AcceptBatchAsync(Batch(_stream, ResistedKoHits(10, 4)));
            Assert.Contains(await store.GetAchievementsAsync(), item => item.Number == 158);
            Assert.DoesNotContain(await store.GetAchievementsAsync(), item => item.Number == 159);
            await store.AcceptBatchAsync(Batch(_stream, ResistedKoHits(14, 14)));
            Assert.DoesNotContain(await store.GetAchievementsAsync(), item => item.Number == 159);
            await store.AcceptBatchAsync(Batch(_stream, ResistedKoHits(1, 28)));
            Assert.Equal(rules[1].Condition, Assert.Single(await store.GetAchievementsAsync(), item => item.Number == 159).Condition);
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        await reopened.AcceptBatchAsync(Batch(_stream, ResistedKoHits(74, 29)));
        Assert.DoesNotContain(await reopened.GetAchievementsAsync(), item => item.Number == 160);
        await reopened.AcceptBatchAsync(Batch(_stream, ResistedKoHits(1, 103)));
        Assert.Equal(rules[2].Condition, Assert.Single(await reopened.GetAchievementsAsync(), item => item.Number == 160).Condition);
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Retains an earned old milestone, its timestamp and notification after the pack raises its threshold.
    /// </summary>
    /// <param name="oldTotal">The cumulative resisted KOs sufficient for the historical milestone.</param>
    /// <param name="milestoneNumber">The previously earned stable milestone number.</param>
    /// <returns>A task completing after authenticated rebinding and original progress are verified.</returns>
    [Theory]
    [InlineData(10, 159)]
    [InlineData(50, 160)]
    public async Task ResistedKoAchievementsKeepLegacyUnlocksAcrossHigherThresholds(int oldTotal, int milestoneNumber)
    {
        using TestDirectory directory = new();
        await using (GameplayCollectionStore original = new(directory.Path))
            await original.AcceptBatchAsync(Batch(_stream, ResistedKoHits(oldTotal, 1)));

        AchievementPack pack = new();
        HashSet<Guid> known = [.. pack.Nodes.Select(node => node.Id)];
        SealedAchievement target = pack.Nodes.Single(node => pack.Open(node.Id, known).Number == milestoneNumber);
        AchievementRule rule = pack.Open(target.Id, known);
        WriteResistedLegacyCheckpoint(directory.Path, target.Id);
        Assert.Equal(Convert.FromHexString(_resistedLegacyPackHash), Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointBindingSql)));
        await using GameplayCollectionStore upgraded = new(directory.Path);
        UnlockedAchievement earned = Assert.Single(await upgraded.GetAchievementsAsync(), item => item.Id == target.Id);
        Assert.Equal(_resistedLegacyUnlockTime, earned.UnlockedAt);
        Assert.Equal(rule.Condition, earned.Condition);
        Assert.Contains(await upgraded.GetAchievementsAsync(true), item => item.Id == target.Id);
        Assert.Equal(pack.Binding, Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointBindingSql)));
        if (milestoneNumber == 159)
        {
            await upgraded.AcceptBatchAsync(Batch(_stream, ResistedKoHits(99 - oldTotal, oldTotal + 1)));
            Assert.DoesNotContain(await upgraded.GetAchievementsAsync(), item => item.Number == 160);
            await upgraded.AcceptBatchAsync(Batch(_stream, ResistedKoHits(1, 100)));
            Assert.Contains(await upgraded.GetAchievementsAsync(), item => item.Number == 160);
        }

        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Creates contiguous authoritative hit occurrences without a synthetic achievement event.
    /// </summary>
    /// <param name="count">The number of confirmed resisted KOs.</param>
    /// <param name="firstSequence">The first contiguous delivery sequence.</param>
    /// <returns>The registered native-shaped hit occurrences.</returns>
    private static JsonElement[] ResistedKoHits(int count, int firstSequence)
        => [.. Enumerable.Range(firstSequence, count).Select(sequence => ResistedKoHit(sequence))];

    /// <summary>
    /// Creates a same-row hit outcome with configurable negative-filter evidence.
    /// </summary>
    /// <param name="sequence">The contiguous source sequence.</param>
    /// <param name="effectiveness">The actual type-effectiveness multiplier.</param>
    /// <param name="lethal">Whether this application caused the knockout.</param>
    /// <param name="relation">The observed terminal-faint relationship.</param>
    /// <returns>The registered hit event with all correlated rule dimensions.</returns>
    private static JsonElement ResistedKoHit(int sequence, decimal effectiveness = 0.5m, bool lethal = true, string relation = _resistedConfirmed)
    {
        var payload = System.Text.Json.Nodes.JsonNode.Parse(_resistedHitPayload)!;
        payload[_resistedSnapshotProperty]![_resistedTypeModifierProperty] = (int)(effectiveness * 2);
        payload[_resistedLethalProperty] = lethal;
        payload[_resistedRelationProperty] = relation;
        return Occurrence(_resistedHitEvent, JsonSerializer.SerializeToElement(payload), sequence);
    }

    /// <summary>
    /// Seeds an authenticated historical ledger in an owned disposable fixture without altering its evidence.
    /// </summary>
    /// <param name="directory">The closed disposable profile database.</param>
    /// <param name="earnedId">The historical milestone earned under the old threshold.</param>
    private static void WriteResistedLegacyCheckpoint(string directory, Guid earnedId)
    {
        byte[] ciphertext = Assert.IsType<byte[]>(ReadScalar(directory, _checkpointCipherSql));
        byte[] tag = Assert.IsType<byte[]>(ReadScalar(directory, _checkpointTagSql));
        byte[] nonce = Assert.IsType<byte[]>(ReadScalar(directory, _checkpointNonceSql));
        byte[] binding = Assert.IsType<byte[]>(ReadScalar(directory, _checkpointBindingSql));
        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm cipher = new(AchievementPack.StateKey(Convert.FromHexString(_profile), Convert.FromHexString(_epoch)), 16);
        cipher.Decrypt(nonce, ciphertext, tag, plaintext, binding);
        AchievementCheckpoint checkpoint = JsonSerializer.Deserialize<AchievementCheckpoint>(plaintext)!;
        checkpoint.Earned[earnedId] = _resistedLegacyUnlockTime;
        checkpoint.Pending.Add(earnedId);
        CryptographicOperations.ZeroMemory(plaintext);
        plaintext = JsonSerializer.SerializeToUtf8Bytes(checkpoint);
        ciphertext = new byte[plaintext.Length];
        nonce = RandomNumberGenerator.GetBytes(12);
        binding = Convert.FromHexString(_resistedLegacyPackHash);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, binding);
        CryptographicOperations.ZeroMemory(plaintext);
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(directory, _databaseName), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = _resistedRewriteSql;
        command.Parameters.AddWithValue(_resistedBindingParameter, binding);
        command.Parameters.AddWithValue(_resistedNonceParameter, nonce);
        command.Parameters.AddWithValue(_resistedTagParameter, tag);
        command.Parameters.AddWithValue(_resistedCipherParameter, ciphertext);
        Assert.Equal(1, command.ExecuteNonQuery());
    }
}
