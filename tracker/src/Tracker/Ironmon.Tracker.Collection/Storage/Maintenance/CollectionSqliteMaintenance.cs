using Ironmon.Sqlite.Querying;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Exposes generated native SQLite maintenance operations with caller-owned transactions.
/// </summary>
internal static partial class CollectionSqliteMaintenance
{
    /// <summary>
    /// Reads the native SQLite library version.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.SqliteVersion)]
    internal static partial string SqliteVersion(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Reads the allocated page size in bytes.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadPageSize)]
    internal static partial long ReadPageSize(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Reads the allocated database page count.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadPageCount)]
    internal static partial long ReadPageCount(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Reads the reusable database page count.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadFreeCount)]
    internal static partial long ReadFreeCount(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Counts rows in one quoted physical table.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The complete metadata-owned table identifier.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.CountTableRows)]
    internal static partial long CountTableRows(SqliteConnection connection, SqliteTransaction? transaction, string table);

    /// <summary>
    /// Disables foreign keys and enables legacy rename semantics before migration.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.BeginMigrationPragmas)]
    internal static partial void BeginMigrationPragmas(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Restores normal rename semantics and foreign-key enforcement after migration.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.RestoreMigrationPragmas)]
    internal static partial void RestoreMigrationPragmas(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Rebuilds reusable pages outside an active transaction.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.Vacuum)]
    internal static partial void Vacuum(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Enables rollback journaling before changing the database page size.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.UseRollbackJournal)]
    internal static partial void UseRollbackJournal(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Tests whether a table exposes the named physical column.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The physical table whose columns are inspected.</param>
    /// <param name="column">The exact physical column name.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.HasColumn)]
    internal static partial bool HasColumn(SqliteConnection connection, SqliteTransaction? transaction, string table, string column);

    /// <summary>
    /// Reads complete nonautomatic historical index and trigger declarations.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadMigrationObjects)]
    internal static partial IReadOnlyList<SqliteSchemaObject> ReadMigrationObjects(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Reads complete physical table identifiers from SQLite metadata.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadTableNames)]
    internal static partial IReadOnlyList<string> ReadTableNames(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Reads parent-table and child-column names for declared foreign keys.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The child table whose references are inspected.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadForeignKeyReferences)]
    internal static partial IReadOnlyList<SqliteForeignKeyReference> ReadForeignKeyReferences(SqliteConnection connection, SqliteTransaction? transaction, string table);

    /// <summary>
    /// Reads exact physical column ordinals, identifiers and generated-column flags.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The physical table whose stored and generated columns are inspected.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadTableColumns)]
    internal static partial IReadOnlyList<SqliteColumnInfo> ReadTableColumns(SqliteConnection connection, SqliteTransaction? transaction, string table);

    /// <summary>
    /// Materializes complete original physical rows before any replacement writes.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The original table whose complete rows must survive replacement.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadTableRows)]
    internal static partial IReadOnlyList<object[]> ReadTableRows(SqliteConnection connection, SqliteTransaction? transaction, string table);

    /// <summary>
    /// Drops one safely quoted historical trigger during migration.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="trigger">The exact trigger identifier read from SQLite metadata.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.DropTrigger)]
    internal static partial void DropTrigger(SqliteConnection connection, SqliteTransaction? transaction, string trigger);

    /// <summary>
    /// Drops one safely quoted obsolete physical table.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The exact obsolete physical table identifier.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.DropTable)]
    internal static partial void DropTable(SqliteConnection connection, SqliteTransaction? transaction, string table);

    /// <summary>
    /// Renames a replacement table using exact quoted identifiers.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The safely quoted replacement table identifier.</param>
    /// <param name="replacement">The original physical table identifier to restore.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.RenameTable)]
    internal static partial void RenameTable(SqliteConnection connection, SqliteTransaction? transaction, string table, string replacement);

    /// <summary>
    /// Restores one complete retained historical schema declaration.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="schema">The retained historical index or trigger declaration read from SQLite metadata.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.ExecuteSchemaObject)]
    internal static partial void ExecuteSchemaObject(SqliteConnection connection, SqliteTransaction? transaction, SqliteSchemaObject schema);

    /// <summary>
    /// Executes one canonical table, view or index declaration.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="definition">The complete declaration extracted from the canonical schema resource.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.ExecuteSchemaDefinition)]
    internal static partial void ExecuteSchemaDefinition(SqliteConnection connection, SqliteTransaction? transaction, string definition);

    /// <summary>
    /// Creates a replacement physical table from its canonical schema declaration.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="definition">The complete canonical target table declaration.</param>
    /// <param name="table">The original canonical table name within the declaration.</param>
    /// <param name="replacement">The temporary replacement name.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.CreateRebuiltTable)]
    internal static partial void CreateRebuiltTable(SqliteConnection connection, SqliteTransaction? transaction, string definition, string table, string replacement);

    /// <summary>
    /// Inserts all requested stored columns while leaving generated values to SQLite.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The temporary physical table identifier.</param>
    /// <param name="columns">Stored physical columns in their original ordinal order.</param>
    /// <param name="values">The converted complete row, including slots for generated columns.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.InsertRebuiltRow)]
    internal static partial void InsertRebuiltRow(SqliteConnection connection, SqliteTransaction? transaction, string table, IReadOnlyList<SqliteColumnInfo> columns, object[] values);

    /// <summary>
    /// Counts exact references using metadata-owned quoted table and column identifiers.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="table">The referencing physical table identifier.</param>
    /// <param name="column">The referencing physical column identifier.</param>
    /// <param name="value">The full immutable identity whose references are counted.</param>
    /// <returns>The exact native metadata result, materialized before returning.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.CountColumnReferences)]
    internal static partial long CountColumnReferences(SqliteConnection connection, SqliteTransaction? transaction, string table, string column, long value);

    /// <summary>
    /// Rejects the first foreign-key violation within the owning transaction.
    /// </summary>
    /// <param name="connection">The externally owned SQLite connection.</param>
    /// <param name="transaction">The current transaction, or null outside a transaction.</param>
    /// <param name="failureMessage">The migration-specific error reported if any retained relationship is invalid.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.AssertForeignKeyIntegrity)]
    internal static partial void AssertForeignKeyIntegrity(SqliteConnection connection, SqliteTransaction? transaction, string failureMessage);

}
