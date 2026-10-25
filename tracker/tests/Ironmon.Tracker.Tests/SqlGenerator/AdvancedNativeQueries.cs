using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Exercises composed provider operations over an independent existing native-value fixture.
/// </summary>
[SqlQueries]
internal static partial class AdvancedNativeQueries
{
    /// <summary>
    /// Declares exact native computations without runtime expression interpretation.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Counts rows by native lexicographic binary comparison rather than managed array identity.
        /// </summary>
        /// <param name="minimum">The inclusive exact binary minimum.</param>
        /// <returns>The signed 64-bit count of matching binary values.</returns>
        public static SqlQuery<long> CountBinaryAtLeast(byte[] minimum)
        {
            return Sql.From<AdvancedBlobRow>().Where(row => Sql.BinaryGreaterThanOrEqual(row.Value, minimum)).Count();
        }

        /// <summary>
        /// Returns SQLite's native nullable aggregate without substituting a zero or narrowing real values.
        /// </summary>
        /// <returns>The native aggregate row, including DBNull for an empty source.</returns>
        public static SqlQuery<AdvancedSumRecord?> SumNativeValues()
        {
            return Sql.From<NativeValueRow>()
                .Select(row => new AdvancedSumRecord { Value = Sql.Sum(row.Value) }).FirstOrDefault();
        }

        /// <summary>
        /// Adds the incoming nullable counter to retained state with native SQL coalescing.
        /// </summary>
        /// <param name="id">The exact test counter identity.</param>
        /// <param name="value">The incoming amount, or null for a known zero contribution.</param>
        /// <returns>The number of inserted or updated rows.</returns>
        public static SqlQuery<int> Accumulate(long id, long? value)
        {
            return Sql.Insert(new AdvancedCounterRow { Id = id, Value = value })
                .OnConflict(row => row.Id)
                .Update((current, incoming) => new AdvancedCounterRow { Value = (current.Value ?? 0L) + (incoming.Value ?? 0L) })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Updates one exact identity with arithmetic over the existing mapped row.
        /// </summary>
        /// <param name="id">The original identity to update.</param>
        /// <param name="delta">The signed amount added to the existing identity.</param>
        /// <returns>The number of changed rows.</returns>
        public static SqlQuery<int> MoveIdentity(long id, long delta)
        {
            return Sql.From<AdvancedCounterRow>().Where(row => row.Id == id)
                .Update(row => new AdvancedCounterRow { Id = row.Id + delta }).ExecuteNonQuery();
        }

        /// <summary>
        /// Clears every counter only after explicitly declaring complete-table intent.
        /// </summary>
        /// <returns>The number of changed rows.</returns>
        public static SqlQuery<int> ClearCounters()
        {
            return Sql.From<AdvancedCounterRow>().AllRows()
                .Update(row => new AdvancedCounterRow { Value = 0L }).ExecuteNonQuery();
        }

        /// <summary>
        /// Counts rows with exact full-width identities above an inclusive boundary.
        /// </summary>
        /// <param name="minimum">The inclusive signed 64-bit minimum identity.</param>
        /// <returns>The exact signed 64-bit row count.</returns>
        public static SqlQuery<long> CountAtLeast(long minimum)
        {
            return Sql.From<AdvancedCounterRow>().Where(row => row.Id >= minimum).Count();
        }

        /// <summary>
        /// Counts later identities using independent correlated source aliases.
        /// </summary>
        /// <returns>Each identity and the exact count of rows with greater identities.</returns>
        public static SqlQuery<IEnumerable<AdvancedPairRecord>> CorrelatedCounts()
        {
            return Sql.From<AdvancedCounterRow>().OrderBy(row => row.Id)
                .Select(row => new AdvancedPairRecord { Id = row.Id, Other = Sql.Count<AdvancedCounterRow>(candidate => candidate.Id > row.Id) }).AsEnumerable();
        }

        /// <summary>
        /// Joins the same physical table using separate mapped source aliases and a bound offset.
        /// </summary>
        /// <param name="offset">The exact signed offset between matching identities.</param>
        /// <returns>Each matching pair in ascending left identity order.</returns>
        public static SqlQuery<IEnumerable<AdvancedPairRecord>> JoinedPairs(long offset)
        {
            return Sql.From<AdvancedCounterRow>().Join<AdvancedCounterRow>((left, right) => left.Id + offset == right.Id)
                .OrderBy((left, right) => left.Id)
                .Select((left, right) => new AdvancedPairRecord { Id = left.Id, Other = right.Id }).AsEnumerable();
        }

        /// <summary>
        /// Combines three compatible branches with native duplicate suppression.
        /// </summary>
        /// <returns>Each complete identity exactly once, paired with a constant projection.</returns>
        public static SqlQuery<IEnumerable<AdvancedPairRecord>> DistinctUnion()
        {
            return Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = row.Id, Other = 0L })
                .Union(Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = row.Id, Other = 0L }))
                .Union(Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = row.Id, Other = 0L })).AsEnumerable();
        }

        /// <summary>
        /// Retains every original row from both compatible branches.
        /// </summary>
        /// <returns>Both complete branch populations without deduplication.</returns>
        public static SqlQuery<IEnumerable<AdvancedPairRecord>> CompleteUnion()
        {
            return Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = row.Id, Other = 0L })
                .UnionAll(Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = row.Id, Other = 0L })).AsEnumerable();
        }

        /// <summary>
        /// Uses the declared Int64 result width across constant and full-width UNION branches.
        /// </summary>
        /// <returns>A constant row and the complete original identities without Int32 narrowing.</returns>
        public static SqlQuery<IEnumerable<AdvancedPairRecord>> WidenedUnion()
        {
            return Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = 0, Other = 0 })
                .UnionAll(Sql.From<AdvancedCounterRow>().Select(row => new AdvancedPairRecord { Id = row.Id, Other = row.Id })).AsEnumerable();
        }

        /// <summary>
        /// Deletes rows only when the correlated inner source proves no matching successor exists.
        /// </summary>
        /// <param name="offset">The exact signed successor offset.</param>
        /// <returns>The number of deleted rows.</returns>
        public static SqlQuery<int> DeleteWithoutSuccessor(long offset)
        {
            return Sql.From<AdvancedCounterRow>()
                .Where(row => !Sql.Exists<AdvancedCounterRow>(candidate => candidate.Id == row.Id + offset)).Delete();
        }
    }
}

