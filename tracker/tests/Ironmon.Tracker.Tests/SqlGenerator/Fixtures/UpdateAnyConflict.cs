using Ironmon.Sqlite.Definitions;

namespace GeneratorFixture;

/// <summary>
/// Defines a well-typed conflict update whose missing unique target must be rejected.
/// </summary>
[SqlQueries]
internal static partial class UpdateAnyConflictQueries
{
    /// <summary>
    /// Declares the unsupported update operation for compile-time validation.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Attempts a conflict update without a specific unique target.
        /// </summary>
        /// <param name="key">The proposed row key.</param>
        /// <returns>The invalid conflict update definition.</returns>
        public static SqlQuery<int> Update(string key)
        {
            return Sql.Insert(new Row { Key = key }).OnConflict().UpdateFromInserted(row => row.Key).ExecuteNonQuery();
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
