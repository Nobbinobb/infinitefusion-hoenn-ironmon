using Microsoft.Data.Sqlite;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;
using Ironmon.Tracker.Collection.Storage.Maintenance;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Reads operational collection metadata while the serialized gameplay writer remains active.
/// </summary>
public static class GameplayCollectionInspector
{
    private const string _databaseName = "collection.sqlite";
    private const string _walSuffix = "-wal";
    private const string _shmSuffix = "-shm";
    private const string _producerDirectory = "producer";
    private const string _streamsDirectory = "streams";
    private const string _bindingAName = "binding-a.json";
    private const string _bindingBName = "binding-b.json";
    private const string _journalPattern = "*.journal";
    private const string _databaseBytesProperty = "database_bytes";
    private const string _walBytesProperty = "wal_bytes";
    private const string _shmBytesProperty = "shm_bytes";
    private const string _producerJournalBytesProperty = "producer_journal_bytes";
    private const string _producerJournalSegmentsProperty = "producer_journal_segments";
    private const string _producerBindingBytesProperty = "producer_binding_bytes";
    private const string _producerMeasurementCompleteProperty = "producer_measurement_complete";
    private const string _totalMeasuredBytesProperty = "total_measured_bytes";
    private const string _approximateSnapshotProperty = "storage_snapshot_approximate";
    private const string _metaTableName = "collection_meta";

