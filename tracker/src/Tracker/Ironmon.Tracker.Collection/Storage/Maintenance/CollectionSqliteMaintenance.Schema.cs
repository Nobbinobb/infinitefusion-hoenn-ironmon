using Ironmon.Sqlite.Querying;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Provides generated schema scripts and fixed SQLite durability operations.
/// </summary>
internal static partial class CollectionSqliteMaintenance
{
    private const string _CreateCoreSchema = "Resources/Schema.sql";

    /// <summary>
    /// Executes the unchanged dedicated schema script in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteScript(_CreateCoreSchema)]
    internal static partial void CreateCoreSchema(SqliteConnection connection, SqliteTransaction? transaction);

    private const string _CreateSupplementalSchema = "Resources/SupplementalSchema.sql";

    /// <summary>
    /// Executes the unchanged dedicated schema script in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteScript(_CreateSupplementalSchema)]
    internal static partial void CreateSupplementalSchema(SqliteConnection connection, SqliteTransaction? transaction);

    private const string _CreateResourceSchema = "Resources/ResourceSchema.sql";

    /// <summary>
    /// Executes the unchanged dedicated schema script in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteScript(_CreateResourceSchema)]
    internal static partial void CreateResourceSchema(SqliteConnection connection, SqliteTransaction? transaction);

    private const string _CreateValueSchema = "Resources/ValueSchema.sql";

    /// <summary>
    /// Executes the unchanged dedicated schema script in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteScript(_CreateValueSchema)]
    internal static partial void CreateValueSchema(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Applies or reads the fixed SQLite durability operation without runtime query translation.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.ConfigureDurability)]
    internal static partial void ConfigureDurability(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Applies or reads the fixed SQLite durability operation without runtime query translation.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.SetCompactPageSize)]
    internal static partial void SetCompactPageSize(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Applies or reads the fixed SQLite durability operation without runtime query translation.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    [SqliteMaintenance(SqliteMaintenanceOperation.CheckpointTruncate)]
    internal static partial void CheckpointTruncate(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Applies or reads the fixed SQLite durability operation without runtime query translation.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    /// <returns>The exact SQLite setting value.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadJournalMode)]
    internal static partial string ReadJournalMode(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Applies or reads the fixed SQLite durability operation without runtime query translation.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    /// <returns>The exact SQLite setting value.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadSynchronous)]
    internal static partial long ReadSynchronous(SqliteConnection connection, SqliteTransaction? transaction);

    /// <summary>
    /// Applies or reads the fixed SQLite durability operation without runtime query translation.
    /// </summary>
    /// <param name="connection">The existing SQLite connection.</param>
    /// <param name="transaction">The existing transaction, or null outside one.</param>
    /// <returns>The exact SQLite setting value.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.ReadForeignKeys)]
    internal static partial long ReadForeignKeys(SqliteConnection connection, SqliteTransaction? transaction);
}
