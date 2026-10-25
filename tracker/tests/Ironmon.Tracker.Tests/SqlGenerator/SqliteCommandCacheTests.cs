using Ironmon.Sqlite.Definitions;
using Ironmon.Sqlite.Execution;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Tests.Collection;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Verifies prepared-command ownership, lifecycle invalidation and bounded input retention.
/// </summary>
public sealed class SqliteCommandCacheTests
{
    private const string _memory = "Data Source=:memory:;Pooling=False";
    private const string _schema = "Ironmon.Tracker.Tests.Collection.Fixtures.GeneratedQueries.sql";
    private const string _text = "cache-native";

    /// <summary>
    /// Infers every ANY input afresh while reusing the exact same prepared command and parameter slots.
    /// </summary>
    [Fact]
    public void ReusedNativeAnyParameterDoesNotKeepAnEarlierStorageType()
    {
        using SqliteConnection connection = Open();
        object identity = new();
        SqliteCommand? original = null;
        object?[] values = [long.MinValue, 1.25d, new byte[] { 0, 255, 128 }, _text, null, long.MaxValue];
        foreach (object? value in values)
        {
            using (SqliteCommandLease lease = Rent(connection, identity))
            {
                original ??= lease.Command;
                Assert.Same(original, lease.Command);
                lease.Command.Parameters[0].Value = 42L;
                lease.Command.Parameters[1].Value = value ?? DBNull.Value;
                Assert.Equal(1, lease.ExecuteNonQuery());
            }

            object? stored = NativeValueQueries.Read(connection, null, 42)!.Value;
            if (value is byte[] binary)
                Assert.Equal(binary, Assert.IsType<byte[]>(stored));
            else
                Assert.Equal(value, stored);
        }
    }

    /// <summary>
    /// Reuses prepared layouts while releasing transaction and managed parameter values between leases.
    /// </summary>
    [Fact]
    public void ReusedCommandClearsValuesAndRebindsAfterRollback()
    {
        using SqliteConnection connection = Open();
        object identity = new();
        SqliteCommand original;
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            using (SqliteCommandLease lease = Rent(connection, identity))
            {
                original = lease.Command;
                original.Transaction = transaction;
                original.Parameters[0].Value = 1L;
                original.Parameters[1].Value = _text;
                Assert.Equal(1, lease.ExecuteNonQuery());
            }

            Assert.Null(original.Transaction);
            Assert.All(original.Parameters.Cast<SqliteParameter>(), parameter => Assert.Same(DBNull.Value, parameter.Value));
            transaction.Rollback();
        }

        using (SqliteCommandLease lease = Rent(connection, identity))
        {
            Assert.Same(original, lease.Command);
            lease.Command.Parameters[0].Value = 2L;
            lease.Command.Parameters[1].Value = new byte[] { 0, 255 };
            Assert.Equal(1, lease.ExecuteNonQuery());
        }

