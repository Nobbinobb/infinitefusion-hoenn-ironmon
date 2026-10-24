using Ironmon.Sqlite.Definitions;

namespace GeneratorFixture;

/// <summary>
/// Supplies a string addition that cannot be translated as native SQLite arithmetic.
/// </summary>
[SqlQueries]
internal static partial class UnsupportedQueries
{
    /// <summary>
    /// Contains the well-typed string expression requiring explicit compiler rejection.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Requires a diagnostic instead of substituting SQL numeric addition for concatenation.
        /// </summary>
        /// <param name="value">The exact bound suffix.</param>
        /// <returns>The unsupported mapped update definition.</returns>
        public static SqlQuery<int> Read(string value)
        {
            return Sql.From<Row>().AllRows().Update(row => new Row { Name = row.Name + value }).ExecuteNonQuery();
        }
    }
}

/// <summary>
/// Maps a text column whose native SQL addition would change its meaning.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
    private const string _name = "fixture_name";

    /// <summary>
    /// Gets or initializes the mapped text value.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;
}
