using Ironmon.Sqlite.Querying;
using Ironmon.Tracker.Tests.Collection;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Checks native storage classes and failure behavior of the generated provider code.
/// </summary>
public sealed class NativeValueQueryTests
{
    private const string _memory = "Data Source=:memory:";
    private const string _schema = "Ironmon.Tracker.Tests.Collection.Fixtures.GeneratedQueries.sql";
    private const string _text = "' OR 1=1; -- Pokémon雪\0";

    /// <summary>
    /// Retains native integer bounds, binary arrays, text, real values and known null without a codec.
    /// </summary>
    [Fact]
    public void NativeAnyValuesRetainTheirSqliteStorageClassesAndNullEquality()
    {
        using SqliteConnection connection = new(_memory);
        connection.Open();
        using (SqliteCommand schema = connection.CreateCommand())
        {
            schema.CommandText = GeneratedCollectionQueriesTests.Resource(_schema);
            schema.ExecuteNonQuery();
        }

        object?[] values = [long.MinValue, long.MaxValue, new byte[] { 0, 255, 128, 0 }, Array.Empty<byte>(), _text, 1.25d, null];
        for (int index = 0; index < values.Length; index++)
        {
            NativeValueQueries.Save(connection, null, index, values[index]);
            NativeValueRecord row = Assert.IsType<NativeValueRecord>(NativeValueQueries.Read(connection, null, index));
            Assert.Equal(index, row.Id);
            if (values[index] is byte[] binary)
                Assert.Equal(binary, Assert.IsType<byte[]>(row.Value));
            else
                Assert.Equal(values[index], row.Value);

            Assert.Equal(index, Assert.Single(NativeValueQueries.Find(connection, null, values[index])).Id);
        }

        Assert.Null(NativeValueQueries.Read(connection, null, long.MaxValue));
        NativeValueQueries.Save(connection, null, 0, _text);
        Assert.Equal(_text, NativeValueQueries.Read(connection, null, 0)!.Value);
    }

    /// <summary>
    /// Prevents accidental runtime interpretation of authored definitions.
    /// </summary>
    [Fact]
    public void DefinitionBuildersFailInsteadOfInterpretingQueriesAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.From<NativeValueRow>());
        Assert.Throws<InvalidOperationException>(() => default(SqlInsertion<NativeValueRow>).OnConflict());
    }
}
