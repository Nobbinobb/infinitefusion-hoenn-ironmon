using System.Linq.Expressions;

namespace Ironmon.Sqlite.Definitions;

/// <summary>
/// Generates SQLite execution methods from the attributed partial class's nested Definitions class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SqlQueriesAttribute : Attribute;

/// <summary>
/// Maps a query model to an existing physical SQLite table without creating or modifying it.
/// </summary>
/// <remarks>
/// Declares the physical table name used by build-time query translation.
/// </remarks>
/// <param name="name">The exact physical table name, which the generator quotes as an identifier.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SqlTableAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the exact physical table name.
    /// </summary>
    public string Name { get; } = name;
}

/// <summary>
/// Maps a query model property to an existing physical SQLite column.
/// </summary>
/// <remarks>
/// Declares the physical column name used by build-time query translation.
/// </remarks>
/// <param name="name">The exact physical column name, which the generator quotes as an identifier.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SqlColumnAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the exact physical column name.
    /// </summary>
    public string Name { get; } = name;
}

/// <summary>
/// Marks the result type of a query definition that is translated during compilation.
/// </summary>
/// <typeparam name="TResult">The generated execution method's exact return type.</typeparam>
public readonly struct SqlQuery<TResult>;

/// <summary>
/// Provides typed query syntax that the generator analyzes without executing its definitions.
/// </summary>
/// <remarks>
/// Call generated methods on the attributed outer class at runtime. Definition builders deliberately
/// throw if invoked; there is no runtime interpreter or fallback to expression-tree translation.
/// </remarks>
public static partial class Sql
{
    /// <summary>
    /// Begins a selection from a mapped existing table.
    /// </summary>
    /// <typeparam name="TTable">The model carrying the physical table and column mappings.</typeparam>
    /// <returns>Compile-time selection syntax; invoking the definition at runtime throws.</returns>
    public static SqlSelection<TTable> From<TTable>() where TTable : class
        => throw DefinitionOnly();

    /// <summary>
    /// Begins an insertion using the explicitly assigned model properties.
    /// </summary>
    /// <typeparam name="TTable">The mapped target table model.</typeparam>
    /// <param name="values">An object initializer containing method parameters or constants.</param>
    /// <returns>Compile-time insertion syntax; invoking the definition at runtime throws.</returns>
    public static SqlInsertion<TTable> Insert<TTable>(TTable values) where TTable : class
        => throw DefinitionOnly();

    /// <summary>
    /// Creates the failure used when a query definition is accidentally executed.
    /// </summary>
    /// <returns>An exception directing callers to the generated execution method.</returns>
    internal static InvalidOperationException DefinitionOnly()
        => new("SQL definitions run only in the build-time generator. Call the generated method on the outer query class.");
}

