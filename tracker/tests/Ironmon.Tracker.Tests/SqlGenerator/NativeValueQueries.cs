using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Exercises native SQLite ANY bindings and nullable readers independently of production query models.
/// </summary>
[SqlQueries]
internal static partial class NativeValueQueries
{
    /// <summary>
    /// Declares the supported native value operations used by provider correctness tests.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Inserts or replaces the exact native storage-class value for a test identity.
        /// </summary>
        /// <param name="id">The exact signed 64-bit test identity.</param>
        /// <param name="value">An integer, binary array, text, real value or null, passed without coercion.</param>
        /// <returns>The number of inserted or updated rows.</returns>
        public static SqlQuery<int> Save(long id, object? value)
        {
            return Sql.Insert(new NativeValueRow { Id = id, Value = value })
                .OnConflict(row => row.Id)
                .UpdateFromInserted(row => row.Value)
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves a nullable native value without mistaking a known null for an absent row.
        /// </summary>
        /// <param name="id">The exact test identity.</param>
        /// <returns>The row and its uncoerced native value, or null for an absent identity.</returns>
        public static SqlQuery<NativeValueRecord?> Read(long id)
        {
            return Sql.From<NativeValueRow>().Where(row => row.Id == id)
                .Select(row => new NativeValueRecord { Id = row.Id, Value = row.Value })
                .FirstOrDefault();
        }

        /// <summary>
        /// Filters with SQLite null-safe value equality, including binary and null operands.
        /// </summary>
        /// <param name="value">The exact native comparison value, including null.</param>
        /// <returns>Every row with the same SQLite value.</returns>
        public static SqlQuery<IEnumerable<NativeValueRecord>> Find(object? value)
        {
            return Sql.From<NativeValueRow>().Where(row => row.Value == value)
                .Select(row => new NativeValueRecord { Id = row.Id, Value = row.Value })
                .AsEnumerable();
        }
    }
}

/// <summary>
/// Maps an independent STRICT ANY fixture table using constant-resolved physical identifiers.
/// </summary>
[SqlTable(_table)]
internal sealed class NativeValueRow
{
    private const string _table = "generator_native_values";
    private const string _id = "native_id";
    private const string _value = "native_value";

    /// <summary>
    /// Gets or initializes the signed 64-bit test identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the nullable native storage-class value.
    /// </summary>
    [SqlColumn(_value)]
    public object? Value { get; init; }
}

/// <summary>
/// Holds a nullable native value while retaining the presence of its enclosing row.
/// </summary>
internal sealed class NativeValueRecord
{
    /// <summary>
    /// Gets or initializes the selected signed 64-bit identity.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact selected native value, including known null.
    /// </summary>
    public object? Value { get; init; }
}
