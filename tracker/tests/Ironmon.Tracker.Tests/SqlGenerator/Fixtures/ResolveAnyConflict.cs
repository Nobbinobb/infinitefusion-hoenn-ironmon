using Ironmon.Sqlite.Querying;

namespace GeneratorFixture;

/// <summary>
/// Defines a well-typed identity operation whose incomplete or nullable conflict contract must be rejected.
/// </summary>
[SqlQueries]
internal static partial class ResolveAnyConflictQueries
{
    /// <summary>
    /// Declares the unsupported identity operation for compile-time validation.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Attempts identity resolution without a complete non-nullable conflict key.
        /// </summary>
        /// <param name="key">The proposed row key.</param>
        /// <returns>The invalid compound identity definition.</returns>
        public static SqlQuery<long> Resolve(string key)
        {
            return Sql.Insert(new Row { Key = key }).OnConflict().DoNothing().ResolveId(row => row.Id);
        }
    }
}

/// <summary>
/// Maps a physical identity and key so validation isolates the unsupported conflict contract.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
    private const string _id = "id";
    private const string _key = "key";

    /// <summary>
    /// Gets or initializes the stored identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the proposed exact key.
    /// </summary>
    [SqlColumn(_key)]
    public string Key { get; init; } = string.Empty;
}
