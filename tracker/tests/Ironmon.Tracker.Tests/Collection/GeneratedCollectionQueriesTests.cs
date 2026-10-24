using System.Reflection;
using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Exercises the real generated writer operations against the actual SQLite provider.
/// </summary>
public sealed class GeneratedCollectionQueriesTests
{
    private const string _memory = "Data Source=:memory:";
    private const string _schema = "Ironmon.Tracker.Tests.Collection.Fixtures.GeneratedQueries.sql";
    private const string _readScalar = "Ironmon.Tracker.Tests.Collection.Fixtures.ReadGeneratedScalar.sql";
    private const string _series = "$series";
    private const string _measure = "$measure";
    private const string _overflow = "922337203685477580800000000000000000000000001";
    private const string _domain = "signed_integer";

    /// <summary>
    /// Preserves identities above Int32, exact binary matches, collision candidates and uncommitted rollback.
    /// </summary>
    [Fact]
    public void DictionaryQueriesPreserveWideIdsBinaryCandidatesAndRollback()
    {
        using SqliteConnection connection = Open();
        byte[] digest = [0, 255, 1, 128, 3, 4, 5, 6];
        byte[] firstPayload = [0, 255, 128, 0];
        byte[] secondPayload = [];
        long first = CollectionQueries.InsertValue(connection, null, 3, firstPayload, digest);
        long second = CollectionQueries.InsertValue(connection, null, 3, secondPayload, digest);
        Assert.True(first > int.MaxValue);
        Assert.Equal(first + 1, second);
        CollectionValueRecord record = Assert.IsType<CollectionValueRecord>(CollectionQueries.ReadValue(connection, null, first));
        Assert.Equal(3, record.Kind);
        Assert.Equal(firstPayload, record.Payload);
        Assert.Equal(digest, record.Digest);
        Assert.Null(CollectionQueries.ReadValue(connection, null, long.MaxValue));
        CollectionValueCandidate[] candidates = [.. CollectionQueries.FindValues(connection, null, 3, [.. digest])];
        Assert.Equal([first, second], candidates.Select(candidate => candidate.Id).Order());
        Assert.Equal(firstPayload, candidates.Single(candidate => candidate.Id == first).Payload);
        Assert.Empty(candidates.Single(candidate => candidate.Id == second).Payload);
        Assert.Empty(CollectionQueries.FindValues(connection, null, 5, digest));

        using SqliteTransaction transaction = connection.BeginTransaction();
        long pending = CollectionQueries.InsertValue(connection, transaction, 3, firstPayload, digest);
        Assert.NotNull(CollectionQueries.ReadValue(connection, transaction, pending));
        using (IEnumerator<CollectionValueCandidate> iterator = CollectionQueries.FindValues(connection, transaction, 3, digest).GetEnumerator())
            Assert.True(iterator.MoveNext());

        transaction.Rollback();
        Assert.Null(CollectionQueries.ReadValue(connection, null, pending));
        Assert.Equal(2, CollectionQueries.FindValues(connection, null, 3, digest).Count());
    }

    /// <summary>
    /// Retains native Int64 bounds, exact decimal overflow, nulls and the original upsert column selection.
    /// </summary>
    [Fact]
    public void ScalarUpsertPreservesOverflowNullsMetadataAndExternalTransaction()
    {
        using SqliteConnection connection = Open();
        CollectionQueries.SaveScalar(connection, null, long.MaxValue, 5, 2, long.MinValue, null, long.MaxValue);
        var first = ReadScalar(connection);
        Assert.Equal(5L, first.Group);
        Assert.Equal(_domain, first.Domain);
        Assert.Equal(long.MinValue, first.Integer);
        Assert.Null(first.Wide);
        Assert.Equal(long.MaxValue, first.Witness);
        DirectScalarRecord selected = Assert.IsType<DirectScalarRecord>(CollectionQueries.ReadScalar(connection, null, long.MaxValue, 2));
        Assert.Equal(long.MinValue, selected.Integer);
        Assert.Null(selected.Wide);
        Assert.Null(CollectionQueries.ReadScalar(connection, null, long.MaxValue, 99));

        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            CollectionQueries.SaveScalar(connection, transaction, long.MaxValue, 99, 2, null, _overflow, null);
            var pending = ReadScalar(connection, transaction);
            Assert.Equal(5L, pending.Group);
            Assert.Null(pending.Integer);
            Assert.Equal(_overflow, pending.Wide);
            Assert.Null(pending.Witness);
            transaction.Rollback();
        }

        Assert.Equal(first, ReadScalar(connection));
        CollectionQueries.SaveScalar(connection, null, long.MaxValue, 99, 2, null, _overflow, null);
        var (Group, Domain, Integer, Wide, Witness) = ReadScalar(connection);
        Assert.Equal(5L, Group);
        Assert.Equal(_domain, Domain);
        Assert.Null(Integer);
        Assert.Equal(_overflow, Wide);
        Assert.Null(Witness);
        selected = Assert.IsType<DirectScalarRecord>(CollectionQueries.ReadScalar(connection, null, long.MaxValue, 2));
        Assert.Null(selected.Integer);
        Assert.Equal(_overflow, selected.Wide);
    }

    /// <summary>
    /// Opens a disposable in-memory database; no player profile or running tracker is involved.
    /// </summary>
    /// <returns>The caller-owned open SQLite connection.</returns>
    private static SqliteConnection Open()
    {
        SqliteConnection connection = new(_memory);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = Resource(_schema);
        command.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Reads generated-upsert results through an independently authored SQL control.
    /// </summary>
    /// <param name="connection">The caller-owned in-memory provider connection.</param>
    /// <param name="transaction">The external transaction whose pending values should be observed.</param>
    /// <returns>All original metadata and exact value storage columns.</returns>
    private static (long Group, string Domain, long? Integer, string? Wide, long? Witness) ReadScalar(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Resource(_readScalar);
        command.Parameters.AddWithValue(_series, long.MaxValue);
        command.Parameters.AddWithValue(_measure, 2);
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return (reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt64(4));
    }

    /// <summary>
    /// Loads a fixed test schema or independent control query without handwritten SQL in C# constants.
    /// </summary>
    /// <param name="name">The exact embedded fixture resource name.</param>
    /// <returns>The unchanged SQL fixture contents.</returns>
    internal static string Resource(string name)
    {
        using Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidDataException("A generated-query SQL fixture is missing.");
        using StreamReader reader = new(input);
        return reader.ReadToEnd();
    }
}
