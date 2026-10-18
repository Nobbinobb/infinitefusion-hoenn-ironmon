using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Ironmon.Sqlite.QueryGenerator;

/// <summary>
/// Retains the validated build-time operation and the exact typed provider bindings to emit.
/// </summary>
/// <remarks>
/// Creates a plan for one semantically checked query definition.
/// </remarks>
/// <param name="method">The definition whose parameters become the generated method's arguments.</param>
/// <param name="result">The exact generated method return type.</param>
/// <param name="mode">The supported terminal operation.</param>
internal sealed class QueryPlan(IMethodSymbol method, ITypeSymbol result, QueryMode mode)
{
    /// <summary>
    /// Gets the source definition and its original typed parameters.
    /// </summary>
    internal IMethodSymbol Method { get; } = method;

    /// <summary>
    /// Gets the exact execution return type, including nullability.
    /// </summary>
    internal ITypeSymbol Result { get; } = result;

    /// <summary>
    /// Gets the terminal operation that controls generated execution and resource lifetime.
    /// </summary>
    internal QueryMode Mode { get; } = mode;

    /// <summary>
    /// Gets or sets the finalized parameterized SQLite statement text.
    /// </summary>
    internal string Sql { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fully qualified result-row type for selections.
    /// </summary>
    internal string RowType { get; set; } = string.Empty;

    /// <summary>
    /// Gets bindings in generated parameter order; storage is null only for native ANY values.
    /// </summary>
    internal List<(string Expression, string? Storage)> Parameters { get; } = [];

    /// <summary>
    /// Gets result property assignments and their exact ordinal-based provider readers.
    /// </summary>
    internal List<(string Property, string Reader)> Projection { get; } = [];
}

/// <summary>
/// Enumerates the supported provider execution shapes without runtime query translation.
/// </summary>
internal enum QueryMode
{
    FirstOrDefault = 0,
    Enumerable = 1,
    NonQuery = 2,
    LastInsertId = 3,
    ResolvedId = 4,
    ScalarInt64 = 5
}
