using Ironmon.Tracker.Collection.Storage.Encoding;
using Ironmon.Tracker.Collection.Storage.Maintenance;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies exact typed storage independently of the gameplay reducer's field selection.
/// </summary>
public sealed class CollectionStorageEncodingTests
{
    private const string _memory = "Data Source=:memory:";
    private const string _completeValue = """
        {"unicode":"Pokémon\u0000雪","empty":[],"unknown":{"coverage":"unknown","value":null},"known":{"coverage":"known","value":0},"numbers":[0,-1,9223372036854775808,1267650600228229401496703205376,1.00,1e+2,-0],"types":[null,false,true,"1",1,{},[]]}
        """;
    private const string _number = "1";
    private const string _text = "\"1\"";
    private const string _deleteDigestSql = "UPDATE collection_values SET digest=zeroblob(8) WHERE value_id=$id";
    private const string _idParameter = "$id";

    /// <summary>
    /// Preserves arbitrary integers, numeric spellings, Unicode, empty containers and availability distinctions.
    /// </summary>
    [Fact]
    public void TypedReferencesAndInlineBodiesRoundTripEveryValueKindExactly()
    {
        using SqliteConnection connection = new(_memory);
        connection.Open();
        CollectionSqliteMaintenance.CreateValueSchema(connection, null);

        CollectionValueStore store = new(connection, () => null);
        using JsonDocument source = JsonDocument.Parse(_completeValue);
        byte[] expected = CollectionEncoding.Canonical(source.RootElement);
        long id = store.Intern(source.RootElement);
        byte[] body = store.Encode(source.RootElement);
        store.ClearCaches();
        Assert.Equal(expected, CollectionEncoding.Canonical(store.Read(id)));
        Assert.Equal(expected, CollectionEncoding.Canonical(store.Decode(body)));
        Assert.Equal(id, store.Intern(source.RootElement));
        using JsonDocument number = JsonDocument.Parse(_number);
        using JsonDocument text = JsonDocument.Parse(_text);
        Assert.NotEqual(store.Intern(number.RootElement), store.Intern(text.RootElement));
        Assert.Throws<InvalidDataException>(() => store.Decode([.. body, 0]));
        Assert.Throws<InvalidDataException>(() => store.Decode([7, 127]));
    }

    /// <summary>
    /// Rejects corrupt referenced data rather than returning an invented value after a damaged disk read.
    /// </summary>
    [Fact]
    public void ImmutableValueDigestIsVerifiedAfterReopeningTheReader()
    {
        using SqliteConnection connection = new(_memory);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        CollectionSqliteMaintenance.CreateValueSchema(connection, null);
        CollectionValueStore store = new(connection, () => null);
        using JsonDocument source = JsonDocument.Parse(_text);
        long id = store.Intern(source.RootElement);
        command.CommandText = _deleteDigestSql;
        command.Parameters.AddWithValue(_idParameter, id);
        command.ExecuteNonQuery();
        Assert.Throws<InvalidDataException>(() => new CollectionValueStore(connection, () => null).Read(id));
    }
}

/// <summary>
/// Reads exact persisted values while integration tests verify actual SQL reductions.
/// </summary>
public sealed partial class CollectionStoreTests
{
    /// <summary>
    /// Decodes an immutable typed value through a separate read-only database connection.
    /// </summary>
    /// <param name="directory">The disposable collection directory.</param>
    /// <param name="id">The actual immutable value foreign key.</param>
    /// <returns>The complete exact stored value.</returns>
    private static JsonElement ReadStoredValue(string directory, long id)
    {
        using SqliteConnection connection = OpenRead(directory);
        return new CollectionValueStore(connection, () => null).Read(id);
    }

    /// <summary>
    /// Decodes a typed binary detail body through a separate read-only connection.
    /// </summary>
    /// <param name="directory">The disposable collection directory.</param>
    /// <param name="bytes">The already inflated version-2 detail or version-3 archive body.</param>
    /// <returns>The complete exact detail value.</returns>
    private static JsonElement DecodeStoredValue(string directory, byte[] bytes)
    {
        using SqliteConnection connection = OpenRead(directory);
        return bytes is [65, 67, 51, ..] ? CollectionArchiveEncoding.Decode(bytes) : new CollectionValueStore(connection, () => null).Decode(bytes);
    }
}
