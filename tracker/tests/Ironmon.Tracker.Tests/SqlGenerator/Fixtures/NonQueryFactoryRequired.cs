using Ironmon.Sqlite.Querying;

namespace GeneratorFixture;

/// <summary>
/// Requests a reusable command for an operation whose scalar result requires its generated reader.
/// </summary>
[SqlQueries]
internal static partial class UnsupportedFactoryQueries
{
    /// <summary>
    /// Contains the well-typed scalar operation carrying an unsupported factory request.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Requests a factory for a scalar operation that must be rejected explicitly.
        /// </summary>
        /// <returns>The exact signed row count definition.</returns>
        [SqlCommandFactory]
        public static SqlQuery<long> Read()
        {
            return Sql.From<Row>().Count();
        }
    }
}

/// <summary>
/// Maps a source table for the otherwise valid scalar query.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
}
