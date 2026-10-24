using Ironmon.Sqlite.Definitions;
using Microsoft.Data.Sqlite;

namespace MaintenanceFixture;

/// <summary>
/// Requests an operation outside the supported native maintenance vocabulary.
/// </summary>
internal static partial class UnknownMaintenanceOperation
{
    /// <summary>
    /// Declares an intentionally unknown native operation.
    /// </summary>
    /// <param name="connection">The caller-owned connection.</param>
    /// <param name="transaction">The caller-owned transaction, or null.</param>
    [SqliteMaintenance((SqliteMaintenanceOperation)255)]
    internal static partial void Run(SqliteConnection connection, SqliteTransaction? transaction);
}
