using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Collection.History;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;
using Ironmon.Tracker.Collection.Storage.Maintenance;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Detaches unrelated history while retaining stable identities, achievements and live checkpoints.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _historyHeader = "header";
    private const string _historyFrame = "frame";
    private const string _historyClosedFrame = "closed_frame";
    private const string _historyDirect = "direct";
    private const string _historyRecipe = "recipe";
    private const string _historyExports = "history_exports";
    private const string _historyImports = "history_imports";
    private const string _historyRoots = "history_value_roots";
    private const string _historyHot = "hot_partitions";
    private const string _historyCold = "cold_partitions";
    private const string _historyStates = "direct_measure_states";
    private const string _historyScalars = "direct_scalar_values";
    private const string _historyMembers = "direct_exact_set_members";
    private const string _historySeries = "series_id";
    private const string _historyGroup = "group_id";
    private const string _historyMeasure = "measure_no";
    private const string _historyKnown = "known";
    private const string _historyUnknown = "unknown";
    private const string _historyUnsupported = "unsupported";
    private const string _historyNotApplicable = "not_applicable";
    private const string _historyIntegerSuffix = "_count_i64";
    private const string _historyWideSuffix = "_count_decimal";
    private const string _historyValueInteger = "value_i64";
    private const string _historyValueWide = "value_decimal";
    private const string _historyMember = "member_value_id";
    private const string _historyJson = ".json";
    private const string _historyJsonPattern = "*.json";
    private const string _historyPendingSuffix = ".pending";
    private const string _historyIdentityFormat = "N";
    private const string _historyFieldName = "name";
    private const string _historyFieldPayload = "payload";
    private const string _historyFieldFamily = "family";
    private const string _historyFieldScope = "scope";
    private const string _historyFieldScopeId = "scope_id";
    private const string _historyFieldFamilyId = "family_id";
    private const string _historyFieldBaseBlockId = "base_block_id";
    private const string _historyFieldPartitionId = "partition_id";
    private const string _historyFieldState = "state";
    private const string _historyFieldScalars = "scalars";
    private const string _historyFieldMembers = "members";
    private const string _historyFieldArchiveId = "archive_id";
    private const string _historyFieldValueId = "value_id";
    private const string _historyFieldMeasureKind = "measure_kind";
    private const int _historyMaximumRecord = 128 * 1024 * 1024;
    private readonly Dictionary<string, IReadOnlyList<SqliteColumnInfo>> _historyColumns = [];

    /// <summary>
    /// Carries one bounded archive record; no achievement definitions are serialized.
    /// </summary>
    /// <remarks>
    /// Initializes an exact tagged record for authenticated streaming.
    /// </remarks>
    /// <param name="Kind">The fixed format record kind.</param>
    /// <param name="Data">The complete lossless record payload.</param>
    private sealed record HistoryRecord(string Kind, JsonElement Data);

    /// <summary>
    /// Binds an archive to the originating profile and compatible generic catalogue.
    /// </summary>
    /// <remarks>
    /// Initializes an encrypted archive manifest without copying achievement progress.
    /// </remarks>
    /// <param name="Id">The independent exactly-once import identity.</param>
    /// <param name="Profile">The original collection profile.</param>
    /// <param name="Epoch">The original tracking epoch.</param>
    /// <param name="Bindings">Stable generic definition digests; additive catalogue updates remain compatible.</param>
    private sealed record HistoryHeader(Guid Id, string Profile, string Epoch, Dictionary<string, string> Bindings);

    private const string _historyFamilyBinding = "family:";
    private const string _historyGroupBinding = "group:";

    /// <summary>
    /// Binds only existing generic definitions so adding new achievements or projections does not reject old archives.
    /// </summary>
    /// <returns>Stable identities and definition digests without private achievement definitions.</returns>
    private Dictionary<string, string> HistoryBindings()
    {
        Dictionary<string, string> bindings = _catalogue.Families.ToDictionary(family => _historyFamilyBinding + family.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), family => Convert.ToHexString(SHA256.HashData(CollectionEncoding.Canonical(family.Definition))));
        foreach (var group in _directGroups.Values)
            bindings.Add(_historyGroupBinding + group.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(SHA256.HashData(CollectionEncoding.Canonical(group.Definition))));

        return bindings;
    }

    /// <summary>
    /// Reads the exact physical row values with generated columns omitted.
    /// </summary>
    /// <param name="table">A fixed maintenance-owned table name.</param>
    /// <returns>Complete stored rows as exact typed tagged values.</returns>
    private IEnumerable<Dictionary<string, JsonElement>> HistoryRows(string table)
    {
        if (!_historyColumns.TryGetValue(table, out var columns))
        {
            columns = CollectionSqliteMaintenance.ReadTableColumns(_db, _transaction, table);
            _historyColumns[table] = columns;
        }

        foreach (object[] row in CollectionSqliteMaintenance.StreamTableRows(_db, _transaction, table))
            yield return columns.Where(column => column.Hidden == 0).ToDictionary(column => column.Name, column => Pack(row[checked((int)column.Ordinal)]));
    }

    /// <summary>
    /// Inserts metadata using the provider's generated exact-row operation.
    /// </summary>
    /// <param name="table">The fixed metadata table.</param>
    /// <param name="values">The complete stored values in declared order.</param>
    private void HistoryInsert(string table, params object[] values)
        => CollectionSqliteMaintenance.InsertRebuiltRow(_db, _transaction, table, CollectionSqliteMaintenance.ReadTableColumns(_db, _transaction, table), values);

    /// <summary>
    /// Protects complete observations used by any shipped rule, including locked achievements.
    /// </summary>
    /// <returns>Series that must remain fully resident.</returns>
    private HashSet<long> HistoryProtectedSeries()
    {
        AchievementPack pack = new();
        HashSet<Guid> all = [.. pack.Nodes.Select(node => node.Id)];
        AchievementRule[] rules = [.. pack.Nodes.Select(node => pack.Open(node.Id, all)).Where(rule => rule.Available)];
        HashSet<long> result = [];
        Dictionary<long, JsonElement> decoded = [];
        foreach (var group in rules.GroupBy(rule => rule.Group))
        {
            foreach (var pair in ReadAchievementEvidenceRows(group.Key, decoded))
            {
                if (group.Any(rule => AchievementEvaluator.Matches(rule.Filter, pair.Value)))
                    result.Add(pair.Key);
            }
        }

        return result;
    }

    /// <summary>
    /// Streams complete unrelated family frames and unrelated additive or set observations.
    /// </summary>
    /// <returns>Eligible history only; live ordered state and identity scaffolding remain resident.</returns>
    /// <param name="runDirectory">Optional tracker-owned recipe root.</param>
    private IEnumerable<HistoryRecord> HistoryRecords(string? runDirectory = null)
    {
        if (runDirectory is not null && Directory.Exists(runDirectory))
        {
            EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string path in Directory.EnumerateFiles(runDirectory, _historyJsonPattern, options))
            {
                string name = Path.GetRelativePath(runDirectory, path);
                byte[] payload = File.ReadAllBytes(path);
                yield return new(_historyRecipe, JsonSerializer.SerializeToElement(new { name, payload }));
            }
        }
        foreach (var row in HistoryRows(_historyCold))
        {
            int family = checked((int)(long)Unpack(row[_historyFieldFamilyId]));
            long block = (long)Unpack(row[_historyFieldBaseBlockId]);
            yield return new(_historyClosedFrame, ReadArchiveFrame(block, family));
        }

        foreach (var row in HistoryRows(_historyHot))
        {
            long scope = (long)Unpack(row[_historyFieldScopeId]);
            int family = checked((int)(long)Unpack(row[_historyFieldFamilyId]));
            long partition = (long)Unpack(row[_historyFieldPartitionId]);
            JsonElement frame = ReadPartition(scope, family, partition);
            if (frame.GetProperty(_archiveSeriesProperty).GetArrayLength() > 0)
                yield return new(_historyFrame, JsonSerializer.SerializeToElement(new { format = 2, family, scope, series = frame.GetProperty(_archiveSeriesProperty), ordered = Array.Empty<object>() }));
        }

        HashSet<long> protectedSeries = HistoryProtectedSeries();
        Dictionary<int, JsonElement> groups = _directGroups.Values.ToDictionary(group => group.Id, group => group.Definition);
        var scalars = HistoryRows(_historyScalars).ToLookup(row => (long)Unpack(row[_historySeries]));
        var members = HistoryRows(_historyMembers).ToLookup(row => (long)Unpack(row[_historySeries]));
        foreach (var state in HistoryRows(_historyStates))
        {
            long series = (long)Unpack(state[_historySeries]);
            if (protectedSeries.Contains(series))
                continue;

            int group = checked((int)(long)Unpack(state[_historyGroup]));
            int measure = checked((int)(long)Unpack(state[_historyMeasure]));
            JsonElement definition = groups[group].GetProperty(_measuresProperty)[measure];
            string kind = definition.GetProperty(_kindProperty).GetString()!;
            if (kind != _exactSet && (kind != _scalar || definition.GetProperty(_mergeProperty).GetString() != _sum))
                continue;

            var actualScalars = scalars[series].Where(row => (long)Unpack(row[_historyMeasure]) == measure).ToArray();
            var actualMembers = members[series].Where(row => (long)Unpack(row[_historyMeasure]) == measure).ToArray();
            yield return new(_historyDirect, JsonSerializer.SerializeToElement(new { state, scalars = actualScalars, members = actualMembers }));
        }
    }

    /// <summary>
    /// Measures conservative disk savings on an isolated database copy without changing progress or source data.
    /// </summary>
    /// <returns>Current physical storage, measured archive size and conservative net disk savings.</returns>
    /// <param name="runDirectory">Optional tracker-owned recipe root.</param>
    internal JsonElement InspectHistory(string? runDirectory)
    {
        long bytes = HistoryRecords(runDirectory).Sum(record => (long)JsonSerializer.SerializeToUtf8Bytes(record).Length);
        HistoryMaintenanceResult result = PreviewHistoryStorage(runDirectory, bytes);
        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>
    /// Creates and verifies an encrypted archive before transactionally detaching its eligible records.
    /// </summary>
    /// <param name="path">A new external archive destination.</param>
    /// <returns>Actual storage measurements after reclaiming database pages.</returns>
    /// <param name="runDirectory">Optional tracker-owned recipe root.</param>
    /// <param name="removeRecipes">Whether verified recipes may be removed; false for isolated storage previews.</param>
    internal JsonElement ExportHistory(string path, string? runDirectory, bool removeRecipes = true)
    {
        if (_profile is null || _epoch is null)
            throw new InvalidOperationException("No tracked gameplay is available to archive.");

        path = Path.GetFullPath(path);
        if (path.StartsWith(Path.GetDirectoryName(_databasePath)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an archive location outside the active collection folder.");
        if (File.Exists(path) && new FileInfo(path).Length != 0)
            throw new IOException("Choose a new archive filename. Existing archives are never overwritten.");

        long before = HistoryStorageBytes(_databasePath);
        string pending = path + Guid.NewGuid().ToString(_historyIdentityFormat) + _historyPendingSuffix;
        HistoryHeader header = new(Guid.NewGuid(), Convert.ToHexString(_profile), Convert.ToHexString(_epoch), HistoryBindings());
        long bytes = 0;
        try
        {
            using (HistoryArchiveCipher cipher = OpenHistoryArchive(pending, true))
            {
                using (BrotliStream compressed = new(cipher, CompressionLevel.SmallestSize, true))
                using (BinaryWriter writer = new(compressed, System.Text.Encoding.UTF8, true))
                {
                    WriteHistoryRecord(writer, new(_historyHeader, JsonSerializer.SerializeToElement(header)));
                    foreach (HistoryRecord record in HistoryRecords(runDirectory))
                        bytes += WriteHistoryRecord(writer, record);

                    if (bytes == 0)
                        throw new InvalidOperationException("No eligible unrelated history is currently available to export.");
                    writer.Write(0);
                }
                cipher.Complete();
            }

            ReadHistory(pending, null);
            File.Move(pending, path, true);
            using SqliteTransaction transaction = _db.BeginTransaction();
            _transaction = transaction;
            try
            {
                HistoryInsert(_historyExports, header.Id.ToByteArray(), Now());
                ReadHistory(path, record => RemoveHistory(record, header.Id));
                CollectionSqliteMaintenance.AssertForeignKeyIntegrity(_db, _transaction, "History export would invalidate a retained relationship.");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                _transaction = null;
                _archiveReadCache.Clear();
                _checkedHotPartitions.Clear();
                _values.ClearCaches();
            }

            CollectionSqliteMaintenance.CheckpointTruncate(_db, null);
            CollectionSqliteMaintenance.Vacuum(_db, null);
            CollectionSqliteMaintenance.CheckpointTruncate(_db, null);
            long after = HistoryStorageBytes(_databasePath);
            long removedRecipes = 0;
            bool retainedRecipes = false;
            if (removeRecipes)
            {
                ReadHistory(path, record =>
                {
                    if (record.Kind != _historyRecipe)
                        return;
                    string file = HistoryRecipePath(runDirectory, record.Data.GetProperty(_historyFieldName).GetString()!);
                    byte[] original = record.Data.GetProperty(_historyFieldPayload).GetBytesFromBase64();
                    try
                    {
                        if (File.Exists(file) && File.ReadAllBytes(file).AsSpan().SequenceEqual(original))
                        {
                            File.Delete(file);
                            removedRecipes += original.LongLength;
                        }
                        else if (File.Exists(file))
                        {
                            retainedRecipes = true;
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        retainedRecipes = true;
                    }
                });
            }

            return JsonSerializer.SerializeToElement(new HistoryMaintenanceResult(after, new FileInfo(path).Length, before - after + removedRecipes, bytes, Notice: retainedRecipes ? "Some newer or locked run files were retained. Their archived versions are safe in the export." : null));
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }

    /// <summary>
    /// Restores an authenticated history once while merging newer counters and preserving permanent unlocks.
    /// </summary>
    /// <param name="path">The external encrypted archive.</param>
    /// <returns>Storage measurements and an explicit already-imported outcome.</returns>
    /// <param name="runDirectory">Optional tracker-owned recipe root.</param>
    internal JsonElement ImportHistory(string path, string? runDirectory)
    {
        HistoryHeader header = ReadHistory(path, null);
        byte[] id = header.Id.ToByteArray();
        if (!HistoryRows(_historyExports).Any(row => ((byte[])Unpack(row[_historyFieldArchiveId])).AsSpan().SequenceEqual(id)))
            throw new InvalidDataException("This archive was not exported from this active profile.");

        if (HistoryRows(_historyImports).Any(row => ((byte[])Unpack(row[_historyFieldArchiveId])).AsSpan().SequenceEqual(id)))
            return JsonSerializer.SerializeToElement(new HistoryMaintenanceResult(HistoryStorageBytes(_databasePath), new FileInfo(path).Length, 0, 0, true));

        using SqliteTransaction transaction = _db.BeginTransaction();
        _transaction = transaction;
        List<string> restoredRecipes = [];
        try
        {
            ReadHistory(path, record =>
            {
                if (record.Kind == _historyRecipe)
                {
                    string file = HistoryRecipePath(runDirectory, record.Data.GetProperty(_historyFieldName).GetString()!);
                    if (File.Exists(file))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    using FileStream destination = new(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    restoredRecipes.Add(file);
                    destination.Write(record.Data.GetProperty(_historyFieldPayload).GetBytesFromBase64());
                    destination.Flush(true);
                }
                else
                {
                    RestoreHistory(record);
                }
            });
            RecloseContinuations();
            HistoryInsert(_historyImports, id, Now());
            CollectionSqliteMaintenance.AssertForeignKeyIntegrity(_db, _transaction, "The archive contains invalid retained relationships.");
            _achievementEvaluationRevision = null;
            EvaluateAchievements(false);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            _achievementPack = null;
            _achievementEvaluationRevision = null;
            foreach (string file in restoredRecipes)
                File.Delete(file);
            throw;
        }
        finally
        {
            _transaction = null;
            _archiveReadCache.Clear();
            _checkedHotPartitions.Clear();
            _values.ClearCaches();
            _thawedPartitions.Clear();
            _replacedColdBlocks.Clear();
        }

        CollectionSqliteMaintenance.CheckpointTruncate(_db, null);
        CollectionSqliteMaintenance.Vacuum(_db, null);
        CollectionSqliteMaintenance.CheckpointTruncate(_db, null);
        return JsonSerializer.SerializeToElement(new HistoryMaintenanceResult(HistoryStorageBytes(_databasePath), new FileInfo(path).Length, 0, 0));
    }

    /// <summary>
    /// Writes one bounded lossless record to the compressor.
    /// </summary>
    /// <param name="writer">The compressed authenticated destination.</param>
    /// <param name="record">The complete unrelated record.</param>
    /// <returns>The serialized record bytes before compression.</returns>
    private static int WriteHistoryRecord(BinaryWriter writer, HistoryRecord record)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > _historyMaximumRecord)
            throw new InvalidDataException("A history partition exceeds the supported archive record size; no data was removed.");

        writer.Write(bytes.Length);
        writer.Write(bytes);
        return bytes.Length;
    }

    /// <summary>
    /// Opens profile-bound archive protection without exposing or accepting a user password.
    /// </summary>
    /// <param name="path">The encrypted archive path.</param>
    /// <param name="writing">Whether to create a new archive.</param>
    /// <returns>The authenticated archive stream with its own independently derived key.</returns>
    private HistoryArchiveCipher OpenHistoryArchive(string path, bool writing)
    {
        if (_profile is null || _epoch is null)
            throw new InvalidOperationException("No tracked gameplay profile is available for this archive.");

        byte[] key = AchievementPack.StateKey(_profile, _epoch);
        try
        {
            return new(path, key, writing);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Authenticates an entire archive and optionally visits its compatible history records.
    /// </summary>
    /// <param name="path">The encrypted archive path.</param>
    /// <param name="visit">An optional transactional record consumer.</param>
    /// <returns>The verified compatible encrypted manifest.</returns>
    private HistoryHeader ReadHistory(string path, Action<HistoryRecord>? visit)
    {
        using HistoryArchiveCipher cipher = OpenHistoryArchive(path, false);
        using BrotliStream compressed = new(cipher, CompressionMode.Decompress, true);
        using BinaryReader reader = new(compressed);
        HistoryHeader? header = null;
        while (true)
        {
            int size = reader.ReadInt32();
            if (size == 0)
                break;

            if (size < 0 || size > _historyMaximumRecord)
                throw new InvalidDataException("The archive declares an unsupported record size.");

            byte[] data = reader.ReadBytes(size);
            if (data.Length != size)
                throw new EndOfStreamException("The archive is truncated.");

            HistoryRecord record = JsonSerializer.Deserialize<HistoryRecord>(data) ?? throw new InvalidDataException("An archive record is invalid.");
            if (header is null)
            {
                if (record.Kind != _historyHeader)
                    throw new InvalidDataException("The archive manifest is missing.");

                header = record.Data.Deserialize<HistoryHeader>() ?? throw new InvalidDataException("The archive manifest is invalid.");
                Dictionary<string, string> bindings = HistoryBindings();
                if (_profile is null || _epoch is null || header.Profile != Convert.ToHexString(_profile) || header.Epoch != Convert.ToHexString(_epoch) || header.Bindings is null || header.Bindings.Any(pair => !bindings.TryGetValue(pair.Key, out string? current) || current != pair.Value))
                    throw new InvalidDataException("The archive belongs to a different profile or incompatible history format. Its source data is unchanged.");
            }
            else
            {
                if (record.Kind is not (_historyFrame or _historyClosedFrame or _historyDirect or _historyRecipe))
                    throw new InvalidDataException("An archive record kind is unsupported.");
                visit?.Invoke(record);
            }
        }

        if (reader.Read() != -1)
            throw new InvalidDataException("The archive contains trailing records.");

        cipher.Complete();
        return header ?? throw new InvalidDataException("The archive is empty.");
    }

    /// <summary>
    /// Removes exactly the data already authenticated in the newly written archive.
    /// </summary>
    /// <param name="record">One full eligible record.</param>
    /// <param name="id">The archive identity retaining any referenced dictionary strings.</param>
    private void RemoveHistory(HistoryRecord record, Guid id)
    {
        if (record.Kind == _historyRecipe)
            return;

        if (record.Kind == _historyDirect)
        {
            JsonElement state = record.Data.GetProperty(_historyFieldState);
            CollectionQueries.DeleteHistoryMeasure(_db, _transaction, (long)Unpack(state.GetProperty(_historySeries)), checked((int)(long)Unpack(state.GetProperty(_historyMeasure))));
            return;
        }

        JsonElement frame = record.Data;
        long scope = frame.GetProperty(_historyFieldScope).GetInt64();
        int family = frame.GetProperty(_historyFieldFamily).GetInt32();
        HashSet<long> roots = [];
        CollectOrderedArchiveRoots(frame, roots);
        foreach (long root in roots)
        {
            if (!HistoryRows(_historyRoots).Any(row => ((byte[])Unpack(row[_historyFieldArchiveId])).AsSpan().SequenceEqual(id.ToByteArray()) && (long)Unpack(row[_historyFieldValueId]) == root))
                HistoryInsert(_historyRoots, id.ToByteArray(), root);
        }

        ColdPartitionRecord? cold = CollectionQueries.ReadColdPartition(_db, _transaction, scope, family);
        HotPartitionRecord? hot = CollectionQueries.ReadHotPartition(_db, _transaction, scope, family);
        if (cold is not null)
        {
            CollectionQueries.DeleteColdPartition(_db, _transaction, scope, family);
            CollectionQueries.DeleteReplacedColdBlock(_db, _transaction, cold.Block);
        }

        if (hot is not null)
            CollectionQueries.DeletePartitionSeries(_db, _transaction, hot.Id);
    }

    /// <summary>
    /// Merges one compatible detached frame with newer retained counters and memberships.
    /// </summary>
    /// <param name="record">The complete authenticated history record.</param>
    private void RestoreHistory(HistoryRecord record)
    {
        if (record.Kind == _historyDirect)
        {
            RestoreHistoryDirect(record.Data);
            return;
        }

        JsonElement frame = record.Data;
        long scope = frame.GetProperty(_historyFieldScope).GetInt64();
        int family = frame.GetProperty(_historyFieldFamily).GetInt32();
        var definition = _catalogue.Families.Single(entry => entry.Id == family);
        ThawPartition(scope, family);
        if (record.Kind == _historyClosedFrame)
            _thawedPartitions.Add((scope, family));

        long partition = CollectionQueries.TouchPartition(_db, _transaction, family, scope, CollectionEncoding.Revision(_revision));
        foreach (JsonElement stored in frame.GetProperty(_archiveSeriesProperty).EnumerateArray())
        {
            long tuple = stored.GetProperty(_archiveTupleProperty).GetInt64();
            long series = CollectionQueries.InternSeries(_db, _transaction, partition, family, tuple);
            var currentCells = CollectionQueries.FindPartitionCells(_db, _transaction, series).ToDictionary(cell => cell.Measure);
            foreach (JsonElement row in stored.GetProperty(_archiveCellsProperty).EnumerateArray())
            {
                int measure = checked((int)(long)Unpack(row[0]));
                var prior = CollectionQueries.ReadCell(_db, _transaction, series, measure);
                BigInteger? oldValue = CollectionEncoding.ReadScalar(Unpack(row[1]));
                BigInteger? newValue = prior is null ? null : CollectionEncoding.ReadScalar(prior.Value);
                string operation = definition.Measures.Single(item => item.Id == measure).MergeOperator;
                byte[] revision = (byte[])Unpack(row[6]);
                bool newer = currentCells.TryGetValue(measure, out var current) && CollectionEncoding.ReadRevision(current.Revision) >= CollectionEncoding.ReadRevision(revision);
                BigInteger? combined = oldValue is null ? newValue : operation == _orderedReplace && newer && newValue is not null ? newValue : Merge(newValue, oldValue.Value, operation);
                BigInteger[] populations = prior is null ? [0, 0, 0, 0] : [CollectionEncoding.ReadCount(prior.Known), CollectionEncoding.ReadCount(prior.Unknown), CollectionEncoding.ReadCount(prior.Unsupported), CollectionEncoding.ReadCount(prior.NotApplicable)];
                for (int index = 0; index < 4; index++)
                    populations[index] += CollectionEncoding.ReadCount(Unpack(row[index + 2]));

                CollectionQueries.SaveCell(_db, _transaction, series, family, measure, CollectionEncoding.Scalar(combined), CollectionEncoding.Count(populations[0]), CollectionEncoding.Count(populations[1]), CollectionEncoding.Count(populations[2]), CollectionEncoding.Count(populations[3]), newer ? current!.Revision : revision);
            }

            foreach (JsonElement row in stored.GetProperty(_archiveSetsProperty).EnumerateArray())
                CollectionQueries.AddSetMember(_db, _transaction, series, family, checked((int)(long)Unpack(row[0])), (long)Unpack(row[1]));

            foreach (JsonElement row in stored.GetProperty(_archiveCoverageProperty).EnumerateArray())
            {
                int measure = checked((int)(long)Unpack(row[0]));
                var prior = CollectionQueries.ReadSetCoverage(_db, _transaction, series, measure);
                BigInteger[] populations = prior is null ? [0, 0, 0, 0] : [CollectionEncoding.ReadCount(prior.Known), CollectionEncoding.ReadCount(prior.Unknown), CollectionEncoding.ReadCount(prior.Unsupported), CollectionEncoding.ReadCount(prior.NotApplicable)];
                for (int index = 0; index < 4; index++)
                    populations[index] += CollectionEncoding.ReadCount(Unpack(row[index + 1]));

                CollectionQueries.SaveSetCoverage(_db, _transaction, series, family, measure, CollectionEncoding.Count(populations[0]), CollectionEncoding.Count(populations[1]), CollectionEncoding.Count(populations[2]), CollectionEncoding.Count(populations[3]), CollectionEncoding.Revision(_revision));
            }
        }

        var ordered = CollectionQueries.FindPartitionOrdered(_db, _transaction, family, scope).ToDictionary(row => row.Tuple);
        foreach (JsonElement row in frame.GetProperty(_archiveOrderedProperty).EnumerateArray())
        {
            long tuple = (long)Unpack(row[0]);
            byte[] revision = (byte[])Unpack(row[7]);
            if (ordered.TryGetValue(tuple, out var prior) && CollectionEncoding.ReadRevision(prior.Revision) >= CollectionEncoding.ReadRevision(revision))
                continue;

            if (prior is not null)
                CollectionQueries.DeleteHistoryOrdered(_db, _transaction, family, scope, tuple);

            CollectionQueries.RestoreOrderedState(_db, _transaction, family, scope, tuple, row[1][0].GetString() == _tagNull ? null : (long)Unpack(row[1]), (long)Unpack(row[2]), (string)Unpack(row[3]), (byte[])Unpack(row[4]), (byte[])Unpack(row[5]), (byte[])Unpack(row[6]), revision);
        }
    }

    /// <summary>
    /// Adds a detached additive or set measure without changing newer observation identities.
    /// </summary>
    /// <param name="data">The original complete tagged coverage, scalar and membership rows.</param>
    private void RestoreHistoryDirect(JsonElement data)
    {
        JsonElement state = data.GetProperty(_historyFieldState);
        long series = (long)Unpack(state.GetProperty(_historySeries));
        int group = checked((int)(long)Unpack(state.GetProperty(_historyGroup)));
        int measure = checked((int)(long)Unpack(state.GetProperty(_historyMeasure)));
        var prior = CollectionQueries.ReadDirectCoverage(_db, _transaction, series, measure);
        BigInteger[] counts = prior is null ? [0, 0, 0, 0] : [Pair(prior.KnownInteger, prior.KnownWide), Pair(prior.UnknownInteger, prior.UnknownWide), Pair(prior.UnsupportedInteger, prior.UnsupportedWide), Pair(prior.NotApplicableInteger, prior.NotApplicableWide)];
        string[] names = [_historyKnown, _historyUnknown, _historyUnsupported, _historyNotApplicable];
        for (int index = 0; index < names.Length; index++)
            counts[index] += HistoryNumber(state, names[index] + _historyIntegerSuffix, names[index] + _historyWideSuffix);

        string kind = (string)Unpack(state.GetProperty(_historyFieldMeasureKind));
        CollectionQueries.SaveDirectCoverage(_db, _transaction, series, group, measure, kind, counts[0] > 0 ? 1 : 0, (long?)Small(counts[0]), (string?)Wide(counts[0]), (long?)Small(counts[1]), (string?)Wide(counts[1]), (long?)Small(counts[2]), (string?)Wide(counts[2]), (long?)Small(counts[3]), (string?)Wide(counts[3]), CollectionEncoding.Revision(_revision));
        foreach (JsonElement row in data.GetProperty(_historyFieldScalars).EnumerateArray())
        {
            var scalar = CollectionQueries.ReadScalar(_db, _transaction, series, measure);
            BigInteger value = HistoryNumber(row, _historyValueInteger, _historyValueWide) + (scalar is null ? 0 : Pair(scalar.Integer, scalar.Wide));
            CollectionQueries.SaveScalar(_db, _transaction, series, group, measure, (long?)Small(value), (string?)Wide(value), null);
        }

        foreach (JsonElement row in data.GetProperty(_historyFieldMembers).EnumerateArray())
            CollectionQueries.AddDirectSetMember(_db, _transaction, series, group, measure, (long)Unpack(row.GetProperty(_historyMember)));
    }

    /// <summary>
    /// Reads a retained full-width integer without narrowing decimal overflow.
    /// </summary>
    /// <param name="row">The complete original tagged SQL row.</param>
    /// <param name="integer">The native integer column.</param>
    /// <param name="wide">The decimal overflow column.</param>
    /// <returns>The exact original integer.</returns>
    private static BigInteger HistoryNumber(JsonElement row, string integer, string wide)
    {
        object small = Unpack(row.GetProperty(integer));
        return small is long number ? new BigInteger(number) : BigInteger.Parse((string)Unpack(row.GetProperty(wide)), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolves a recipe path only inside the configured tracker-owned root.
    /// </summary>
    /// <param name="root">The explicitly configured recipe root.</param>
    /// <param name="name">The exact relative filename retained in the authenticated archive.</param>
    /// <returns>The validated absolute recipe path.</returns>
    private static string HistoryRecipePath(string? root, string name)
    {
        if (root is null || Path.IsPathRooted(name) || Path.GetExtension(name) != _historyJson)
            throw new InvalidDataException("The archive recipe path is incompatible with this tracker profile.");

        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An archive recipe path leaves the tracker profile.");

        return path;
    }
}
