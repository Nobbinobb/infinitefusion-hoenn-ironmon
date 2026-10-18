using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using System.Security.Cryptography;
using System.Text;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks achievement transactions through the real collection writer and SQLite storage.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _rivalClass = "RIVAL1";
    private const string _wallyClass = "RIVAL2";
    private const string _transitionEvent = "attempt.transition_resolved";
    private const string _manualReset = "manual_reset";
    private const string _successfulTransition = "success";
    private const string _unregisteredAchievementFixtureEvent = "unregistered.fixture";
    private const string _moveEvent = "move.finished";
    private const string _effect = "executed_effect";
    private const string _directInvocationKind = "direct";
    private const string _normal = "NORMAL";
    private const string _distinctMoves = """["TACKLE","GROWL","SCRATCH","POUND","BODYSLAM","HYPERBEAM","QUICKATTACK","SLASH","DOUBLEEDGE","MEGAPUNCH"]""";
    private const string _checkpointCipherSql = "SELECT ciphertext FROM private_consumers";
    private const string _checkpointTagSql = "SELECT auth_tag FROM private_consumers";
    private const string _achievementText = "Achievement";
    private const string _conditionText = "Win a rival battle";
    private const string _checkpointBindingSql = "SELECT pack_binding_token FROM private_consumers";
    private const string _checkpointNonceSql = "SELECT nonce FROM private_consumers";
    private const string _additionalMovePrefix = "FIXTURE_MOVE_";

    /// <summary>
    /// Unlocks both parallel first-win rules on the same rival victory and keeps Wally separate.
    /// </summary>
    /// <returns>A task completing after durable unlocks and replay idempotency are verified.</returns>
    [Fact]
    public async Task RivalVictoryUnlocksParallelRulesWithoutDuplicateNotices()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement batch = Batch(_stream, AchievementTrainer(_rivalClass, 1));
        await store.AcceptBatchAsync(batch);
        IReadOnlyList<UnlockedAchievement> earned = await store.GetAchievementsAsync();
        Assert.Contains(earned, item => item.Number == 31);
        Assert.Contains(earned, item => item.Number == 45);
        Assert.DoesNotContain(earned, item => item.Number == 40);
        await store.AcceptBatchAsync(batch);
        Assert.Equal(earned, await store.GetAchievementsAsync());
        Assert.Equal(earned, await store.GetAchievementsAsync(true));
        byte[] ciphertext = Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointCipherSql));
        Assert.DoesNotContain(_achievementText, Encoding.UTF8.GetString(ciphertext));
        Assert.DoesNotContain(_conditionText, Encoding.UTF8.GetString(ciphertext));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Recovers durable notices after a writer restart and deletes only acknowledged delivery state.
    /// </summary>
    /// <returns>A task completing after reopening the real database twice.</returns>
    [Fact]
    public async Task UnlocksAndPendingNoticesSurviveRestartAndAcknowledgement()
    {
        using TestDirectory directory = new();
        IReadOnlyList<UnlockedAchievement> original;
        await using (GameplayCollectionStore first = new(directory.Path))
        {
            await first.AcceptBatchAsync(Batch(_stream, AchievementTrainer(_rivalClass, 1)));
            original = await first.GetAchievementsAsync(true);
        }

        await using (GameplayCollectionStore second = new(directory.Path))
        {
            Assert.Equal(original, await second.GetAchievementsAsync(true));
            HashSet<Guid> displayed = [.. original.Select(item => item.Id)];
            await second.AcknowledgeAchievementsAsync(displayed);
            await second.AcknowledgeAchievementsAsync(displayed);
            Assert.Empty(await second.GetAchievementsAsync(true));
        }

        await using GameplayCollectionStore third = new(directory.Path);
        Assert.Equal(original, await third.GetAchievementsAsync());
        Assert.Empty(await third.GetAchievementsAsync(true));
    }

    /// <summary>
    /// Cascades several thresholds in one committed batch without another gameplay event.
    /// </summary>
    /// <returns>A task completing after exact trainer and per-attempt milestones are checked.</returns>
    [Fact]
    public async Task OneBatchCascadesEveryReachedThreshold()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [.. TrainerWins(50, 1)]));
        int[] numbers = [.. (await store.GetAchievementsAsync()).Select(item => item.Number)];
        Assert.All([45, 49, 58, 59, 60], number => Assert.Contains(number, numbers));
        Assert.DoesNotContain(46, numbers);
        Assert.DoesNotContain(47, numbers);
        Assert.DoesNotContain(48, numbers);
        Assert.DoesNotContain(50, numbers);
    }

    /// <summary>
    /// Keeps Wally's native rival class out of the main-rival victory population.
    /// </summary>
    /// <returns>A task completing after the generic victory is earned without a main-rival unlock.</returns>
    [Fact]
    public async Task WallyVictoryDoesNotEarnMainRivalAchievement()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, AchievementTrainer(_wallyClass, 1)));
        IReadOnlyList<UnlockedAchievement> earned = await store.GetAchievementsAsync();
        Assert.Contains(earned, item => item.Number == 45);
        Assert.DoesNotContain(earned, item => item.Number == 31);
    }

    /// <summary>
    /// Counts committed reset transitions and keeps their earned chain across the reset boundary.
    /// </summary>
    /// <returns>A task completing after the first reset and the later fifty-reset milestone are verified.</returns>
    [Fact]
    public async Task ResetTransitionsAccumulatePermanentMilestones()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement payload = JsonSerializer.SerializeToElement(new { kind = _manualReset, outcome = _successfulTransition });
        await store.AcceptBatchAsync(Batch(_stream, [.. Enumerable.Range(1, 5).Select(sequence => Occurrence(_transitionEvent, payload, sequence))]));
        IReadOnlyList<UnlockedAchievement> earned = await store.GetAchievementsAsync();
        Assert.Contains(earned, item => item.Number == 20);
        Assert.DoesNotContain(earned, item => item.Number == 24);
        await store.AcceptBatchAsync(Batch(_stream, [.. Enumerable.Range(6, 44).Select(sequence => Occurrence(_transitionEvent, payload, sequence))]));
        Assert.DoesNotContain(await store.GetAchievementsAsync(), item => item.Number == 24);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_transitionEvent, payload, 50)]));
        Assert.Contains(await store.GetAchievementsAsync(), item => item.Number == 24);
        Assert.DoesNotContain(await store.GetAchievementsAsync(), item => item.Number == 25);
    }

    /// <summary>
    /// Rolls back unlock evidence together with a rejected collection batch.
    /// </summary>
    /// <returns>A task completing after rejected data earns nothing and a corrected replay succeeds.</returns>
    [Fact]
    public async Task RejectedBatchCannotLeavePhantomUnlocks()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement[] wins = AchievementTrainer(_rivalClass, 1);
        JsonElement invalid = Occurrence(_unregisteredAchievementFixtureEvent, JsonSerializer.Deserialize<JsonElement>(_emptyPayload), 3);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcceptBatchAsync(Batch(_stream, [.. wins, invalid])));
        Assert.Empty(await store.GetAchievementsAsync());
        await store.AcceptBatchAsync(Batch(_stream, wins));
        Assert.Contains(await store.GetAchievementsAsync(), item => item.Number == 31);
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Keeps distinct-move collection independent of collecting five different move types.
    /// </summary>
    /// <returns>A task completing after seventy-five successful distinct moves of one type earn the collection.</returns>
    [Fact]
    public async Task DistinctMovesDoNotRequireFiveDifferentTypes()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        string[] moves = [.. JsonSerializer.Deserialize<string[]>(_distinctMoves)!, .. Enumerable.Range(1, 65).Select(index => _additionalMovePrefix + index)];
        JsonElement[] events = [.. moves.Select((move, index) => Occurrence(_moveEvent, JsonSerializer.SerializeToElement(new { actor = new { owner_role = _player }, move_id = move, result = _effect, positive_hp_targets = 1, positive_substitute_targets = 0, hit_iterations_executed = 1, resolved_move_metadata = new { type_id = _normal, execution_origin = _directInvocationKind }, invocation_context = new { invocation_kind = _directInvocationKind } }), index + 1))];
        await store.AcceptBatchAsync(Batch(_stream, events[..74]));
        Assert.DoesNotContain(await store.GetAchievementsAsync(), item => item.Number is 95 or 113);
        await store.AcceptBatchAsync(Batch(_stream, events[74..]));
        IReadOnlyList<UnlockedAchievement> earned = await store.GetAchievementsAsync();
        Assert.DoesNotContain(earned, item => item.Number == 95);
        Assert.Contains(earned, item => item.Number == 113);
    }

    /// <summary>
    /// Authenticates the real private checkpoint under its collection identity and pack binding.
    /// </summary>
    /// <returns>A task completing after authenticated decryption and corruption rejection are checked.</returns>
    [Fact]
    public async Task PrivateCheckpointIsAuthenticatedAndContainsOnlyEarnedIds()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, AchievementTrainer(_rivalClass, 1)));
        byte[] ciphertext = Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointCipherSql));
        byte[] tag = Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointTagSql));
        byte[] nonce = Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointNonceSql));
        byte[] binding = Assert.IsType<byte[]>(ReadScalar(directory.Path, _checkpointBindingSql));
        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm cipher = new(AchievementPack.StateKey(Convert.FromHexString(_profile), Convert.FromHexString(_epoch)), 16);
        cipher.Decrypt(nonce, ciphertext, tag, plaintext, binding);
        AchievementCheckpoint checkpoint = JsonSerializer.Deserialize<AchievementCheckpoint>(plaintext)!;
        Assert.Equal((await store.GetAchievementsAsync()).Select(item => item.Id).Order(), checkpoint.Earned.Keys.Order());
        Assert.Equal(checkpoint.Earned.Keys.Order(), checkpoint.Pending.Order());
        ciphertext[0] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(nonce, ciphertext, tag, plaintext, binding));
    }

    /// <summary>
    /// Constructs a native-shaped trainer admission and victory sharing one exact battle.
    /// </summary>
    /// <param name="trainerClass">The actual native trainer class identity.</param>
    /// <param name="sequence">The first contiguous delivery sequence.</param>
    /// <returns>The two authoritative trainer battle occurrences.</returns>
    private static JsonElement[] AchievementTrainer(string trainerClass, int sequence)
        => [Occurrence(_battleStartedEvent, JsonSerializer.SerializeToElement(new { battle_kind = _trainerKind, encounter = new { trainer_class_ids = new[] { trainerClass }, authored_entry_ids = Array.Empty<string>() } }), sequence), Occurrence(_battleEndedEvent, JsonSerializer.SerializeToElement(new { outcome = _winOutcome }), sequence + 1)];
}
