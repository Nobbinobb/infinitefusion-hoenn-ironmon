using Ironmon.Sqlite.Definitions;
using Microsoft.Data.Sqlite;

namespace MaintenanceFixture;

/// <summary>
/// Requests a row-count intrinsic with an invalid integer identifier parameter.
/// </summary>
internal static partial class InvalidMaintenanceSignature
{
    /// <summary>
    /// Declares an intentionally incompatible metadata contract.
    /// </summary>
    /// <param name="connection">The caller-owned connection.</param>
    /// <param name="transaction">The caller-owned transaction, or null.</param>
    /// <param name="table">The incompatible integer parameter.</param>
    /// <returns>The requested native row count.</returns>
    [SqliteMaintenance(SqliteMaintenanceOperation.CountTableRows)]
    internal static partial long Run(SqliteConnection connection, SqliteTransaction? transaction, long table);
}
