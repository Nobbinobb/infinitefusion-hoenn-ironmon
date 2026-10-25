using Ironmon.Sqlite.Definitions;

namespace GeneratorFixture;

/// <summary>
/// Supplies an intentionally unsupported composed SQL definition.
/// </summary>
[SqlQueries]
internal static partial class UnsupportedQueries
{
    /// <summary>
    /// Contains the malformed but well-typed C# operation.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Requires a generator diagnostic without executable fallback code.
        /// </summary>
        /// <param name="value">The exact bound fixture text.</param>
        /// <returns>The unsupported compile-time operation.</returns>
        public static SqlQuery<Result?> Read(string value)
        {
            return Sql.From<Row>().Where(row => Sql.Like(row.Name, value) && row.Name.StartsWith(value)).Select(row => new Result { Name = row.Name }).FirstOrDefault();
        }
    }
}

/// <summary>
/// Maps a complete source column for the malformed definition.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
    private const string _name = "fixture_name";

    /// <summary>
    /// Gets or initializes the exact mapped source text.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Holds projected fixture text in two independently assigned properties.
/// </summary>
internal sealed class Result
{
    /// <summary>
    /// Gets or initializes the original source text.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes an independent projected text value.
    /// </summary>
    public string Other { get; init; } = string.Empty;
}
