using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.History;
using System.Security.Cryptography;
using System.Text;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Exercises encrypted history maintenance against actual collection receipts and later gameplay.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _historyPool = "pool";
    private const string _historyPokemon = "pokemon";
    private const string _historyMagic = "IRMHIST2";
    private const string _historyLegacyMagic = "IRMHIST1";
    private const string _historyPlaintext = "Private history must remain encrypted outside the tracker.";
    private const string _historyProfileProperty = "profile_id";
    private const string _historyEpochProperty = "epoch_id";
    private const string _historyIdentityFormat = "N";
    private const string _historyFilename = "history.irmhistory";
    private const string _historySecondFilename = "history-second.irmhistory";
    private const string _historyLifetimeDamageSql = "SELECT SUM(c.value_blob) FROM aggregate_cells c JOIN aggregate_series a USING(series_id) JOIN aggregate_families f USING(family_id) JOIN hot_partitions p USING(partition_id) JOIN scopes s USING(scope_id) WHERE f.family_key='actual_hp_lost' AND s.scope_kind='profile_lifetime'";
    private const string _historyDirectDamageSql = "SELECT value_i64 FROM direct_scalar_values v JOIN direct_groups g USING(group_id) WHERE g.projection_key='actual_hp_lost+substitute_hp_damage'";
    private const string _historyConsumerSql = "SELECT ciphertext FROM private_consumers";
    private const string _historyMeasureCountSql = "SELECT COUNT(*) FROM aggregate_cells";
    private const string _historyRecipeName = "recipe.json";
    private const string _historyRecipeOriginal = "{\"run_id\":\"old-run\"}";
    private const string _historyRecipeNewer = "{\"run_id\":\"newer-run\"}";
    private const string _historyWalSuffix = "-wal";
    private const string _historyShmSuffix = "-shm";

    /// <summary>
    /// Keeps live records and recipes unchanged during review and never overstates the completed export's net savings.
    /// </summary>
    /// <param name="lockRecipe">Whether recipe deletion is prevented during the real export.</param>
    /// <returns>The asynchronous preview and measured-export scenario.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryReviewMeasuresConservativeDiskSavingsWithoutChangingTheProfile(bool lockRecipe)
    {
        using TestDirectory directory = new();
        using TestDirectory external = new();
        using TestDirectory runs = new();
        string recipe = Path.Combine(runs.Path, _historyRecipeName);
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path, runs.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
        await File.WriteAllTextAsync(recipe, _historyRecipeOriginal);
        using FileStream? recipeLock = lockRecipe ? new(recipe, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        string databasePath = Path.Combine(directory.Path, _databaseName);
        byte[] database = ReadHistoryLiveFile(databasePath);
        byte[] wal = ReadHistoryLiveFile(databasePath + _historyWalSuffix);
        long before = HistoryDatabaseStorageBytes(directory.Path);
        Assert.NotEmpty(wal);

        HistoryMaintenanceResult review = await store.InspectHistoryAsync();

        Assert.Equal(database, ReadHistoryLiveFile(databasePath));
        Assert.Equal(wal, ReadHistoryLiveFile(databasePath + _historyWalSuffix));
        Assert.Equal(_historyRecipeOriginal, await File.ReadAllTextAsync(recipe));
        Assert.Equal(40L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        Assert.Equal(before, review.DatabaseBytes);
        Assert.True(review.ArchiveBytes > 0);
        Assert.True(review.EstimatedFreedBytes > 0);
        Assert.Equal(0, review.RemovedBytes);
        Assert.False(File.Exists(archive));
        HistoryMaintenanceResult result = await store.ExportHistoryAsync(archive);
        long removedRecipeBytes = lockRecipe ? 0 : Encoding.UTF8.GetByteCount(_historyRecipeOriginal);
        Assert.Equal(before - HistoryDatabaseStorageBytes(directory.Path) + removedRecipeBytes, result.RemovedBytes);
        Assert.True(review.EstimatedFreedBytes <= Math.Max(0, result.RemovedBytes - result.ArchiveBytes));
        Assert.Equal(lockRecipe, File.Exists(recipe));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Shows zero net savings when an incompressible archive costs more than compaction can recover.
    /// </summary>
    /// <returns>The asynchronous archive-cost preview scenario.</returns>
    [Fact]
    public async Task HistoryReviewIncludesArchiveStorageWhenNoNetSpaceCanBeFreed()
    {
        using TestDirectory directory = new();
        using TestDirectory runs = new();
        await using (GameplayCollectionStore initial = new(directory.Path))
            await initial.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));

        string recipe = Path.Combine(runs.Path, _historyRecipeName);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(RandomNumberGenerator.GetBytes(2 * 1024 * 1024)) });
        await File.WriteAllBytesAsync(recipe, payload);
        await using GameplayCollectionStore store = new(directory.Path, runs.Path);
        await store.GetMeasurementsAsync();
        HistoryMaintenanceResult review = await store.InspectHistoryAsync();
        Assert.True(review.ArchiveBytes > review.DatabaseBytes);
        Assert.Equal(0L, review.EstimatedFreedBytes);
        Assert.Equal(payload, await File.ReadAllBytesAsync(recipe));
        Assert.Equal(40L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
    }

    /// <summary>
    /// Returns an empty review without attempting an export from an unbound profile.
    /// </summary>
    /// <returns>The asynchronous empty-profile review scenario.</returns>
    [Fact]
    public async Task HistoryReviewOfAnEmptyProfileReportsNoDiskSavings()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        HistoryMaintenanceResult review = await store.InspectHistoryAsync();
        Assert.Equal(0L, review.EligibleBytes);
        Assert.Equal(0L, review.ArchiveBytes);
        Assert.Equal(0L, review.EstimatedFreedBytes);
    }

    /// <summary>
    /// Reads physical database and journal bytes without altering SQLite checkpoint state.
    /// </summary>
    /// <param name="directory">The owned collection profile directory.</param>
    /// <returns>The combined database, WAL and shared-memory file lengths.</returns>
    private static long HistoryDatabaseStorageBytes(string directory)
    {
        string database = Path.Combine(directory, _databaseName);
        string[] paths = [database, database + _historyWalSuffix, database + _historyShmSuffix];
        return paths.Sum(path => File.Exists(path) ? new FileInfo(path).Length : 0L);
    }

    /// <summary>
    /// Snapshots a live SQLite file without denying the writer's existing native file-sharing access.
    /// </summary>
    /// <param name="path">The owned database or WAL file.</param>
    /// <returns>The complete current file bytes.</returns>
    private static byte[] ReadHistoryLiveFile(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Restores removed history without changing achievement evidence, losing later gameplay or counting a second import.
    /// </summary>
    /// <returns>The asynchronous real database scenario.</returns>
    [Fact]
    public async Task HistoryExportPreservesAchievementsAndImportMergesLaterGameplayExactlyOnce()
    {
        using TestDirectory directory = new();
        using TestDirectory external = new();
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
        var earned = await store.GetAchievementsAsync();
        byte[] ledger = Assert.IsType<byte[]>(ReadScalar(directory.Path, _historyConsumerSql));
        Assert.Equal(40L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        HistoryMaintenanceResult result = await store.ExportHistoryAsync(archive);
        Assert.True(result.ArchiveBytes > 0);
        Assert.True(result.EligibleBytes > 0);
        Assert.Equal(40L, ReadScalar(directory.Path, _historyDirectDamageSql));
        Assert.Equal(ledger, Assert.IsType<byte[]>(ReadScalar(directory.Path, _historyConsumerSql)));
        Assert.Equal(0L, ReadScalar(directory.Path, _historyMeasureCountSql));
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(60), 2)]));
        Assert.Equal(60L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        await store.ImportHistoryAsync(archive);
        Assert.Equal(100L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        Assert.Equal(100L, ReadScalar(directory.Path, _historyDirectDamageSql));
        Assert.Equal(earned, await store.GetAchievementsAsync());
        Assert.True((await store.ImportHistoryAsync(archive)).AlreadyImported);
        Assert.Equal(100L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Rejects corrupted files without changing current rows or receipts.
    /// </summary>
    /// <returns>The asynchronous authenticated import scenario.</returns>
    [Fact]
    public async Task HistoryTamperingLeavesTheProfileUnchanged()
    {
        using TestDirectory directory = new();
        using TestDirectory external = new();
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
        await store.ExportHistoryAsync(archive);
        byte[] file = File.ReadAllBytes(archive);
        file[^1] ^= 1;
        File.WriteAllBytes(archive, file);
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => store.ImportHistoryAsync(archive));
        Assert.Equal(0L, ReadScalar(directory.Path, _historyMeasureCountSql));
        Assert.Equal(40L, ReadScalar(directory.Path, _historyDirectDamageSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Handles multiple exports in reverse import order without requiring retained plaintext history.
    /// </summary>
    /// <returns>The asynchronous multi-archive scenario.</returns>
    [Fact]
    public async Task HistoryMultipleArchivesCanBeImportedInReverseOrderAfterRestart()
    {
        using TestDirectory directory = new();
        using TestDirectory external = new();
        string first = Path.Combine(external.Path, _historyFilename);
        string second = Path.Combine(external.Path, _historySecondFilename);
        await using (GameplayCollectionStore initial = new(directory.Path))
        {
            await initial.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
            await initial.ExportHistoryAsync(first);
            await initial.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(60), 2)]));
            await initial.ExportHistoryAsync(second);
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        await reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(20), 3)]));
        await reopened.ImportHistoryAsync(second);
        await reopened.ImportHistoryAsync(first);
        Assert.Equal(120L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        Assert.Equal(120L, ReadScalar(directory.Path, _historyDirectDamageSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Authenticates chunk order and truncation even when archives span multiple encryption chunks.
    /// </summary>
    [Fact]
    public void HistoryCipherRoundTripsLargeStreamsAndRejectsTruncation()
    {
        using TestDirectory directory = new();
        string path = Path.Combine(directory.Path, _historyFilename);
        byte[] original = RandomNumberGenerator.GetBytes(800000);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        using (HistoryArchiveCipher writer = new(path, key, true))
        {
            writer.Write(original);
            writer.Complete();
        }

        using (HistoryArchiveCipher reader = new(path, key, false))
        {
            using MemoryStream output = new();
            reader.CopyTo(output);
            reader.Complete();
            Assert.Equal(original, output.ToArray());
        }

        byte[] truncated = File.ReadAllBytes(path)[..^20];
        File.WriteAllBytes(path, truncated);
        using HistoryArchiveCipher invalid = new(path, key, false);
        Assert.Throws<EndOfStreamException>(() => invalid.CopyTo(Stream.Null));
    }

    /// <summary>
    /// Uses fresh archive protection and refuses unrelated keys or the old password-based format.
    /// </summary>
    [Fact]
    public void HistoryCipherUsesFreshProtectionAndRejectsWrongKeysAndLegacyArchives()
    {
        using TestDirectory directory = new();
        string firstPath = Path.Combine(directory.Path, _historyFilename);
        string secondPath = Path.Combine(directory.Path, _historySecondFilename);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] original = Encoding.UTF8.GetBytes(_historyPlaintext);
        string[] paths = [firstPath, secondPath];
        foreach (string path in paths)
        {
            using HistoryArchiveCipher writer = new(path, key, true);
            writer.Write(original);
            writer.Complete();
        }

        byte[] first = File.ReadAllBytes(firstPath);
        byte[] second = File.ReadAllBytes(secondPath);
        Assert.Equal(_historyMagic, Encoding.ASCII.GetString(first, 0, 8));
        Assert.False(first.AsSpan(8, 24).SequenceEqual(second.AsSpan(8, 24)));
        Assert.Equal(-1, first.AsSpan().IndexOf(original));
        using (HistoryArchiveCipher wrongKey = new(firstPath, RandomNumberGenerator.GetBytes(32), false))
            Assert.Throws<AuthenticationTagMismatchException>(() => wrongKey.CopyTo(Stream.Null));

        first[8] ^= 1;
        File.WriteAllBytes(firstPath, first);
        using (HistoryArchiveCipher changedHeader = new(firstPath, key, false))
            Assert.Throws<AuthenticationTagMismatchException>(() => changedHeader.CopyTo(Stream.Null));

        Encoding.ASCII.GetBytes(_historyLegacyMagic).CopyTo(first, 0);
        File.WriteAllBytes(firstPath, first);
        Assert.Throws<InvalidDataException>(() => new HistoryArchiveCipher(firstPath, key, false));
    }

    /// <summary>
    /// Rejects another profile or epoch cryptographically before restoring any archived rows.
    /// </summary>
    /// <param name="changedBinding">The source binding that differs from the exported archive.</param>
    /// <returns>The asynchronous profile-bound archive scenario.</returns>
    [Theory]
    [InlineData(_historyProfileProperty)]
    [InlineData(_historyEpochProperty)]
    public async Task HistoryArchivesRejectDifferentProfileOrEpochBeforeMutation(string changedBinding)
    {
        using TestDirectory directory = new();
        using TestDirectory unrelated = new();
        using TestDirectory external = new();
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
        await store.ExportHistoryAsync(archive);
        var otherBatch = JsonSerializer.SerializeToNode(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(20), 1)]))!.AsObject();
        otherBatch[changedBinding] = Guid.NewGuid().ToString(_historyIdentityFormat);
        await using GameplayCollectionStore other = new(unrelated.Path);
        await other.AcceptBatchAsync(JsonSerializer.SerializeToElement(otherBatch));
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => other.ImportHistoryAsync(archive));
        Assert.Equal(20L, ReadScalar(unrelated.Path, _historyLifetimeDamageSql));
        Assert.Equal(20L, ReadScalar(unrelated.Path, _historyDirectDamageSql));
        AssertIntegrity(unrelated.Path);
    }

    /// <summary>
    /// Restores archived recipe files while preserving a newer file with the same name.
    /// </summary>
    /// <returns>The asynchronous recipe archive scenario.</returns>
    [Fact]
    public async Task HistoryRecipeFilesAreRemovedAfterVerificationAndNewerFilesWinOnImport()
    {
        using TestDirectory directory = new();
        using TestDirectory external = new();
        using TestDirectory runs = new();
        string recipe = Path.Combine(runs.Path, _historyRecipeName);
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path, runs.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
        await File.WriteAllTextAsync(recipe, _historyRecipeOriginal);
        await store.ExportHistoryAsync(archive);
        Assert.False(File.Exists(recipe));
        await File.WriteAllTextAsync(recipe, _historyRecipeNewer);
        await store.ImportHistoryAsync(archive);
        Assert.Equal(_historyRecipeNewer, await File.ReadAllTextAsync(recipe));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Confirms unrelated direct summaries can be restored for later achievement definitions.
    /// </summary>
    /// <returns>The asynchronous unrelated-observation scenario.</returns>
    [Fact]
    public async Task HistoryUnrelatedDirectSummariesReturnWithAllLaterCounts()
    {
        using TestDirectory directory = new();
        using TestDirectory external = new();
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1)]));
        await store.ExportHistoryAsync(archive);
        Assert.Null(ReadScalar(directory.Path, _historyDirectDamageSql));
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(60), 2)]));
        await store.ImportHistoryAsync(archive);
        Assert.Equal(100L, ReadScalar(directory.Path, _historyDirectDamageSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Refuses existing archive paths and cross-profile imports without removing source history.
    /// </summary>
    /// <returns>The asynchronous export-failure and profile-binding scenario.</returns>
    [Fact]
    public async Task HistoryArchiveFailuresDoNotRemoveCurrentEvidence()
    {
        using TestDirectory directory = new();
        using TestDirectory unrelated = new();
        using TestDirectory external = new();
        string archive = Path.Combine(external.Path, _historyFilename);
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(40), 1)]));
        await File.WriteAllTextAsync(archive, _historyRecipeOriginal);
        await Assert.ThrowsAsync<IOException>(() => store.ExportHistoryAsync(archive));
        Assert.Equal(40L, ReadScalar(directory.Path, _historyLifetimeDamageSql));
        File.Delete(archive);
        await store.ExportHistoryAsync(archive);
        await using GameplayCollectionStore other = new(unrelated.Path);
        await other.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, HistoryDamage(20), 1)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => other.ImportHistoryAsync(archive));
        Assert.Equal(20L, ReadScalar(unrelated.Path, _historyLifetimeDamageSql));
        AssertIntegrity(directory.Path);
        AssertIntegrity(unrelated.Path);
    }
    /// <summary>
    /// Supplies the explicit HP pool required by the shipped damage achievements.
    /// </summary>
    /// <param name="amount">The actual HP loss.</param>
    /// <returns>A jointly complete native-shaped damage observation.</returns>
    private static JsonElement HistoryDamage(long amount)
    {
        var payload = JsonSerializer.SerializeToNode(Damage(amount))!.AsObject();
        payload[_historyPool] = _historyPokemon;
        return JsonSerializer.SerializeToElement(payload);
    }

}
