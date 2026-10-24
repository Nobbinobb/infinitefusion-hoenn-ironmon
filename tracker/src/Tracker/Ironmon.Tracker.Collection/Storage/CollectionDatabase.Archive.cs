using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;
using System.Globalization;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Publishes lossless closed family partitions with shared immutable revision headers.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _coldBlockKind = "closed_scope_v3";
    private const string _archiveFamiliesProperty = "families";
    private const string _rawBlockCodec = "raw";
    private const string _compressedBlockCodec = "zlib";
    private const string _tagBlob = "b";
    private const string _tagInteger = "i";
    private const string _tagText = "t";
    private const string _tagNull = "n";
    private const string _archiveSeriesProperty = "series";
    private const string _archiveOrderedProperty = "ordered";
    private const string _archiveTupleProperty = "tuple";
    private const string _archiveCellsProperty = "cells";
    private const string _archiveSetsProperty = "sets";
    private const string _archiveCoverageProperty = "coverage";
    private readonly HashSet<(long Scope, int Family)> _thawedPartitions = [];
    private readonly HashSet<long> _replacedColdBlocks = [];
    private readonly Dictionary<long, JsonElement> _archiveReadCache = [];
    private readonly HashSet<(long Scope, int Family)> _checkedHotPartitions = [];

    /// <summary>
    /// Compacts completed attempt and battle partitions while retaining actual owned records.
    /// </summary>
    /// <param name="attempt">The actual attempt UUID in its 16-byte database representation.</param>
    private void CompactAttempt(byte[] attempt)
    {
        List<long> scopes = [.. CollectionQueries.FindAttemptScopes(_db, _transaction, attempt).Select(row => row.Id)];

        foreach (long scope in scopes)
        {
            List<(long Partition, int Family)> partitions = [.. CollectionQueries.FindScopePartitions(_db, _transaction, attempt, scope).Select(row => (row.Partition, row.Family))];

            PublishScope(scope, partitions);
        }
    }

    /// <summary>
    /// Publishes finalized family records in one shared typed block for an actual scope.
    /// </summary>
    /// <param name="scope">The interned lifetime, attempt or battle scope identifier.</param>
    /// <param name="partitions">The active relational partitions to merge with any unchanged archived families.</param>
    private void PublishScope(long scope, IReadOnlyList<(long Partition, int Family)> partitions)
    {
        _checkedHotPartitions.Clear();
        List<(int Family, long Block, long Publication)> previous = [];
        foreach (ArchiveColdPartitionRecord row in CollectionQueries.FindScopeColdPartitions(_db, _transaction, scope))
            previous.Add((row.Family, row.Block, row.Publication));

        if (previous.Count == 0 && partitions.Count == 0)
            return;

        Dictionary<int, JsonElement> frames = [];
        foreach (var (family, block, _) in previous)
        {
            frames[family] = ReadArchiveFrame(block, family);
            _replacedColdBlocks.Add(block);
        }

        foreach (var (partition, family) in partitions)
            frames[family] = ReadPartition(scope, family, partition);

        byte[] raw = CollectionEncoding.Canonical(new { format = 2, scope, families = frames.OrderBy(pair => pair.Key).Select(pair => new object[] { pair.Key, pair.Value }) });
        long sharedBlock = Block(_coldBlockKind, raw);
        long publication = partitions.Count == 0 ? 0 : CollectionQueries.InternColdPublication(_db, _transaction, CollectionEncoding.Revision(_revision));
        Dictionary<int, long> publications = previous.ToDictionary(item => item.Family, item => item.Publication);
        foreach (var (_, family) in partitions)
            publications[family] = publication;

        CollectionQueries.DeleteScopeColdPartitions(_db, _transaction, scope);
        foreach (var (partition, family) in partitions)
        {
            CollectionQueries.DeletePartitionSeries(_db, _transaction, partition);
            CollectionQueries.DeleteOrderedState(_db, _transaction, scope, family);
            CollectionQueries.DeleteHotPartition(_db, _transaction, partition);
        }

        foreach (int family in frames.Keys)
            CollectionQueries.InsertColdPartition(_db, _transaction, scope, family, sharedBlock, publications[family]);
    }

    /// <summary>
    /// Reads an active family into a complete lossless typed archive frame before removing its hot rows.
    /// </summary>
    /// <param name="scope">The actual scope identifier.</param>
    /// <param name="family">The registered family identifier.</param>
    /// <param name="partition">The active partition identifier.</param>
    /// <returns>The complete scalar, membership, coverage and ordered-state frame.</returns>
    private JsonElement ReadPartition(long scope, int family, long partition)
    {
        List<object> seriesRows = [];
        foreach (ArchiveSeriesRecord stored in CollectionQueries.FindPartitionSeries(_db, _transaction, partition))
        {
            long series = stored.Series;
            long tuple = stored.Tuple;
            List<JsonElement[]> cells = PackRows(CollectionQueries.FindPartitionCells(_db, _transaction, series), row => [Pack(row.Measure), Pack(row.Value), Pack(row.Known), Pack(row.Unknown), Pack(row.Unsupported), Pack(row.NotApplicable), Pack(row.Revision)]);
            List<JsonElement[]> sets = PackRows(CollectionQueries.FindPartitionMembers(_db, _transaction, series), row => [Pack(row.Measure), Pack(row.Member)]);
            List<JsonElement[]> coverage = PackRows(CollectionQueries.FindPartitionCoverage(_db, _transaction, series), row => [Pack(row.Measure), Pack(row.Known), Pack(row.Unknown), Pack(row.Unsupported), Pack(row.NotApplicable), Pack(row.Revision)]);
            seriesRows.Add(new { tuple, cells, sets, coverage });
        }

        List<JsonElement[]> ordered = PackRows(CollectionQueries.FindPartitionOrdered(_db, _transaction, family, scope), row => [Pack(row.Tuple), Pack((object?)row.Context ?? DBNull.Value), Pack(row.Format), Pack(row.Codec), Pack(row.Payload), Pack(row.Stream), Pack(row.Sequence), Pack(row.Revision)]);
        return JsonSerializer.SerializeToElement(new { format = 2, family, scope, series = seriesRows, ordered });
    }

    /// <summary>
    /// Reopens a closed partition only for genuinely new post-load gameplay, preserving all prior evidence.
    /// </summary>
    /// <param name="scope">The interned lifetime, attempt or battle scope identifier.</param>
    /// <param name="family">The registered aggregate family identifier.</param>
    private void ThawPartition(long scope, int family)
    {
        if (_checkedHotPartitions.Contains((scope, family)))
            return;

        ColdPartitionRecord? archived = CollectionQueries.ReadColdPartition(_db, _transaction, scope, family);
        if (archived is null)
        {
            if (_checkedHotPartitions.Count < 32768)
                _checkedHotPartitions.Add((scope, family));

            return;
        }

        long block = archived.Block;
        JsonElement frame = ReadArchiveFrame(block, family);

        CollectionQueries.DeleteColdPartition(_db, _transaction, scope, family);
        _thawedPartitions.Add((scope, family));
        _replacedColdBlocks.Add(block);
        long partition = CollectionQueries.TouchPartition(_db, _transaction, family, scope, CollectionEncoding.Revision(_revision));
        foreach (JsonElement stored in frame.GetProperty(_archiveSeriesProperty).EnumerateArray())
        {
            long tuple = stored.GetProperty(_archiveTupleProperty).GetInt64();
            long series = CollectionQueries.InternSeries(_db, _transaction, partition, family, tuple);
            foreach (JsonElement row in stored.GetProperty(_archiveCellsProperty).EnumerateArray())
                CollectionQueries.SaveCell(_db, _transaction, series, family, checked((int)(long)Unpack(row[0])), Unpack(row[1]), Unpack(row[2]), Unpack(row[3]), Unpack(row[4]), Unpack(row[5]), (byte[])Unpack(row[6]));

            foreach (JsonElement row in stored.GetProperty(_archiveSetsProperty).EnumerateArray())
                CollectionQueries.AddSetMember(_db, _transaction, series, family, checked((int)(long)Unpack(row[0])), (long)Unpack(row[1]));

            foreach (JsonElement row in stored.GetProperty(_archiveCoverageProperty).EnumerateArray())
                CollectionQueries.SaveSetCoverage(_db, _transaction, series, family, checked((int)(long)Unpack(row[0])), Unpack(row[1]), Unpack(row[2]), Unpack(row[3]), Unpack(row[4]), (byte[])Unpack(row[5]));
        }

        foreach (JsonElement row in frame.GetProperty(_archiveOrderedProperty).EnumerateArray())
            CollectionQueries.RestoreOrderedState(_db, _transaction, family, scope, (long)Unpack(row[0]), row[1][0].GetString() == _tagNull ? null : (long)Unpack(row[1]), (long)Unpack(row[2]), (string)Unpack(row[3]), (byte[])Unpack(row[4]), (byte[])Unpack(row[5]), (byte[])Unpack(row[6]), (byte[])Unpack(row[7]));

        if (_checkedHotPartitions.Count < 32768)
            _checkedHotPartitions.Add((scope, family));
    }

    /// <summary>
    /// Publishes continuation evidence back into closed partitions without retaining replacement generations.
    /// </summary>
    private void RecloseContinuations()
    {
        foreach (var group in _thawedPartitions.GroupBy(item => item.Scope))
        {
            List<(long Partition, int Family)> partitions = [];
            foreach (var (_, family) in group)
            {
                if (CollectionQueries.ReadHotPartition(_db, _transaction, group.Key, family) is { } partition)
                    partitions.Add((partition.Id, family));
            }

            PublishScope(group.Key, partitions);
        }

        foreach (long block in _replacedColdBlocks)
            CollectionQueries.DeleteReplacedColdBlock(_db, _transaction, block);

        CollectionQueries.DeleteUnusedPublications(_db, _transaction);
        _thawedPartitions.Clear();
        _replacedColdBlocks.Clear();
        _archiveReadCache.Clear();
    }

    /// <summary>
    /// Resolves one family frame from its indexed scope block, retaining compatibility during migration.
    /// </summary>
    /// <param name="block">The referenced immutable block ID.</param>
    /// <param name="family">The exact family required by the partition's relational key.</param>
    /// <returns>The complete family frame.</returns>
    private JsonElement ReadArchiveFrame(long block, int family)
    {
        JsonElement value = ReadStoredBlock(block);
        if (!value.TryGetProperty(_archiveFamiliesProperty, out JsonElement families))
            return UpgradeArchiveOrdered(value);

        foreach (JsonElement entry in families.EnumerateArray())
        {
            if (entry[0].GetInt32() == family)
                return entry[1];
        }

        throw new InvalidDataException("A closed scope block does not contain its referenced family.");
    }

    /// <summary>
    /// Reads and validates an immutable block before interpreting its explicit storage version.
    /// </summary>
    /// <param name="block">The immutable block ID.</param>
    /// <returns>The exact decoded value, cached only within the bounded immutable read cache.</returns>
    private JsonElement ReadStoredBlock(long block)
    {
        if (_archiveReadCache.TryGetValue(block, out JsonElement cached))
            return cached;

        ImmutableBlockRecord stored = CollectionQueries.ReadImmutableBlock(_db, _transaction, block) ?? throw new InvalidDataException("An immutable collection block is missing.");
        byte[] bytes = stored.Codec == _rawBlockCodec ? stored.Payload : CollectionEncoding.Decompress(stored.Payload, stored.Length, stored.Digest);
        if (bytes.LongLength != stored.Length || !System.Security.Cryptography.SHA256.HashData(bytes).AsSpan().SequenceEqual(stored.Digest))
            throw new InvalidDataException("An immutable collection block failed its integrity check.");

        int format = stored.Format;

        JsonElement value;
        if (format == 1)
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            value = document.RootElement.Clone();
        }
        else if (format == 2)
        {
            value = _values.Decode(bytes);
        }
        else if (format == 3)
        {
            value = CollectionArchiveEncoding.Decode(bytes);
        }
        else
        {
            throw new NotSupportedException("Unsupported collection detail format.");
        }

        if (_archiveReadCache.Count >= 128)
            _archiveReadCache.Clear();

        _archiveReadCache[block] = value;
        return value;
    }

    /// <summary>
    /// Encodes typed selected rows with their exact original SQLite storage-class tags.
    /// </summary>
    /// <typeparam name="TRow">The generated original-column result shape.</typeparam>
    /// <param name="rows">The ordered rows whose enumerator owns the current command and reader.</param>
    /// <param name="encode">Converts each complete row to the existing exact tagged value format.</param>
    /// <returns>Complete tagged rows in unchanged provider order.</returns>
    private static List<JsonElement[]> PackRows<TRow>(IEnumerable<TRow> rows, Func<TRow, JsonElement[]> encode)
        => [.. rows.Select(encode)];

    /// <summary>
    /// Encodes one SQLite value without integer precision loss.
    /// </summary>
    /// <param name="value">The native SQLite blob, integer, text or database-null value to tag explicitly.</param>
    /// <returns>The tagged JSON representation of the original SQLite storage class and value.</returns>
    private static JsonElement Pack(object value) => value is byte[] bytes ? JsonSerializer.SerializeToElement(new[] { _tagBlob, Convert.ToHexString(bytes) })
        : value is long integer ? JsonSerializer.SerializeToElement(new object[] { _tagInteger, integer })
        : value is DBNull ? JsonSerializer.SerializeToElement(new[] { _tagNull })
        : JsonSerializer.SerializeToElement(new[] { _tagText, (string)value });

    /// <summary>
    /// Decodes one exact SQLite value from its explicit storage class.
    /// </summary>
    /// <param name="value">The tagged JSON storage-class/value pair from a finalized partition.</param>
    /// <returns>The original SQLite-compatible byte array, signed integer, text or database-null value.</returns>
    private static object Unpack(JsonElement value)
        => value[0].GetString() switch { _tagBlob => Convert.FromHexString(value[1].GetString()!), _tagInteger => value[1].ValueKind == JsonValueKind.Number ? value[1].GetInt64() : long.Parse(value[1].GetString()!, CultureInfo.InvariantCulture), _tagNull => DBNull.Value, _tagText => value[1].GetString()!, _ => throw new InvalidDataException("Unknown closed-partition storage class.") };
}
