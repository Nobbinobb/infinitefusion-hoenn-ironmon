namespace Ironmon.Sqlite.Querying;

/// <summary>
/// Generates a typed native SQLite maintenance operation without authored command text.
/// </summary>
/// <remarks>
/// Identifies one provider intrinsic at build time; no runtime operation registry is created.
/// </remarks>
/// <param name="operation">The native SQLite operation implemented by the generated partial method.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqliteMaintenanceAttribute(SqliteMaintenanceOperation operation) : Attribute
{
    /// <summary>
    /// Gets the native operation selected during compilation.
    /// </summary>
    public SqliteMaintenanceOperation Operation { get; } = operation;
}

/// <summary>
/// Compiles a dedicated schema or migration script into a typed partial method.
/// </summary>
/// <remarks>
/// The script must be supplied as an AdditionalFile; its text exists only in generated execution code.
/// </remarks>
/// <param name="path">The project-relative SQL script path supplied to the compiler.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqliteScriptAttribute(string path) : Attribute
{
    /// <summary>
    /// Gets the project-relative dedicated script path.
    /// </summary>
    public string Path { get; } = path;
}

/// <summary>
/// Enumerates general native provider operations supported by maintenance generation.
/// </summary>
public enum SqliteMaintenanceOperation
{
    /// <summary>
    /// Reads the active native SQLite version.
    /// </summary>
    SqliteVersion = 0,
    /// <summary>
    /// Reads the database page size in bytes.
    /// </summary>
    ReadPageSize = 1,
    /// <summary>
    /// Reads the allocated database page count.
    /// </summary>
    ReadPageCount = 2,
    /// <summary>
    /// Reads the reusable database page count.
    /// </summary>
    ReadFreeCount = 3,
    /// <summary>
    /// Counts rows in one quoted metadata-owned table.
    /// </summary>
    CountTableRows = 4,
    /// <summary>
    /// Enables WAL, full synchronization, foreign keys and bounded busy handling.
    /// </summary>
    ConfigureDurability = 5,
    /// <summary>
    /// Requests compact 1024-byte database pages.
    /// </summary>
    SetCompactPageSize = 6,
    /// <summary>
    /// Checkpoints and truncates the write-ahead log.
    /// </summary>
    CheckpointTruncate = 7,
    /// <summary>
    /// Reads the current journal mode.
    /// </summary>
    ReadJournalMode = 8,
    /// <summary>
    /// Reads the synchronization policy code.
    /// </summary>
    ReadSynchronous = 9,
    /// <summary>
    /// Reads whether foreign keys are enforced.
    /// </summary>
    ReadForeignKeys = 10,
    /// <summary>
    /// Disables foreign keys and preserves legacy rename semantics before migration.
    /// </summary>
    BeginMigrationPragmas = 11,
    /// <summary>
    /// Restores ordinary rename semantics and foreign-key enforcement after migration.
    /// </summary>
    RestoreMigrationPragmas = 12,
    /// <summary>
    /// Rebuilds free pages using the requested page size outside transactions.
    /// </summary>
    Vacuum = 13,
    /// <summary>
    /// Selects rollback journaling before page compaction.
    /// </summary>
    UseRollbackJournal = 14,
    /// <summary>
    /// Tests whether a physical table has a named stored or generated column.
    /// </summary>
    HasColumn = 15,
    /// <summary>
    /// Reads nonautomatic historical index and trigger definitions.
    /// </summary>
    ReadMigrationObjects = 16,
    /// <summary>
    /// Lists the physical table names visible in SQLite metadata.
    /// </summary>
    ReadTableNames = 17,
    /// <summary>
    /// Reads declared parent table and child column names for foreign-key references.
    /// </summary>
    ReadForeignKeyReferences = 18,
    /// <summary>
    /// Reads physical column ordinals, names and generated-column flags.
    /// </summary>
    ReadTableColumns = 19,
    /// <summary>
    /// Materializes complete original physical rows before a table rebuild.
    /// </summary>
    ReadTableRows = 20,
    /// <summary>
    /// Drops one metadata-owned trigger using quoted identifier syntax.
    /// </summary>
    DropTrigger = 21,
    /// <summary>
    /// Drops one table using quoted identifier syntax.
    /// </summary>
    DropTable = 22,
    /// <summary>
    /// Renames one replacement table using quoted identifier syntax.
    /// </summary>
    RenameTable = 23,
    /// <summary>
    /// Executes an existing historical schema object's retained definition.
    /// </summary>
    ExecuteSchemaObject = 24,
    /// <summary>
    /// Executes a table, view or index declaration extracted from the canonical schema resource.
    /// </summary>
    ExecuteSchemaDefinition = 25,
    /// <summary>
    /// Creates a temporary replacement from a canonical table declaration.
    /// </summary>
    CreateRebuiltTable = 26,
    /// <summary>
    /// Inserts exact stored fields while excluding generated columns in a replacement table.
    /// </summary>
    InsertRebuiltRow = 27,
    /// <summary>
    /// Counts rows referencing an immutable identity through a metadata-owned column.
    /// </summary>
    CountColumnReferences = 28,
    /// <summary>
    /// Rejects the first foreign-key violation without modifying the owning transaction.
    /// </summary>
    AssertForeignKeyIntegrity = 29
}

