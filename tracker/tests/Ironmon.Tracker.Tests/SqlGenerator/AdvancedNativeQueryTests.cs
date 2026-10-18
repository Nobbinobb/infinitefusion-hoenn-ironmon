using Ironmon.Tracker.Tests.Collection;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Verifies native relational composition and exact transaction ownership independently of collection data.
/// </summary>
public sealed class AdvancedNativeQueryTests
{
    private const string _memory = "Data Source=:memory:";
    private const string _schema = "Ironmon.Tracker.Tests.Collection.Fixtures.GeneratedQueries.sql";

    /// <summary>
    /// Preserves SQLite BLOB ordering across high-bit bytes, prefixes and exact equality.
    /// </summary>
    [Fact]
    public void NativeBinaryComparisonUsesLexicographicBytesAndLengths()
    {
        using SqliteConnection connection = Open();
        NativeValueQueries.Save(connection, null, 1, Array.Empty<byte>());
        NativeValueQueries.Save(connection, null, 2, new byte[] { 0 });
        NativeValueQueries.Save(connection, null, 3, new byte[] { 128 });
        NativeValueQueries.Save(connection, null, 4, new byte[] { 128, 0 });
        NativeValueQueries.Save(connection, null, 5, new byte[] { 255 });
        Assert.Equal(3L, AdvancedNativeQueries.CountBinaryAtLeast(connection, null, [128]));
        Assert.Equal(2L, AdvancedNativeQueries.CountBinaryAtLeast(connection, null, [128, 0]));
        Assert.Equal(1L, AdvancedNativeQueries.CountBinaryAtLeast(connection, null, [255]));
        Assert.Equal(5L, AdvancedNativeQueries.CountBinaryAtLeast(connection, null, []));
    }

    /// <summary>
    /// Preserves native SUM behavior for empty, exact integer, binary and real-valued inputs.
    /// </summary>
    [Fact]
    public void NativeSumRetainsDatabaseNullAndExactAggregateStorageClasses()
    {
        using SqliteConnection connection = Open();
        Assert.IsType<DBNull>(AdvancedNativeQueries.SumNativeValues(connection, null)!.Value);
        NativeValueQueries.Save(connection, null, 1, long.MinValue);
        NativeValueQueries.Save(connection, null, 2, long.MaxValue);
        Assert.Equal(-1L, Assert.IsType<long>(AdvancedNativeQueries.SumNativeValues(connection, null)!.Value));
        NativeValueQueries.Save(connection, null, 1, null);
        NativeValueQueries.Save(connection, null, 2, null);
        NativeValueQueries.Save(connection, null, 3, new byte[] { 49, 50 });
        Assert.Equal(12d, Assert.IsType<double>(AdvancedNativeQueries.SumNativeValues(connection, null)!.Value));
        NativeValueQueries.Save(connection, null, 4, 1.5d);
        Assert.Equal(13.5d, Assert.IsType<double>(AdvancedNativeQueries.SumNativeValues(connection, null)!.Value));
    }

    /// <summary>
    /// Keeps nullable conflict computations and explicit full-table updates inside caller-owned transactions.
    /// </summary>
    [Fact]
    public void ComputedUpsertsAndUpdatesKeepExactCountersAndRollback()
    {
        using SqliteConnection connection = Open();
        AdvancedNativeQueries.Accumulate(connection, null, 1, null);
        AdvancedNativeQueries.Accumulate(connection, null, 1, long.MaxValue - 1);
        AdvancedNativeQueries.Accumulate(connection, null, 1, null);
        AdvancedNativeQueries.Accumulate(connection, null, 1, 1);
        Assert.Equal(long.MaxValue, NativeValueQueries.Read(connection, null, 1)!.Value);
        using SqliteTransaction transaction = connection.BeginTransaction();
        Assert.Equal(1, AdvancedNativeQueries.MoveIdentity(connection, transaction, 1, int.MaxValue));
        Assert.Equal(1L, AdvancedNativeQueries.CountAtLeast(connection, transaction, (long)int.MaxValue + 1));
        Assert.Equal(1, AdvancedNativeQueries.ClearCounters(connection, transaction));
        Assert.Equal(0L, NativeValueQueries.Read(connection, transaction, (long)int.MaxValue + 1)!.Value);
        transaction.Rollback();
        Assert.Equal(long.MaxValue, NativeValueQueries.Read(connection, null, 1)!.Value);
        Assert.Equal(0L, AdvancedNativeQueries.CountAtLeast(connection, null, (long)int.MaxValue + 1));
    }

    /// <summary>
    /// Preserves separate aliases when joining and correlating the same physical mapped table.
    /// </summary>
    [Fact]
    public void JoinsAndCorrelatedCountsKeepIndependentSourceBindings()
    {
        using SqliteConnection connection = Open();
        for (long id = 1; id <= 3; id++)
            AdvancedNativeQueries.Accumulate(connection, null, id, id);

        Assert.Equal([(1L, 2L), (2L, 1L), (3L, 0L)], AdvancedNativeQueries.CorrelatedCounts(connection, null).Select(row => (row.Id, row.Other)));
        Assert.Equal([(1L, 2L), (2L, 3L)], AdvancedNativeQueries.JoinedPairs(connection, null, 1).Select(row => (row.Id, row.Other)));
        Assert.Equal(3L, AdvancedNativeQueries.CountAtLeast(connection, null, 1));
        Assert.Equal(1, AdvancedNativeQueries.DeleteWithoutSuccessor(connection, null, 1));
        Assert.Equal([1L, 2L], AdvancedNativeQueries.CorrelatedCounts(connection, null).Select(row => row.Id));
    }

    /// <summary>
    /// Distinguishes UNION deduplication from UNION ALL population retention across multiple branches.
    /// </summary>
    [Fact]
    public void CompoundQueriesRetainNativeDuplicateSemantics()
    {
        using SqliteConnection connection = Open();
        for (long id = 1; id <= 3; id++)
            AdvancedNativeQueries.Accumulate(connection, null, id, null);

        Assert.Equal([1L, 2L, 3L], AdvancedNativeQueries.DistinctUnion(connection, null).Select(row => row.Id));
        Assert.Equal([1L, 2L, 3L, 1L, 2L, 3L], AdvancedNativeQueries.CompleteUnion(connection, null).Select(row => row.Id));
        Assert.All(AdvancedNativeQueries.CompleteUnion(connection, null), row => Assert.Equal(0L, row.Other));
    }

    /// <summary>
    /// Uses the common destination width when an Int32 constant precedes an Int64 source branch.
    /// </summary>
    [Fact]
    public void UnionReadersUseTheDeclaredResultWidthInsteadOfTheFirstBranchLiteralType()
    {
        using SqliteConnection connection = Open();
        AdvancedNativeQueries.Accumulate(connection, null, long.MaxValue, null);
        Assert.Equal([(0L, 0L), (long.MaxValue, long.MaxValue)], AdvancedNativeQueries.WidenedUnion(connection, null).Select(row => (row.Id, row.Other)));
    }

    /// <summary>
    /// Creates an isolated in-memory database using the existing provider fixture schema.
    /// </summary>
    /// <returns>An opened connection owned by the calling test.</returns>
    private static SqliteConnection Open()
    {
        SqliteConnection connection = new(_memory);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = GeneratedCollectionQueriesTests.Resource(_schema);
        command.ExecuteNonQuery();
        return connection;
    }
}
