using System.Linq.Expressions;

namespace Ironmon.Sqlite.Querying;

/// <summary>
/// Supplies expression intrinsics translated only during source generation.
/// </summary>
public static partial class Sql
{
    /// <summary>
    /// Compares binary values with SQLite's native lexicographic BLOB ordering.
    /// </summary>
    /// <param name="left">The exact mapped or bound left binary operand.</param>
    /// <param name="right">The exact mapped or bound right binary operand.</param>
    /// <returns>Compile-time greater-than-or-equal syntax; runtime evaluation throws.</returns>
    public static bool BinaryGreaterThanOrEqual(byte[] left, byte[] right)
        => throw DefinitionOnly();

    /// <summary>
    /// Aggregates a native SQLite value without coercing its resulting storage class.
    /// </summary>
    /// <param name="value">The mapped value or supported SQL expression to sum.</param>
    /// <returns>The native integer, real or DBNull aggregate; runtime evaluation throws.</returns>
    public static object Sum(object? value)
        => throw DefinitionOnly();

    /// <summary>
    /// Tests for a matching row without materializing it, including correlated outer columns.
    /// </summary>
    /// <typeparam name="TTable">The mapped inner source table.</typeparam>
    /// <param name="predicate">The supported correlated inner-row condition.</param>
    /// <returns>Compile-time EXISTS syntax; runtime evaluation throws.</returns>
    public static bool Exists<TTable>(Expression<Func<TTable, bool>> predicate) where TTable : class
        => throw DefinitionOnly();

    /// <summary>
    /// Counts matching rows in a scalar correlated SQLite subquery.
    /// </summary>
    /// <typeparam name="TTable">The mapped inner source table.</typeparam>
    /// <param name="predicate">The supported correlated inner-row condition.</param>
    /// <returns>The exact signed 64-bit SQL count; runtime evaluation throws.</returns>
    public static long Count<TTable>(Expression<Func<TTable, bool>> predicate) where TTable : class
        => throw DefinitionOnly();

    /// <summary>
    /// Matches text using SQLite LIKE semantics, including percent and underscore wildcards.
    /// </summary>
    /// <param name="value">The mapped text or bound input to match.</param>
    /// <param name="pattern">The exact SQLite pattern.</param>
    /// <returns>Compile-time LIKE syntax; runtime evaluation throws.</returns>
    public static bool Like(string value, string pattern)
        => throw DefinitionOnly();

    /// <summary>
    /// Extracts bytes with SQLite's one-based substring indexing.
    /// </summary>
    /// <param name="value">The mapped binary value.</param>
    /// <param name="start">The one-based first byte index.</param>
    /// <param name="length">The maximum number of retained bytes.</param>
    /// <returns>Compile-time binary substring syntax; runtime evaluation throws.</returns>
    public static byte[] Substring(byte[] value, long start, long length)
        => throw DefinitionOnly();

    /// <summary>
    /// Extracts text with SQLite's one-based substring indexing.
    /// </summary>
    /// <param name="value">The mapped text value.</param>
    /// <param name="start">The one-based first character index.</param>
    /// <param name="length">The maximum number of retained characters.</param>
    /// <returns>Compile-time text substring syntax; runtime evaluation throws.</returns>
    public static string Substring(string value, long start, long length)
        => throw DefinitionOnly();
}

/// <summary>
/// Extends a mapped single-table selection with explicit maintenance and composition operations.
/// </summary>
public readonly partial struct SqlSelection<TTable> where TTable : class
{
    /// <summary>
    /// Explicitly permits an unfiltered update or deletion of this complete table.
    /// </summary>
    /// <returns>The compile-time selection carrying deliberate full-table intent.</returns>
    public SqlSelection<TTable> AllRows()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Suppresses duplicate projected rows using SQLite DISTINCT semantics.
    /// </summary>
    /// <returns>The compile-time distinct selection.</returns>
    public SqlSelection<TTable> Distinct()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Counts every row matching the authored filters and joins.
    /// </summary>
    /// <returns>A definition returning the exact signed 64-bit SQLite count.</returns>
    public SqlQuery<long> Count()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Assigns mapped columns using constants, parameters and supported current-row expressions.
    /// </summary>
    /// <param name="assignments">An explicit mapped object initializer containing only changed columns.</param>
    /// <returns>The compile-time update awaiting its execution terminal.</returns>
    public SqlUpdate<TTable> Update(Expression<Func<TTable, TTable>> assignments)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Adds a mapped inner join using all current source rows.
    /// </summary>
    /// <typeparam name="TJoined">The next mapped source table.</typeparam>
    /// <param name="predicate">The explicit two-source join condition.</param>
    /// <returns>The compile-time two-source selection.</returns>
    public SqlJoinedSelection<TTable, TJoined> Join<TJoined>(Expression<Func<TTable, TJoined, bool>> predicate) where TJoined : class
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Retains a mapped update until its generated provider operation is requested.
/// </summary>
/// <typeparam name="TTable">The mapped update target table.</typeparam>
public readonly struct SqlUpdate<TTable> where TTable : class
{
    /// <summary>
    /// Executes the update without changing caller-owned connection or transaction lifetime.
    /// </summary>
    /// <returns>A definition returning SQLite's affected-row count.</returns>
    public SqlQuery<int> ExecuteNonQuery()
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Extends conflict handling with explicit computations over current and excluded rows.
/// </summary>
public readonly partial struct SqlConflict<TTable> where TTable : class
{
    /// <summary>
    /// Assigns conflict updates with native SQL arithmetic, coalescing and bound values.
    /// </summary>
    /// <param name="assignments">A mapped initializer using the current stored row and incoming excluded row.</param>
    /// <returns>The compile-time insertion carrying the computed conflict update.</returns>
    public SqlInsertion<TTable> Update(Expression<Func<TTable, TTable, TTable>> assignments)
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Combines compatible result projections without introducing runtime query interpretation.
/// </summary>
public readonly partial struct SqlProjection<TResult> where TResult : class
{
    /// <summary>
    /// Combines both projections and suppresses duplicate complete projected rows.
    /// </summary>
    /// <param name="other">The supported projection with the same mapped result-property order.</param>
    /// <returns>The compile-time native SQLite UNION projection.</returns>
    public SqlProjection<TResult> Union(SqlProjection<TResult> other)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Combines both projections while retaining every original row.
    /// </summary>
    /// <param name="other">The supported projection with the same mapped result-property order.</param>
    /// <returns>The compile-time native SQLite UNION ALL projection.</returns>
    public SqlProjection<TResult> UnionAll(SqlProjection<TResult> other)
        => throw Sql.DefinitionOnly();
}
