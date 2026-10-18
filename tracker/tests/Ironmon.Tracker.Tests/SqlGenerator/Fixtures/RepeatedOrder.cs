using Ironmon.Sqlite.Querying;

namespace GeneratorFixture;

/// <summary>
/// Supplies a well-typed ordering definition that must fail compilation.
/// </summary>
[SqlQueries]
internal static partial class UnsupportedQueries
{
    /// <summary>
    /// Declares the intentionally unsupported ordering chain.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Isolates one unsupported ordering shape without an unrelated type error.
        /// </summary>
        /// <returns>The requested first matching row definition.</returns>
        public static SqlQuery<Result?> Read()
        {
            return Sql.From<Row>().OrderBy(row => row.Name).OrderByDescending(row => row.Name)
                .Select(row => new Result { Name = row.Name }).FirstOrDefault();
        }
    }
}

/// <summary>
/// Provides a mapped table for the malformed fluent ordering chain.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
    private const string _name = "fixture_name";

    /// <summary>
    /// Gets or initializes the mapped text column.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Holds the projected text without additional query operations.
/// </summary>
internal sealed class Result
{
    /// <summary>
    /// Gets or initializes the selected text.
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