/// <summary>
/// Retains a historical SQLite index or trigger definition during an atomic rebuild.
/// </summary>
/// <remarks>
/// Creates an immutable metadata record with the complete original declaration.
/// </remarks>
/// <param name="type">The SQLite schema-object type.</param>
/// <param name="name">The exact schema-object identifier.</param>
/// <param name="sql">The retained declaration read from SQLite metadata.</param>
public sealed class SqliteSchemaObject(string type, string name, string sql)
{
    /// <summary>
    /// Gets the SQLite schema-object type.
    /// </summary>
    public string Type { get; } = type;
    /// <summary>
    /// Gets the exact schema-object identifier.
    /// </summary>
    public string Name { get; } = name;
    /// <summary>
    /// Gets the declaration read from SQLite metadata.
    /// </summary>
    public string Sql { get; } = sql;
}

/// <summary>
/// Describes one physical column while preserving its original row-array ordinal.
/// </summary>
/// <remarks>
/// Creates an immutable column descriptor from SQLite table metadata.
/// </remarks>
/// <param name="ordinal">The zero-based physical column ordinal.</param>
/// <param name="name">The exact column identifier.</param>
/// <param name="hidden">The SQLite hidden or generated-column flag; zero denotes a stored column.</param>
public sealed class SqliteColumnInfo(long ordinal, string name, long hidden)
{
    /// <summary>
    /// Gets the zero-based physical column ordinal.
    /// </summary>
    public long Ordinal { get; } = ordinal;
    /// <summary>
    /// Gets the exact column identifier.
    /// </summary>
    public string Name { get; } = name;
    /// <summary>
    /// Gets the SQLite hidden or generated-column flag.
    /// </summary>
    public long Hidden { get; } = hidden;
}

/// <summary>
/// Describes one declared foreign-key column without interpreting it as a query identifier.
/// </summary>
/// <remarks>
/// Creates an immutable parent-table and child-column metadata pair.
/// </remarks>
/// <param name="table">The referenced parent table identifier.</param>
/// <param name="column">The referencing child column identifier.</param>
public sealed class SqliteForeignKeyReference(string table, string column)
{
    /// <summary>
    /// Gets the referenced parent table identifier.
    /// </summary>
    public string Table { get; } = table;
    /// <summary>
    /// Gets the referencing child column identifier.
    /// </summary>
    public string Column { get; } = column;
}

/// <summary>
/// Quotes runtime identifiers obtained from SQLite metadata without changing their spelling.
/// </summary>
public static class SqliteIdentifier
{
    private const string _quote = "\"";
    private const string _escapedQuote = "\"\"";

    /// <summary>
    /// Quotes a single complete table, trigger or column name safely.
    /// </summary>
    /// <param name="name">The exact identifier; embedded quote characters are escaped.</param>
    /// <returns>A quoted SQLite identifier that cannot introduce additional syntax.</returns>
    public static string Quote(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('\0'))
            throw new ArgumentException("SQLite identifiers cannot contain a null character.", nameof(name));

        return _quote + name.Replace(_quote, _escapedQuote, StringComparison.Ordinal) + _quote;
    }
}
