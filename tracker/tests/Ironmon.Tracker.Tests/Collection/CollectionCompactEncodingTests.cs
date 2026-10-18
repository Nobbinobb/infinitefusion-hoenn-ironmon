using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies exact typed archives and source-string interning independently of real-play samples.
/// </summary>
public sealed class CollectionCompactEncodingTests
{
    private const string _memory = "Data Source=:memory:";
    private const string _strings = """
        {"values":["0123456789abcdef0123456789abcdef","0123456789ABCDEF0123456789ABCDEF","AbCdEf0123456789","0123456789abcdef0123456789abcdef:pokemon:123","0123456789abcdef0123456789abcdef:event:18446744073709551616000000000000","0123456789abcdef0123456789abcdef:pokemon:001","0000123","Pokémon雪\u0000"],"unknown":null,"zero":0,"text_zero":"0"}
        """;
    private const string _unused = "\"unreferenced test value\"";
    private const string _frame = """
        {"format":2,"scope":9,"families":[[7,{"format":2,"scope":9,"family":7,"series":[{"tuple":2,"cells":[[["i",1],["i",-9223372036854775808],["i",0],["i",9223372036854775807],["n"],["b",""],["b","FFFFFFFFFFFFFFFF"]]],"sets":[[["i",1],["i",2]]],"coverage":[[["i",1],["i",0],["i",0],["i",0],["i",0],["b","0000000000000000"]]]}],"ordered":[[["i",2],["n"],["i",2],["t","zlib-value-v2"],["b","00FF81"],["b","0123456789ABCDEF0123456789ABCDEF"],["b","FFFFFFFFFFFFFFFF"],["b","0000000000000000"]]]}]]}
        """;

    /// <summary>
    /// Retains every signed integer, binary value, type tag and frame relation through positional storage.
    /// </summary>
    [Fact]
    public void TypedArchiveRetainsExactSqlClassesAndRejectsTrailingOrTruncatedInput()
    {
        using JsonDocument source = JsonDocument.Parse(_frame);
        byte[] encoded = CollectionArchiveEncoding.Encode(source.RootElement);
        Assert.Equal(CollectionEncoding.Canonical(source.RootElement), CollectionEncoding.Canonical(CollectionArchiveEncoding.Decode(encoded)));
        Assert.Throws<InvalidDataException>(() => CollectionArchiveEncoding.Decode([.. encoded, 0]));
        Assert.ThrowsAny<Exception>(() => CollectionArchiveEncoding.Decode(encoded[..^1]));
    }

    /// <summary>
    /// Preserves source IDs, leading zeros, Unicode and very large source counters after a legacy upgrade and reclamation.
    /// </summary>
    [Fact]
    public void CompactNativeStringsRetainStableIdsAndAllReferencedComponents()
    {
        using SqliteConnection connection = new(_memory);
        connection.Open();
        CollectionSqliteMaintenance.CreateValueSchema(connection, null);

        CollectionValueStore store = new(connection, () => null, false);
        using JsonDocument source = JsonDocument.Parse(_strings);
        using JsonDocument unused = JsonDocument.Parse(_unused);
        long root = store.Intern(source.RootElement);
        long discarded = store.Intern(unused.RootElement);
        byte[] body = store.Encode(source.RootElement);
        store.CompactStoredStrings();
        HashSet<long> retained = [root];
        CollectionValueStore.CollectReferences(body, retained);
        store.ReclaimUnreferenced(retained);
        byte[] expected = CollectionEncoding.Canonical(source.RootElement);
        Assert.Equal(expected, CollectionEncoding.Canonical(store.Read(root)));
        Assert.Equal(expected, CollectionEncoding.Canonical(store.Decode(body)));
        Assert.Equal(root, store.Intern(source.RootElement));
        store.ClearCaches();
        Assert.Throws<InvalidDataException>(() => store.Read(discarded));
    }
}
