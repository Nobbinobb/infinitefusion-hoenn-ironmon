using Ironmon.Sqlite.Querying;
using Ironmon.Tracker.Tests.Collection;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Checks ordered generated queries against SQLite storage classes, ties and transaction scope.
/// </summary>
public sealed class OrderedValueQueryTests
{
    private const string _memory = "Data Source=:memory:";
    private const string _schema = "Ironmon.Tracker.Tests.Collection.Fixtures.GeneratedQueries.sql";
    private const string _text = "a";

    /// <summary>
    /// Preserves SQLite null, numeric, text and binary ordering with both tie-breaker directions.
    /// </summary>
    [Fact]
    public void OrderedRowsRetainNativeStorageOrderAndAuthoredTieBreakers()
    {
        using SqliteConnection connection = Open();
        object?[] values = [null, null, -1L, 0L, 1.25d, _text, _text, new byte[] { 0 }, new byte[] { 0 }, new byte[] { 255 }];
        for (int index = 0; index < values.Length; index++)
            NativeValueQueries.Save(connection, null, index + 1, values[index]);

        Assert.Equal([2L, 1L, 3L, 4L, 5L, 7L, 6L, 9L, 8L, 10L], OrderedValueQueries.Ascending(connection, null, 0).Select(row => row.Id));
        Assert.Equal([3L, 4L, 5L, 7L, 6L, 9L, 8L, 10L], OrderedValueQueries.Ascending(connection, null, 3).Select(row => row.Id));
        Assert.Equal([10L, 8L, 9L, 6L, 7L, 5L, 4L, 3L, 1L, 2L], OrderedValueQueries.Descending(connection, null).Select(row => row.Id));
    }

    /// <summary>
    /// Applies LIMIT after ORDER BY and keeps the caller's transaction in charge of visibility.
    /// </summary>
    [Fact]
    public void OrderedFirstReadsTheGreatestIdentityAndRetainsTransactionOwnership()
    {
        using SqliteConnection connection = Open();
        Assert.Null(OrderedValueQueries.Latest(connection, null));
        NativeValueQueries.Save(connection, null, 1, null);
        using SqliteTransaction transaction = connection.BeginTransaction();
        NativeValueQueries.Save(connection, transaction, long.MaxValue, _text);
        Assert.Equal(long.MaxValue, OrderedValueQueries.Latest(connection, transaction)!.Id);
        Assert.Equal([1L, long.MaxValue], OrderedValueQueries.Ascending(connection, transaction, 0).Select(row => row.Id));
        transaction.Rollback();
        Assert.Equal(1L, OrderedValueQueries.Latest(connection, null)!.Id);
    }

    /// <summary>
    /// Prevents ordered definitions from acquiring an accidental runtime evaluation fallback.
    /// </summary>
    [Fact]
    public void OrderingBuildersRemainDefinitionOnly()
    {
        Assert.Throws<InvalidOperationException>(() => default(SqlSelection<NativeValueRow>).OrderBy(row => row.Id));
        Assert.Throws<InvalidOperationException>(() => default(SqlSelection<NativeValueRow>).OrderByDescending(row => row.Id));
        Assert.Throws<InvalidOperationException>(() => default(SqlSelection<NativeValueRow>).ThenBy(row => row.Id));
        Assert.Throws<InvalidOperationException>(() => default(SqlSelection<NativeValueRow>).ThenByDescending(row => row.Id));
    }

    /// <summary>
    /// Creates an isolated in-memory provider database from the independent native-value fixture.
    /// </summary>
    /// <returns>An opened connection owned by the calling test.</returns>
    private static SqliteConnection Open()
    {
        SqliteConnection connection = new(_memory);
        connection.Open();
        using SqliteCommand schema = connection.CreateCommand();
        schema.CommandText = GeneratedCollectionQueriesTests.Resource(_schema);
        schema.ExecuteNonQuery();
        return connection;
    }
}
