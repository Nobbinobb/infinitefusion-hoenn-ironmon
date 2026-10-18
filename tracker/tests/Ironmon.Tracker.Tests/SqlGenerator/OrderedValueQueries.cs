using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Exercises mapped ordering against native SQLite values and tie-breaker columns.
/// </summary>
[SqlQueries]
internal static partial class OrderedValueQueries
{
    /// <summary>
    /// Declares ordered operations over the existing independent native-value fixture.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Sorts retained native values ascending with descending identity tie-breakers.
        /// </summary>
        /// <param name="minimumId">The inclusive minimum signed 64-bit identity.</param>
        /// <returns>The matching rows in exact native SQLite order.</returns>
        public static SqlQuery<IEnumerable<NativeValueRecord>> Ascending(long minimumId)
        {
            return Sql.From<NativeValueRow>().Where(row => row.Id >= minimumId)
                .OrderBy(row => row.Value).ThenByDescending(row => row.Id)
                .Select(row => new NativeValueRecord { Id = row.Id, Value = row.Value })
                .AsEnumerable();
        }

        /// <summary>
        /// Sorts retained native values descending with ascending identity tie-breakers.
        /// </summary>
        /// <returns>All rows in exact native SQLite order.</returns>
        public static SqlQuery<IEnumerable<NativeValueRecord>> Descending()
        {
            return Sql.From<NativeValueRow>()
                .OrderByDescending(row => row.Value).ThenBy(row => row.Id)
                .Select(row => new NativeValueRecord { Id = row.Id, Value = row.Value })
                .AsEnumerable();
        }

        /// <summary>
        /// Applies sorting before the one-row limit when selecting the greatest identity.
        /// </summary>
        /// <returns>The greatest-identity row, or null when the fixture is empty.</returns>
        public static SqlQuery<NativeValueRecord?> Latest()
        {
            return Sql.From<NativeValueRow>().OrderByDescending(row => row.Id)
                .Select(row => new NativeValueRecord { Id = row.Id, Value = row.Value })
                .FirstOrDefault();
        }
    }
}
