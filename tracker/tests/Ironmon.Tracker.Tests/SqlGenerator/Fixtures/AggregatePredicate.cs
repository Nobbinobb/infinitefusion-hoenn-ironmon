using Ironmon.Sqlite.Querying;

namespace GeneratorFixture;

/// <summary>
/// Requests aggregate evaluation in a row predicate that SQLite does not permit.
/// </summary>
[SqlQueries]
internal static partial class UnsupportedQueries
{
    /// <summary>
    /// Contains a well-typed SQL definition with an unsupported aggregate scope.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Requires rejection before an aggregate-misuse error could occur at runtime.
        /// </summary>
        /// <returns>The unsupported filtered count definition.</returns>
        public static SqlQuery<long> Read()
        {
            return Sql.From<Row>().Where(row => Sql.Sum(row.Value) == null).Count();
        }
    }
}

/// <summary>
/// Maps the native value used by the invalid aggregate predicate.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
    private const string _value = "fixture_value";

    /// <summary>
    /// Gets or initializes the complete native value.
    /// </summary>
    [SqlColumn(_value)]
    public object Value { get; init; } = System.DBNull.Value;
}