    /// <summary>
    /// Reads storage growth, table/family counts and receipt frontiers without exporting collected payloads.
    /// </summary>
    /// <param name="profileDirectory">The external directory containing collection.sqlite.</param>
    /// <returns>A standalone operational JSON snapshot.</returns>
    public static JsonElement Read(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        string path = Path.Combine(Path.GetFullPath(profileDirectory), _databaseName);
        if (!File.Exists(path))
            return IncludePhysicalStorage(new { exists = false }, path);

        SqliteConnectionStringBuilder builder = new() { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using SqliteConnection connection = new(builder.ToString());
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        List<string> tableNames = [.. CollectionQueries.FindInspectorTables(connection, transaction).Select(row => row.Name)];

        if (!tableNames.Contains(_metaTableName, StringComparer.Ordinal))
        {
            transaction.Commit();
            return IncludePhysicalStorage(new { exists = true, initializing = true }, path);
        }

        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        foreach (string table in tableNames)
            counts[table] = CollectionSqliteMaintenance.CountTableRows(connection, transaction, table);

        List<object> families = [];
        foreach (InspectorFamilyRecord family in CollectionQueries.FindInspectorFamilies(connection, transaction))
            families.Add(new { key = family.Key, hot_series = family.Series, hot_partitions = family.Hot, cold_partitions = family.Cold });

        List<object> receipts = [];
        foreach (InspectorReceiptRecord receipt in CollectionQueries.FindInspectorReceipts(connection, transaction))
            receipts.Add(new { stream_id = CollectionEncoding.Identifier(receipt.Stream), durable_prefix = CollectionEncoding.Decimal(CollectionEncoding.ReadRevision(receipt.Sequence)), revision = CollectionEncoding.Decimal(CollectionEncoding.ReadRevision(receipt.Revision)) });

        InspectorOperationsRecord? storedOperations = CollectionQueries.ReadInspectorOperations(connection, transaction);
        object? operations = storedOperations is null ? null : new { accepted_batches = storedOperations.Batches, accepted_events = storedOperations.Events, duplicate_events = storedOperations.Duplicates, received_bytes = storedOperations.Bytes, previous_commit_ms = storedOperations.CommitMilliseconds, updated_utc_ms = storedOperations.Updated };
        InspectorMetadataRecord? storedMetadata = CollectionQueries.ReadInspectorMetadata(connection, transaction);
        object? metadata = storedMetadata is null ? null : new { schema_version = storedMetadata.Version, revision = CollectionEncoding.Decimal(CollectionEncoding.ReadRevision(storedMetadata.Revision)) };
        Dictionary<(string Family, string Measure, string Scope), BigInteger[]> coverageTotals = [];
        foreach (InspectorCoverageRecord coverage in CollectionQueries.FindInspectorCoverage(connection, transaction))
        {
            (string Family, string Measure, string Scope) key = (coverage.Family, coverage.Measure, coverage.Scope);
            if (!coverageTotals.TryGetValue(key, out BigInteger[]? totals))
                coverageTotals[key] = totals = [0, 0, 0, 0];

            totals[0] += CollectionEncoding.ReadCount(coverage.Known);
            totals[1] += CollectionEncoding.ReadCount(coverage.Unknown);
            totals[2] += CollectionEncoding.ReadCount(coverage.Unsupported);
            totals[3] += CollectionEncoding.ReadCount(coverage.NotApplicable);
        }

        object[] hotCoverage = [.. coverageTotals.OrderBy(pair => pair.Key.Family, StringComparer.Ordinal).ThenBy(pair => pair.Key.Measure, StringComparer.Ordinal).ThenBy(pair => pair.Key.Scope, StringComparer.Ordinal).Select(pair => (object)new { family = pair.Key.Family, measure = pair.Key.Measure, scope_kind = pair.Key.Scope, known = CollectionEncoding.Decimal(pair.Value[0]), unknown = CollectionEncoding.Decimal(pair.Value[1]), unsupported = CollectionEncoding.Decimal(pair.Value[2]), not_applicable = CollectionEncoding.Decimal(pair.Value[3]) })];
        object result = new { exists = true, sqlite_version = CollectionSqliteMaintenance.SqliteVersion(connection, transaction), page_size = CollectionSqliteMaintenance.ReadPageSize(connection, transaction), page_count = CollectionSqliteMaintenance.ReadPageCount(connection, transaction), free_pages = CollectionSqliteMaintenance.ReadFreeCount(connection, transaction), metadata, tables = counts, families, hot_coverage = hotCoverage, receipts, operations };
        transaction.Commit();
        return IncludePhysicalStorage(result, path);
    }

    /// <summary>
    /// Adds an approximate physical snapshot of SQLite files and the producer's retained outbox without reading gameplay payloads.
    /// </summary>
    /// <param name="inspection">The operational database inspection to extend with physical file sizes.</param>
    /// <param name="databasePath">The absolute collection SQLite path whose sibling files and producer outbox are measured.</param>
    /// <returns>An independent JSON result including approximate SQLite/outbox byte totals and measurement completeness.</returns>
    private static JsonElement IncludePhysicalStorage(object inspection, string databasePath)
    {
        JsonObject result = JsonSerializer.SerializeToNode(inspection)!.AsObject();
        long database = Length(databasePath);
        long wal = Length(databasePath + _walSuffix);
        long shm = Length(databasePath + _shmSuffix);
        string producer = Path.Combine(Path.GetDirectoryName(databasePath)!, _producerDirectory);
        long journals = 0;
        long segments = 0;
        long bindings = 0;
        bool complete = true;
        try
        {
            if (Directory.Exists(producer) && (File.GetAttributes(producer) & FileAttributes.ReparsePoint) != 0)
            {
                complete = false;
            }
            else
            {
                bindings = ProducerFileLength(Path.Combine(producer, _bindingAName), ref complete) + ProducerFileLength(Path.Combine(producer, _bindingBName), ref complete);
                string streams = Path.Combine(producer, _streamsDirectory);
                if (Directory.Exists(streams))
                {
                    if ((File.GetAttributes(streams) & FileAttributes.ReparsePoint) != 0)
                    {
                        complete = false;
                    }
                    else
                    {
                        EnumerationOptions options = new() { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
                        foreach (string file in Directory.EnumerateFiles(streams, _journalPattern, options))
                        {
                            journals += ProducerFileLength(file, ref complete);
                            segments++;
                        }
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            complete = false;
        }

        result[_databaseBytesProperty] = database;
        result[_walBytesProperty] = wal;
        result[_shmBytesProperty] = shm;
        result[_producerJournalBytesProperty] = journals;
        result[_producerJournalSegmentsProperty] = segments;
        result[_producerBindingBytesProperty] = bindings;
        result[_producerMeasurementCompleteProperty] = complete;
        result[_totalMeasuredBytesProperty] = checked(database + wal + shm + journals + bindings);
        result[_approximateSnapshotProperty] = true;
        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>
    /// Reads file metadata only, preserving a partial-measurement indicator for inaccessible files and refusing links outside the owned layout.
    /// </summary>
    /// <param name="path">The owned producer file whose metadata is inspected without reading its payload.</param>
    /// <param name="complete">Set to false when the path is inaccessible or a link; an existing false value is preserved.</param>
    /// <returns>The file length in bytes, or zero for a missing, inaccessible or rejected link path.</returns>
    private static long ProducerFileLength(string path, ref bool complete)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                complete = false;
                return 0;
            }

            return new FileInfo(path).Length;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            complete = false;
            return 0;
        }
    }

    /// <summary>
    /// Reads the existing length of one owned storage file.
    /// </summary>
    /// <param name="path">The owned storage file whose length is requested; a missing file contributes zero.</param>
    /// <returns>The existing file length in bytes, or zero when the file is absent.</returns>
    private static long Length(string path)
        => File.Exists(path) ? new FileInfo(path).Length : 0;

}
