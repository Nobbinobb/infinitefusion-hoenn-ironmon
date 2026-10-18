using Ironmon.Sqlite.Querying;

namespace GeneratorFixture;

/// <summary>
/// Contains a well-typed C# query whose string operation has not been added to the SQL compiler.
/// </summary>
[SqlQueries]
internal static partial class UnsupportedQueries
{
    /// <summary>
    /// Supplies an unsupported predicate that must fail generation instead of falling back at runtime.
    /// </summary>
    private static class Definitions
    {
        /// <summary>
        /// Uses an ordinary string method outside this prototype's supported predicate syntax.
        /// </summary>
        /// <param name="prefix">The search prefix.</param>
        /// <returns>The requested first matching result definition.</returns>
        public static SqlQuery<Result?> Read(string prefix)
        {
            return Sql.From<Row>().Where(row => row.Name.StartsWith(prefix))
                .Select(row => new Result { Name = row.Name }).FirstOrDefault();
        }
    }
}

/// <summary>
/// Provides a valid table mapping so the test isolates the unsupported query operation.
/// </summary>
[SqlTable(_table)]
internal sealed class Row
{
    private const string _table = "fixture_values";
    private const string _name = "fixture_name";

    /// <summary>
    /// Gets or initializes the mapped text column.
    /// </summary>
    [SqlColumn(_name)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Provides a valid selectable result model.
/// </summary>
internal sealed class Result
{
    /// <summary>
    /// Gets or initializes the selected text.
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
