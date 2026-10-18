using System.Linq.Expressions;

namespace Ironmon.Sqlite.Querying;

/// <summary>
/// Describes a checked inner-join selection over 2 mapped source tables.
/// </summary>
/// <typeparam name="T1">The mapped source table at position 1.</typeparam>
/// <typeparam name="T2">The mapped source table at position 2.</typeparam>
public readonly struct SqlJoinedSelection<T1, T2> where T1 : class where T2 : class
{
    /// <summary>
    /// Adds a supported condition over every current source row.
    /// </summary>
    /// <param name="predicate">The 2-source predicate with parameters in join order.</param>
    /// <returns>The filtered compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2> Where(Expression<Func<T1, T2, bool>> predicate)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Suppresses duplicate complete projected rows.
    /// </summary>
    /// <returns>The distinct compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2> Distinct()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Counts matching joined rows without materializing results.
    /// </summary>
    /// <returns>A definition returning the signed 64-bit row count.</returns>
    public SqlQuery<long> Count()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Projects mapped columns and supported SQL expressions into an explicit result initializer.
    /// </summary>
    /// <typeparam name="TResult">The exact generated result row type.</typeparam>
    /// <param name="projection">The 2-source expression with parameters in join order.</param>
    /// <returns>The compile-time typed projection.</returns>
    public SqlProjection<TResult> Select<TResult>(Expression<Func<T1, T2, TResult>> projection) where TResult : class
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins an ascending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 2-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2> OrderBy<TColumn>(Expression<Func<T1, T2, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins a descending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 2-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2> OrderByDescending<TColumn>(Expression<Func<T1, T2, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends an ascending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 2-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2> ThenBy<TColumn>(Expression<Func<T1, T2, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends a descending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 2-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2> ThenByDescending<TColumn>(Expression<Func<T1, T2, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Adds the next mapped inner source using an explicit all-source condition.
    /// </summary>
    /// <typeparam name="TJoined">The next mapped source table.</typeparam>
    /// <param name="predicate">The 3-source join condition in source order.</param>
    /// <returns>The extended compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, TJoined> Join<TJoined>(Expression<Func<T1, T2, TJoined, bool>> predicate) where TJoined : class
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Describes a checked inner-join selection over 3 mapped source tables.
/// </summary>
/// <typeparam name="T1">The mapped source table at position 1.</typeparam>
/// <typeparam name="T2">The mapped source table at position 2.</typeparam>
/// <typeparam name="T3">The mapped source table at position 3.</typeparam>
public readonly struct SqlJoinedSelection<T1, T2, T3> where T1 : class where T2 : class where T3 : class
{
    /// <summary>
    /// Adds a supported condition over every current source row.
    /// </summary>
    /// <param name="predicate">The 3-source predicate with parameters in join order.</param>
    /// <returns>The filtered compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3> Where(Expression<Func<T1, T2, T3, bool>> predicate)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Suppresses duplicate complete projected rows.
    /// </summary>
    /// <returns>The distinct compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3> Distinct()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Counts matching joined rows without materializing results.
    /// </summary>
    /// <returns>A definition returning the signed 64-bit row count.</returns>
    public SqlQuery<long> Count()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Projects mapped columns and supported SQL expressions into an explicit result initializer.
    /// </summary>
    /// <typeparam name="TResult">The exact generated result row type.</typeparam>
    /// <param name="projection">The 3-source expression with parameters in join order.</param>
    /// <returns>The compile-time typed projection.</returns>
    public SqlProjection<TResult> Select<TResult>(Expression<Func<T1, T2, T3, TResult>> projection) where TResult : class
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins an ascending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 3-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3> OrderBy<TColumn>(Expression<Func<T1, T2, T3, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins a descending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 3-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3> OrderByDescending<TColumn>(Expression<Func<T1, T2, T3, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends an ascending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 3-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3> ThenBy<TColumn>(Expression<Func<T1, T2, T3, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends a descending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 3-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3> ThenByDescending<TColumn>(Expression<Func<T1, T2, T3, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Adds the next mapped inner source using an explicit all-source condition.
    /// </summary>
    /// <typeparam name="TJoined">The next mapped source table.</typeparam>
    /// <param name="predicate">The 4-source join condition in source order.</param>
    /// <returns>The extended compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, TJoined> Join<TJoined>(Expression<Func<T1, T2, T3, TJoined, bool>> predicate) where TJoined : class
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Describes a checked inner-join selection over 4 mapped source tables.
/// </summary>
/// <typeparam name="T1">The mapped source table at position 1.</typeparam>
/// <typeparam name="T2">The mapped source table at position 2.</typeparam>
/// <typeparam name="T3">The mapped source table at position 3.</typeparam>
/// <typeparam name="T4">The mapped source table at position 4.</typeparam>
public readonly struct SqlJoinedSelection<T1, T2, T3, T4> where T1 : class where T2 : class where T3 : class where T4 : class
{
    /// <summary>
    /// Adds a supported condition over every current source row.
    /// </summary>
    /// <param name="predicate">The 4-source predicate with parameters in join order.</param>
    /// <returns>The filtered compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4> Where(Expression<Func<T1, T2, T3, T4, bool>> predicate)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Suppresses duplicate complete projected rows.
    /// </summary>
    /// <returns>The distinct compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4> Distinct()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Counts matching joined rows without materializing results.
    /// </summary>
    /// <returns>A definition returning the signed 64-bit row count.</returns>
    public SqlQuery<long> Count()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Projects mapped columns and supported SQL expressions into an explicit result initializer.
    /// </summary>
    /// <typeparam name="TResult">The exact generated result row type.</typeparam>
    /// <param name="projection">The 4-source expression with parameters in join order.</param>
    /// <returns>The compile-time typed projection.</returns>
    public SqlProjection<TResult> Select<TResult>(Expression<Func<T1, T2, T3, T4, TResult>> projection) where TResult : class
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins an ascending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 4-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4> OrderBy<TColumn>(Expression<Func<T1, T2, T3, T4, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins a descending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 4-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4> OrderByDescending<TColumn>(Expression<Func<T1, T2, T3, T4, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends an ascending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 4-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4> ThenBy<TColumn>(Expression<Func<T1, T2, T3, T4, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends a descending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 4-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4> ThenByDescending<TColumn>(Expression<Func<T1, T2, T3, T4, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Adds the next mapped inner source using an explicit all-source condition.
    /// </summary>
    /// <typeparam name="TJoined">The next mapped source table.</typeparam>
    /// <param name="predicate">The 5-source join condition in source order.</param>
    /// <returns>The extended compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, TJoined> Join<TJoined>(Expression<Func<T1, T2, T3, T4, TJoined, bool>> predicate) where TJoined : class
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Describes a checked inner-join selection over 5 mapped source tables.
/// </summary>
/// <typeparam name="T1">The mapped source table at position 1.</typeparam>
/// <typeparam name="T2">The mapped source table at position 2.</typeparam>
/// <typeparam name="T3">The mapped source table at position 3.</typeparam>
/// <typeparam name="T4">The mapped source table at position 4.</typeparam>
/// <typeparam name="T5">The mapped source table at position 5.</typeparam>
public readonly struct SqlJoinedSelection<T1, T2, T3, T4, T5> where T1 : class where T2 : class where T3 : class where T4 : class where T5 : class
{
    /// <summary>
    /// Adds a supported condition over every current source row.
    /// </summary>
    /// <param name="predicate">The 5-source predicate with parameters in join order.</param>
    /// <returns>The filtered compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5> Where(Expression<Func<T1, T2, T3, T4, T5, bool>> predicate)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Suppresses duplicate complete projected rows.
    /// </summary>
    /// <returns>The distinct compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5> Distinct()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Counts matching joined rows without materializing results.
    /// </summary>
    /// <returns>A definition returning the signed 64-bit row count.</returns>
    public SqlQuery<long> Count()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Projects mapped columns and supported SQL expressions into an explicit result initializer.
    /// </summary>
    /// <typeparam name="TResult">The exact generated result row type.</typeparam>
    /// <param name="projection">The 5-source expression with parameters in join order.</param>
    /// <returns>The compile-time typed projection.</returns>
    public SqlProjection<TResult> Select<TResult>(Expression<Func<T1, T2, T3, T4, T5, TResult>> projection) where TResult : class
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins an ascending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 5-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5> OrderBy<TColumn>(Expression<Func<T1, T2, T3, T4, T5, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins a descending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 5-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5> OrderByDescending<TColumn>(Expression<Func<T1, T2, T3, T4, T5, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends an ascending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 5-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5> ThenBy<TColumn>(Expression<Func<T1, T2, T3, T4, T5, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends a descending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 5-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5> ThenByDescending<TColumn>(Expression<Func<T1, T2, T3, T4, T5, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Adds the next mapped inner source using an explicit all-source condition.
    /// </summary>
    /// <typeparam name="TJoined">The next mapped source table.</typeparam>
    /// <param name="predicate">The 6-source join condition in source order.</param>
    /// <returns>The extended compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, TJoined> Join<TJoined>(Expression<Func<T1, T2, T3, T4, T5, TJoined, bool>> predicate) where TJoined : class
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Describes a checked inner-join selection over 6 mapped source tables.
/// </summary>
/// <typeparam name="T1">The mapped source table at position 1.</typeparam>
/// <typeparam name="T2">The mapped source table at position 2.</typeparam>
/// <typeparam name="T3">The mapped source table at position 3.</typeparam>
/// <typeparam name="T4">The mapped source table at position 4.</typeparam>
/// <typeparam name="T5">The mapped source table at position 5.</typeparam>
/// <typeparam name="T6">The mapped source table at position 6.</typeparam>
public readonly struct SqlJoinedSelection<T1, T2, T3, T4, T5, T6> where T1 : class where T2 : class where T3 : class where T4 : class where T5 : class where T6 : class
{
    /// <summary>
    /// Adds a supported condition over every current source row.
    /// </summary>
    /// <param name="predicate">The 6-source predicate with parameters in join order.</param>
    /// <returns>The filtered compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, T6> Where(Expression<Func<T1, T2, T3, T4, T5, T6, bool>> predicate)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Suppresses duplicate complete projected rows.
    /// </summary>
    /// <returns>The distinct compile-time joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, T6> Distinct()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Counts matching joined rows without materializing results.
    /// </summary>
    /// <returns>A definition returning the signed 64-bit row count.</returns>
    public SqlQuery<long> Count()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Projects mapped columns and supported SQL expressions into an explicit result initializer.
    /// </summary>
    /// <typeparam name="TResult">The exact generated result row type.</typeparam>
    /// <param name="projection">The 6-source expression with parameters in join order.</param>
    /// <returns>The compile-time typed projection.</returns>
    public SqlProjection<TResult> Select<TResult>(Expression<Func<T1, T2, T3, T4, T5, T6, TResult>> projection) where TResult : class
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins an ascending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 6-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, T6> OrderBy<TColumn>(Expression<Func<T1, T2, T3, T4, T5, T6, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins a descending ordering using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 6-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, T6> OrderByDescending<TColumn>(Expression<Func<T1, T2, T3, T4, T5, T6, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends an ascending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 6-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, T6> ThenBy<TColumn>(Expression<Func<T1, T2, T3, T4, T5, T6, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends a descending tie-breaker using one directly mapped source column.
    /// </summary>
    /// <typeparam name="TColumn">The exact mapped ordering-column type.</typeparam>
    /// <param name="column">The 6-source selector returning one mapped property.</param>
    /// <returns>The compile-time ordered joined selection.</returns>
    public SqlJoinedSelection<T1, T2, T3, T4, T5, T6> ThenByDescending<TColumn>(Expression<Func<T1, T2, T3, T4, T5, T6, TColumn>> column)
        => throw Sql.DefinitionOnly();

}