/// <summary>
/// Describes filters and projections for one mapped table at compile time.
/// </summary>
/// <typeparam name="TTable">The mapped source table model.</typeparam>
public readonly partial struct SqlSelection<TTable> where TTable : class
{
    /// <summary>
    /// Adds a supported comparison or Boolean combination using mapped columns and bound values.
    /// </summary>
    /// <param name="predicate">The expression translated to SQLite, including value equality for binary columns.</param>
    /// <returns>The compile-time filtered selection.</returns>
    public SqlSelection<TTable> Where(Expression<Func<TTable, bool>> predicate)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins an ascending ordering by one mapped source column before projection.
    /// </summary>
    /// <typeparam name="TColumn">The mapped column's exact type.</typeparam>
    /// <param name="column">A direct mapped property of the current row.</param>
    /// <returns>The compile-time ordered selection; repeated initial ordering is rejected.</returns>
    public SqlSelection<TTable> OrderBy<TColumn>(Expression<Func<TTable, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Begins a descending ordering by one mapped source column before projection.
    /// </summary>
    /// <typeparam name="TColumn">The mapped column's exact type.</typeparam>
    /// <param name="column">A direct mapped property of the current row.</param>
    /// <returns>The compile-time ordered selection; repeated initial ordering is rejected.</returns>
    public SqlSelection<TTable> OrderByDescending<TColumn>(Expression<Func<TTable, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends an ascending tie-breaker to a preceding initial ordering.
    /// </summary>
    /// <typeparam name="TColumn">The mapped column's exact type.</typeparam>
    /// <param name="column">A direct mapped property of the current row.</param>
    /// <returns>The compile-time selection ordered by all authored columns in sequence.</returns>
    public SqlSelection<TTable> ThenBy<TColumn>(Expression<Func<TTable, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Appends a descending tie-breaker to a preceding initial ordering.
    /// </summary>
    /// <typeparam name="TColumn">The mapped column's exact type.</typeparam>
    /// <param name="column">A direct mapped property of the current row.</param>
    /// <returns>The compile-time selection ordered by all authored columns in sequence.</returns>
    public SqlSelection<TTable> ThenByDescending<TColumn>(Expression<Func<TTable, TColumn>> column)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Deletes rows matching an explicit filter and returns the affected-row count.
    /// </summary>
    /// <returns>A definition for a filtered deletion; an unfiltered deletion is rejected at build time.</returns>
    public SqlQuery<int> Delete()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Selects mapped columns into explicitly initialized result properties.
    /// </summary>
    /// <typeparam name="TResult">The reference-type result model.</typeparam>
    /// <param name="projection">An object initializer assigning mapped source columns to result properties.</param>
    /// <returns>The compile-time result projection.</returns>
    public SqlProjection<TResult> Select<TResult>(Expression<Func<TTable, TResult>> projection) where TResult : class
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Specifies how a generated selection returns its typed results.
/// </summary>
/// <typeparam name="TResult">The reference-type result model.</typeparam>
public readonly partial struct SqlProjection<TResult> where TResult : class
{
    /// <summary>
    /// Returns the first matching row, or null when no row matches.
    /// </summary>
    /// <returns>A definition for a nullable generated result.</returns>
    public SqlQuery<TResult?> FirstOrDefault()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Streams matching rows while the generated enumerator owns its command and reader.
    /// </summary>
    /// <returns>A definition for a lazily enumerated result whose enumerator must be disposed.</returns>
    public SqlQuery<IEnumerable<TResult>> AsEnumerable()
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Describes a parameterized insertion and optional SQLite conflict handling.
/// </summary>
/// <typeparam name="TTable">The mapped insertion target.</typeparam>
public readonly struct SqlInsertion<TTable> where TTable : class
{
    /// <summary>
    /// Chooses native duplicate suppression for any unique constraint on the inserted row.
    /// </summary>
    /// <remarks>
    /// Only DoNothing followed by ExecuteNonQuery is supported without a column target.
    /// Check, not-null, foreign-key and trigger errors retain their native behavior.
    /// </remarks>
    /// <returns>The compile-time conflict declaration without a column target.</returns>
    public SqlConflict<TTable> OnConflict()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Chooses the unique columns that identify an existing row during an upsert.
    /// </summary>
    /// <typeparam name="TColumns">The single column or anonymous column-list shape.</typeparam>
    /// <param name="columns">Mapped columns belonging to an existing unique constraint.</param>
    /// <returns>The compile-time conflict target.</returns>
    public SqlConflict<TTable> OnConflict<TColumns>(Expression<Func<TTable, TColumns>> columns)
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Executes the generated insertion or upsert and returns its affected-row count.
    /// </summary>
    /// <returns>A definition for the provider's affected-row count.</returns>
    public SqlQuery<int> ExecuteNonQuery()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Inserts into a rowid table and reads the same connection's last inserted row identity.
    /// </summary>
    /// <returns>A definition for the exact signed 64-bit SQLite row identity.</returns>
    public SqlQuery<long> LastInsertId()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Inserts or retains a row and reads its identity using the declared conflict key on the same connection.
    /// </summary>
    /// <param name="identity">The mapped non-nullable Int64 identity column.</param>
    /// <returns>A definition for the matching row identity, including an existing conflicting row.</returns>
    public SqlQuery<long> ResolveId(Expression<Func<TTable, long>> identity)
        => throw Sql.DefinitionOnly();
}

/// <summary>
/// Chooses native duplicate suppression or an explicit excluded-value conflict update.
/// </summary>
/// <typeparam name="TTable">The mapped upsert target.</typeparam>
public readonly partial struct SqlConflict<TTable> where TTable : class
{
    /// <summary>
    /// Retains an existing row when the declared unique-conflict policy matches.
    /// </summary>
    /// <returns>The compile-time insertion with native duplicate suppression under the declared target policy.</returns>
    public SqlInsertion<TTable> DoNothing()
        => throw Sql.DefinitionOnly();

    /// <summary>
    /// Copies only the selected columns from SQLite's excluded insertion row.
    /// </summary>
    /// <typeparam name="TColumns">The single column or anonymous column-list shape.</typeparam>
    /// <param name="columns">Inserted mapped columns whose excluded values replace the stored values.</param>
    /// <returns>The compile-time insertion with its conflict update attached.</returns>
    public SqlInsertion<TTable> UpdateFromInserted<TColumns>(Expression<Func<TTable, TColumns>> columns)
        => throw Sql.DefinitionOnly();
}