/// <summary>
/// Maps the independent native fixture's nullable integer subset for computation tests.
/// </summary>
[SqlTable(_table)]
internal sealed class AdvancedCounterRow
{
    private const string _table = "generator_native_values";
    private const string _id = "native_id";
    private const string _value = "native_value";

    /// <summary>
    /// Gets or initializes the full-width signed identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the retained nullable integer amount.
    /// </summary>
    [SqlColumn(_value)]
    public long? Value { get; init; }
}

/// <summary>
/// Carries two independently projected signed 64-bit values.
/// </summary>
internal sealed class AdvancedPairRecord
{
    /// <summary>
    /// Gets or initializes the exact source identity.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the joined identity, correlated count or constant value.
    /// </summary>
    public long Other { get; init; }
}

/// <summary>
/// Retains native SQLite aggregate storage classes including an empty source's DBNull result.
/// </summary>
internal sealed class AdvancedSumRecord
{
    /// <summary>
    /// Gets or initializes the native integer, real or database-null aggregate.
    /// </summary>
    public object Value { get; init; } = DBNull.Value;
}

/// <summary>
/// Maps the independent native fixture's BLOB subset for binary-ordering tests.
/// </summary>
[SqlTable(_table)]
internal sealed class AdvancedBlobRow
{
    private const string _table = "generator_native_values";
    private const string _value = "native_value";

    /// <summary>
    /// Gets or initializes the exact binary value retained by SQLite.
    /// </summary>
    [SqlColumn(_value)]
    public byte[] Value { get; init; } = [];
}