        Assert.Null(NativeValueQueries.Read(connection, null, 1));
        Assert.Equal(new byte[] { 0, 255 }, Assert.IsType<byte[]>(NativeValueQueries.Read(connection, null, 2)!.Value));
    }

    /// <summary>
    /// Keeps overlapping readers independent and returns normally disposed incomplete enumerations for reuse.
    /// </summary>
    [Fact]
    public void OverlappingAndEarlyDisposedGeneratedEnumerationsRemainIndependent()
    {
        using SqliteConnection connection = Open();
        for (long id = 1; id <= 3; id++)
            NativeValueQueries.Save(connection, null, id, _text);

        using (IEnumerator<NativeValueRecord> first = NativeValueQueries.Find(connection, null, _text).GetEnumerator())
        {
            Assert.True(first.MoveNext());
            long firstId = first.Current.Id;
            using (IEnumerator<NativeValueRecord> second = NativeValueQueries.Find(connection, null, _text).GetEnumerator())
            {
                Assert.True(second.MoveNext());
                Assert.Equal(firstId, second.Current.Id);
                Assert.True(first.MoveNext());
                Assert.NotEqual(firstId, first.Current.Id);
            }
        }

        Assert.Equal([1L, 2L, 3L], NativeValueQueries.Find(connection, null, _text).Select(row => row.Id).Order());
        Assert.Single(NativeValueQueries.Find(connection, null, _text).Take(1));
        Assert.Equal(3, NativeValueQueries.Find(connection, null, _text).Count());
    }

    /// <summary>
    /// Discards native execution failures before the same generated shape is used successfully again.
    /// </summary>
    [Fact]
    public void ConstraintAndReaderFailuresDoNotPoisonLaterExecutions()
    {
        using SqliteConnection connection = Open();
        Assert.Throws<SqliteException>(() => CollectionQueries.InsertValue(connection, null, 0, [], null!));
        long value = CollectionQueries.InsertValue(connection, null, 0, [], new byte[8]);
        Assert.NotNull(CollectionQueries.ReadValue(connection, null, value));
        NativeValueQueries.Save(connection, null, 1, long.MaxValue);
        NativeValueQueries.Save(connection, null, 2, long.MaxValue);
        Assert.Throws<SqliteException>(() => AdvancedNativeQueries.SumNativeValues(connection, null));
        NativeValueQueries.Save(connection, null, 2, -1L);
        Assert.Equal(long.MaxValue - 1, Assert.IsType<long>(AdvancedNativeQueries.SumNativeValues(connection, null)!.Value));
    }

    /// <summary>
    /// Invalidates old leases across a connection close/reopen without mixing new and old databases.
    /// </summary>
    [Fact]
    public void ConnectionReopenRejectsOldPreparedStatementsAndLateLeaseReturns()
    {
        using SqliteConnection connection = Open();
        NativeValueQueries.Save(connection, null, 1, _text);
        IEnumerator<NativeValueRecord> old = NativeValueQueries.Find(connection, null, _text).GetEnumerator();
        Assert.True(old.MoveNext());
        connection.Close();
        connection.Open();
        Initialize(connection);
        NativeValueQueries.Save(connection, null, 2, _text);
        old.Dispose();
        Assert.Equal([2L], NativeValueQueries.Find(connection, null, _text).Select(row => row.Id));
    }

    /// <summary>
    /// Keeps identical generated query identities scoped to their externally owned connections.
    /// </summary>
    [Fact]
    public void DifferentConnectionsNeverSharePreparedCommandsOrValues()
    {
        using SqliteConnection first = Open();
        using SqliteConnection second = Open();
        NativeValueQueries.Save(first, null, 1, long.MinValue);
        NativeValueQueries.Save(second, null, 1, long.MaxValue);
        Assert.Equal(long.MinValue, NativeValueQueries.Read(first, null, 1)!.Value);
        Assert.Equal(long.MaxValue, NativeValueQueries.Read(second, null, 1)!.Value);
    }

    /// <summary>
    /// Frees large native bound buffers instead of retaining them in an idle prepared statement.
    /// </summary>
    [Fact]
    public void LargeInputsReleaseValuesAndBypassIdleRetention()
    {
        using SqliteConnection connection = Open();
        object identity = new();
        SqliteCommand original;
        byte[] payload = new byte[8192];
        payload[^1] = 255;
        using (SqliteCommandLease lease = Rent(connection, identity))
        {
            original = lease.Command;
            original.Parameters[0].Value = 1L;
            original.Parameters[1].Value = payload;
            Assert.Equal(1, lease.ExecuteNonQuery());
        }

        Assert.All(original.Parameters.Cast<SqliteParameter>(), parameter => Assert.Same(DBNull.Value, parameter.Value));
        using SqliteCommandLease next = Rent(connection, identity);
        Assert.NotSame(original, next.Command);
        Assert.Equal(payload, Assert.IsType<byte[]>(NativeValueQueries.Read(connection, null, 1)!.Value));
    }

    /// <summary>
    /// Keeps retained command capacity bounded while preserving correct execution after saturation.
    /// </summary>
    [Fact]
    public void CommandRetentionRemainsBoundedUnderDistinctIdentityPressure()
    {
        using SqliteConnection connection = Open();
        object[] identities = [.. Enumerable.Range(0, 256).Select(_ => new object())];
        SqliteCommand? last = null;
        foreach (object identity in identities)
        {
            using SqliteCommandLease lease = Rent(connection, identity);
            last = lease.Command;
        }

        using SqliteCommandLease next = Rent(connection, identities[^1]);
        Assert.NotSame(last, next.Command);
        next.Command.Parameters[0].Value = 1L;
        next.Command.Parameters[1].Value = 7L;
        Assert.Equal(1, next.ExecuteNonQuery());
        Assert.Equal(7L, NativeValueQueries.Read(connection, null, 1)!.Value);
    }

    /// <summary>
    /// Uses the generated native ANY command layout as a factory without authored SQL text.
    /// </summary>
    /// <param name="connection">The existing test connection.</param>
    /// <param name="identity">The isolated generated-layout identity for this test.</param>
    /// <returns>An exclusive command lease.</returns>
    private static SqliteCommandLease Rent(SqliteConnection connection, object identity)
        => SqliteCommandCache.Rent(connection, identity, static source => CacheFactoryQueries.CreateSaveCommand(source, null, 0L, null));

    /// <summary>
    /// Creates an isolated in-memory database using the retained native provider fixture.
    /// </summary>
    /// <returns>The opened caller-owned connection.</returns>
    private static SqliteConnection Open()
    {
        SqliteConnection connection = new(_memory);
        connection.Open();
        Initialize(connection);
        return connection;
    }

    /// <summary>
    /// Recreates the isolated schema after a native in-memory connection is reopened.
    /// </summary>
    /// <param name="connection">The already open isolated provider connection.</param>
    private static void Initialize(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = GeneratedCollectionQueriesTests.Resource(_schema);
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Supplies a generated native ANY factory for cache ownership and retention tests.
/// </summary>
[SqlQueries]
internal static partial class CacheFactoryQueries
{
    /// <summary>
    /// Declares the existing test schema's native parameter layout.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Inserts or replaces exact native values without provider coercion.
        /// </summary>
        /// <param name="id">The exact signed test identity.</param>
        /// <param name="value">The complete nullable native input.</param>
        /// <returns>The number of inserted or updated rows.</returns>
        [SqlCommandFactory]
        public static SqlQuery<int> Save(long id, object? value)
        {
            return Sql.Insert(new NativeValueRow { Id = id, Value = value })
                .OnConflict(row => row.Id).UpdateFromInserted(row => row.Value).ExecuteNonQuery();
        }
    }
}
