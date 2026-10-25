using Ironmon.Sqlite.Definitions;

namespace GeneratorFixture;

/// <summary>
/// Supplies a well-typed deletion that lacks the generator's required explicit filter.
/// </summary>
[SqlQueries]
internal static partial class UnfilteredDeleteQueries
{
    /// <summary>
    /// Defines the invalid deletion so build-time validation must reject it.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Attempts to remove all rows without an explicit Where clause.
        /// </summary>
        /// <returns>The deletion definition that must never become executable code.</returns>
        public static SqlQuery<int> Remove()
        {
            return Sql.From<Row>().Delete();
        }
    }
}

/// <summary>
/// Provides the mapped deletion target without irrelevant columns.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
}
