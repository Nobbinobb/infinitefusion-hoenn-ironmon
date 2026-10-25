using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Removes only an explicitly classified non-achievement measure population.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares narrowly keyed history-maintenance operations.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Removes one unrelated measure and its dependent scalar or membership rows atomically.
        /// </summary>
        /// <param name="series">The exact observation identity retained in the active profile.</param>
        /// <param name="measure">The complete measure ordinal.</param>
        /// <returns>The affected coverage-row count.</returns>
        public static SqlQuery<int> DeleteHistoryMeasure(long series, int measure)
        {
            return Sql.From<DirectCoverageRow>().Where(row => row.Series == series && row.Measure == measure).Delete();
        }

        /// <summary>
        /// Removes only the older ordered record selected for replacement during import.
        /// </summary>
        /// <param name="family">The exact family identity.</param>
        /// <param name="scope">The exact scope identity.</param>
        /// <param name="tuple">The complete ordered tuple identity.</param>
        /// <returns>The affected ordered-row count.</returns>
        public static SqlQuery<int> DeleteHistoryOrdered(int family, long scope, long tuple)
        {
            return Sql.From<OrderedStateRow>().Where(row => row.Family == family && row.Scope == scope && row.Tuple == tuple).Delete();
        }
    }
}
