using Ironmon.Sqlite.Definitions;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Collection.Storage.Maintenance;

/// <summary>
/// Exposes dedicated fixed migration scripts through generated typed methods.
/// </summary>
internal static partial class CollectionSqliteMaintenance
{
    private const string _LegacyDropIdentityView = "Resources/LegacyDropIdentityView.sql";

    /// <summary>
    /// Executes the fixed historical migration declaration in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing caller-owned SQLite connection.</param>
    /// <param name="transaction">The migration transaction, or null outside one.</param>
    [SqliteScript(_LegacyDropIdentityView)]
    internal static partial void LegacyDropIdentityView(SqliteConnection connection, SqliteTransaction? transaction);

    private const string _LegacyRestoreIdentityView = "Resources/LegacyRestoreIdentityView.sql";

    /// <summary>
    /// Executes the fixed historical migration declaration in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing caller-owned SQLite connection.</param>
    /// <param name="transaction">The migration transaction, or null outside one.</param>
    [SqliteScript(_LegacyRestoreIdentityView)]
    internal static partial void LegacyRestoreIdentityView(SqliteConnection connection, SqliteTransaction? transaction);

    private const string _CompactDimensionMembersView = "Resources/CompactDimensionMembersView.sql";

    /// <summary>
    /// Executes the fixed historical migration declaration in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing caller-owned SQLite connection.</param>
    /// <param name="transaction">The migration transaction, or null outside one.</param>
    [SqliteScript(_CompactDimensionMembersView)]
    internal static partial void CompactDimensionMembersView(SqliteConnection connection, SqliteTransaction? transaction);

    private const string _CompactTupleMembers = "Resources/CompactTupleMembers.sql";

    /// <summary>
    /// Executes the fixed historical migration declaration in the caller-owned transaction.
    /// </summary>
    /// <param name="connection">The existing caller-owned SQLite connection.</param>
    /// <param name="transaction">The migration transaction, or null outside one.</param>
    [SqliteScript(_CompactTupleMembers)]
    internal static partial void CompactTupleMembers(SqliteConnection connection, SqliteTransaction? transaction);

}
