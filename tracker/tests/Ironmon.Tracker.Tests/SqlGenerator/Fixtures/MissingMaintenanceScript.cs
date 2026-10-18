using Ironmon.Sqlite.Querying;
using Microsoft.Data.Sqlite;

namespace MaintenanceFixture;

/// <summary>
/// Requests a dedicated script that was not supplied to the compiler.
/// </summary>
internal static partial class MissingMaintenanceScript
{
    private const string _missing = "Resources/Unavailable.sql";

    /// <summary>
    /// Declares the intentionally unavailable dedicated script.
    /// </summary>
    /// <param name="connection">The caller-owned connection.</param>
    /// <param name="transaction">The caller-owned transaction, or null.</param>
    [SqliteScript(_missing)]
    internal static partial void Run(SqliteConnection connection, SqliteTransaction? transaction);
}
