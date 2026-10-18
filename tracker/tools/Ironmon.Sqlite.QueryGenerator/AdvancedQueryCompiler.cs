using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ironmon.Sqlite.QueryGenerator;

/// <summary>
/// Translates explicit relational composition while retaining the established simple-query emitter path.
/// </summary>
internal sealed class AdvancedQueryCompiler
{
    private const string _namespace = "Ironmon.Sqlite.Querying";
    private const string _intrinsics = _namespace + ".Sql";
    private const string _tableAttribute = _namespace + ".SqlTableAttribute";
    private const string _columnAttribute = _namespace + ".SqlColumnAttribute";
    private const string _from = "From";
    private const string _where = "Where";
    private const string _select = "Select";
    private const string _insert = "Insert";
    private const string _update = "Update";
    private const string _delete = "Delete";
    private const string _join = "Join";
    private const string _distinct = "Distinct";
    private const string _allRows = "AllRows";
    private const string _count = "Count";
    private const string _exists = "Exists";
    private const string _substring = "Substring";
    private const string _like = "Like";
    private const string _sum = "Sum";
    private const string _binaryGreaterThanOrEqual = "BinaryGreaterThanOrEqual";
    private const string _union = "Union";
    private const string _unionAll = "UnionAll";
    private const string _orderBy = "OrderBy";
    private const string _orderByDescending = "OrderByDescending";
    private const string _thenBy = "ThenBy";
    private const string _thenByDescending = "ThenByDescending";
    private const string _first = "FirstOrDefault";
    private const string _enumerable = "AsEnumerable";
    private const string _execute = "ExecuteNonQuery";
    private const string _identity = "LastInsertId";
    private const string _resolve = "ResolveId";
    private const string _conflict = "OnConflict";
    private const string _nothing = "DoNothing";
    private const string _excluded = "excluded";
    private const string _aliasPrefix = "t";
    private const string _parameterPrefix = "@p";
    private const string _selectSql = "SELECT ";
    private const string _distinctSql = "DISTINCT ";
    private const string _fromSql = " FROM ";
    private const string _asSql = " AS ";
    private const string _whereSql = " WHERE ";
    private const string _joinSql = " JOIN ";
    private const string _onSql = " ON ";
    private const string _orderSql = " ORDER BY ";
    private const string _ascendingSql = " ASC";
    private const string _descendingSql = " DESC";
    private const string _unionSql = " UNION ";
    private const string _unionAllSql = " UNION ALL ";
    private const string _limitSql = " LIMIT 1";
    private const string _countSql = "COUNT(*)";
    private const string _existsSql = "EXISTS(";
    private const string _substringSql = "substr(";
    private const string _sumSql = "SUM(";
    private const string _coalesceSql = "coalesce(";
    private const string _updateSql = "UPDATE ";
    private const string _setSql = " SET ";
    private const string _deleteSql = "DELETE FROM ";
    private const string _insertSql = "INSERT INTO ";
    private const string _valuesSql = ") VALUES(";
    private const string _conflictSql = " ON CONFLICT(";
    private const string _conflictUpdateSql = ") DO UPDATE SET ";
    private const string _nothingSql = ") DO NOTHING";
    private const string _identitySql = "; SELECT last_insert_rowid()";
    private const string _resolveSql = "; SELECT ";
    private const string _andSql = " AND ";
    private const string _orSql = " OR ";
    private const string _equalSql = " = ";
    private const string _notEqualSql = " <> ";
    private const string _isSql = " IS ";
    private const string _isNotSql = " IS NOT ";
    private const string _greaterSql = " > ";
    private const string _greaterEqualSql = " >= ";
    private const string _lessSql = " < ";
    private const string _lessEqualSql = " <= ";
    private const string _plusSql = " + ";
    private const string _minusSql = " - ";
    private const string _multiplySql = " * ";
    private const string _divideSql = " / ";
    private const string _moduloSql = " % ";
    private const string _notSql = "NOT ";
    private const string _likeSql = " LIKE ";
    private const string _separator = ",";
    private const string _oneSql = "1";
    private const string _integerStorage = "Integer";
    private const string _realStorage = "Real";
    private readonly IMethodSymbol _method;
    private readonly SemanticModel _semantic;
    private readonly QueryPlan _plan;
    private readonly Dictionary<string, int> _parameters = new(StringComparer.Ordinal);
    private int _nextAlias;
    private bool _aggregateProjection;

    /// <summary>
    /// Creates a compiler sharing exact binding and reader rules with the established query path.
    /// </summary>
    /// <param name="method">The authored definition method.</param>
    /// <param name="semantic">The complete semantic model for its syntax tree.</param>
    /// <param name="plan">The generated provider plan receiving SQL and bindings.</param>
    private AdvancedQueryCompiler(IMethodSymbol method, SemanticModel semantic, QueryPlan plan)
    {
        _method = method;
        _semantic = semantic;
        _plan = plan;
    }

    /// <summary>
    /// Routes only newly composed syntax away from the unchanged established compilation path.
    /// </summary>
    /// <param name="chain">The checked outer fluent invocation chain.</param>
    /// <param name="semantic">The source semantic model.</param>
    /// <returns>Whether the operation requires relational composition or scalar projection expressions.</returns>
    internal static bool RequiresAdvanced(List<InvocationExpressionSyntax> chain, SemanticModel semantic)
    {
        if (chain.Any(call => Name(call) is _join or _distinct or _allRows or _update or _union or _unionAll or _count))
            return true;

        if (chain.SelectMany(call => call.ArgumentList.DescendantNodes().OfType<InvocationExpressionSyntax>()).Any(call => semantic.GetSymbolInfo(call).Symbol is IMethodSymbol symbol && symbol.ContainingType.ToDisplayString() == _intrinsics))
            return true;

        return chain.Where(call => Name(call) == _select).SelectMany(call => call.ArgumentList.DescendantNodes().OfType<AssignmentExpressionSyntax>()).Any(assignment => assignment.Right is not MemberAccessExpressionSyntax);
    }

    /// <summary>
    /// Compiles one complete advanced definition without runtime translation or fallback.
    /// </summary>
    /// <param name="method">The checked source definition.</param>
    /// <param name="result">The exact generated return type.</param>
    /// <param name="semantic">The complete source semantic model.</param>
    /// <param name="chain">The complete fluent query chain.</param>
    /// <returns>The validated generated provider operation.</returns>
    internal static QueryPlan Compile(IMethodSymbol method, ITypeSymbol result, SemanticModel semantic, List<InvocationExpressionSyntax> chain)
    {
        QueryMode mode = Name(chain.Last()) switch
        {
            _first => QueryMode.FirstOrDefault,
            _enumerable => QueryMode.Enumerable,
            _execute or _delete => QueryMode.NonQuery,
            _identity => QueryMode.LastInsertId,
            _resolve => QueryMode.ResolvedId,
            _count => QueryMode.ScalarInt64,
            _ => throw new InvalidOperationException("The composed query terminal is not supported.")
        };
        QueryPlan plan = new(method, result, mode);
        AdvancedQueryCompiler compiler = new(method, semantic, plan);
        if (Name(chain[0]) == _insert)
        {
            compiler.Insert(chain);
        }
        else if (Name(chain[0]) == _from && mode is QueryMode.FirstOrDefault or QueryMode.Enumerable)
        {
            plan.Sql = compiler.Projection(chain.Take(chain.Count - 1).ToList(), new(SymbolEqualityComparer.Default), true);
            if (mode == QueryMode.FirstOrDefault)
                plan.Sql += _limitSql;
        }
        else if (Name(chain[0]) == _from && mode == QueryMode.ScalarInt64)
        {
            if (result.SpecialType != SpecialType.System_Int64)
                throw new InvalidOperationException("Count requires an exact Int64 generated result.");

            var (Sql, Rows, Ordering, Distinct) = compiler.Source(chain.Take(chain.Count - 1).ToList(), new(SymbolEqualityComparer.Default));
            if (Distinct || Ordering.Count != 0)
                throw new InvalidOperationException("Count supports filtered and joined sources without Distinct or ordering.");

            plan.Sql = _selectSql + _countSql + Sql;
        }
        else if (Name(chain[0]) == _from && mode == QueryMode.NonQuery)
        {
            compiler.Mutation(chain);
        }
        else
        {
            throw new InvalidOperationException("The composed source and terminal do not form a supported operation.");
        }

        if (method.Parameters.Any(parameter => !compiler._parameters.ContainsKey(parameter.Name)))
            throw new InvalidOperationException("Every query argument must be bound by the query; unused arguments are rejected.");

        return plan;
    }

    /// <summary>
    /// Compiles a projection and compatible left-associated compound projections.
    /// </summary>
    /// <param name="chain">The source chain ending in Select or a compound operation.</param>
    /// <param name="outer">Available correlated outer row bindings.</param>
    /// <param name="primary">Whether this projection establishes the generated reader shape.</param>
    /// <returns>The complete parameterized SELECT or compound SELECT.</returns>
    private string Projection(List<InvocationExpressionSyntax> chain, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> outer, bool primary)
    {
        int compound = chain.FindIndex(call => Name(call) is _union or _unionAll);
        List<InvocationExpressionSyntax> first = compound < 0 ? chain : chain.Take(compound).ToList();
        if (first.Count < 2 || Name(first.Last()) != _select)
            throw new InvalidOperationException("A composed selection requires one explicit Select before its terminal or UNION.");

        var (Sql, Rows, Ordering, Distinct) = Source(first.Take(first.Count - 1).ToList(), outer);
        var (Body, Bindings) = Lambda(first.Last(), Rows, outer);
        ObjectCreationExpressionSyntax creation = Creation(Body);
        ITypeSymbol rowType = _semantic.GetTypeInfo(creation).Type ?? throw new InvalidOperationException("The result row type could not be resolved.");
        List<string> columns = [];
        List<string> properties = [];
        foreach (ExpressionSyntax initializer in creation.Initializer!.Expressions)
        {
            if (initializer is not AssignmentExpressionSyntax assignment || assignment.Left is not IdentifierNameSyntax property || assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
                throw new InvalidOperationException("Projection initializers require simple result-property assignments.");

            properties.Add(property.Identifier.ValueText);
            columns.Add(ScopedExpression(assignment.Right, Bindings, true));
            ITypeSymbol valueType = _semantic.GetTypeInfo(assignment.Right).ConvertedType ?? _semantic.GetTypeInfo(assignment.Right).Type ?? throw new InvalidOperationException("The projected SQL expression requires an exact readable type.");
            if (primary)
                _plan.Projection.Add((property.Identifier.ValueText, SqliteQueryCompiler.Reader(valueType, columns.Count - 1)));
        }

        if (columns.Count == 0 || properties.Distinct(StringComparer.Ordinal).Count() != properties.Count)
            throw new InvalidOperationException("A projection needs a nonempty list of unique result properties.");

        if (primary)
        {
            _plan.RowType = SqliteQueryCompiler.TypeName(rowType);
        }
        else if (_plan.RowType != SqliteQueryCompiler.TypeName(rowType) || !_plan.Projection.Select(value => value.Property).SequenceEqual(properties))
        {
            throw new InvalidOperationException("UNION branches require the same exact result type and property order.");
        }

        string sql = _selectSql + (Distinct ? _distinctSql : string.Empty) + string.Join(_separator, columns) + Sql;
        if (Ordering.Count != 0)
        {
            if (compound >= 0 || !primary)
                throw new InvalidOperationException("UNION branches cannot contain ordering; order the complete compound result in a future supported extension.");

            sql += _orderSql + string.Join(_separator, Ordering);
        }

        if (compound >= 0)
        {
            foreach (InvocationExpressionSyntax call in chain.Skip(compound))
            {
                if (Name(call) is not (_union or _unionAll) || call.ArgumentList.Arguments.Count != 1)
                    throw new InvalidOperationException("Only UNION or UNION ALL can follow a projected compound branch.");

                List<InvocationExpressionSyntax> branch = Chain(call.ArgumentList.Arguments[0].Expression);
                if (branch.Any(operation => Name(operation) is _union or _unionAll))
                    throw new InvalidOperationException("Compose UNION branches left to right instead of nesting a compound right operand.");

                sql += (Name(call) == _union ? _unionSql : _unionAllSql) + Projection(branch, outer, false);
            }
        }

        return sql;
    }

    /// <summary>
    /// Compiles a mapped source, explicit joins, filters and ordered single-column selectors.
    /// </summary>
    /// <param name="chain">The From chain before projection or count.</param>
    /// <param name="outer">Available correlated outer row bindings.</param>
    /// <returns>The FROM/WHERE SQL, complete row sources and optional ordering.</returns>
    private (string Sql, List<(INamedTypeSymbol Table, string Alias)> Rows, List<string> Ordering, bool Distinct) Source(List<InvocationExpressionSyntax> chain, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> outer)
    {
        if (chain.Count == 0 || Name(chain[0]) != _from)
            throw new InvalidOperationException("A composed source must begin with Sql.From<TTable>().");

        var first = NewSource(chain[0]);
        List<(INamedTypeSymbol Table, string Alias)> rows = [first];
        List<string> filters = [];
        List<string> ordering = [];
        bool distinct = false;
        string sql = _fromSql + TableSource(first);
        foreach (InvocationExpressionSyntax call in chain.Skip(1))
        {
            string name = Name(call);
            if (name == _join)
            {
                if (filters.Count != 0 || ordering.Count != 0 || distinct || rows.Count >= 6)
                    throw new InvalidOperationException("Up to six inner sources must be joined before filtering, distinctness or ordering.");

                var joined = NewSource(call);
                rows.Add(joined);
                var (Body, Bindings) = Lambda(call, rows, outer);
                sql += _joinSql + TableSource(joined) + _onSql + Expression(Body, Bindings);
            }
            else if (name == _where)
            {
                if (ordering.Count != 0)
                    throw new InvalidOperationException("Where filters must precede the ordering chain.");

                var (Body, Bindings) = Lambda(call, rows, outer);
                filters.Add(Expression(Body, Bindings));
            }
            else if (name == _distinct)
            {
                if (distinct)
                    throw new InvalidOperationException("Distinct can be declared only once.");

                distinct = true;
            }
            else if (name is _orderBy or _orderByDescending or _thenBy or _thenByDescending)
            {
                bool initial = name is _orderBy or _orderByDescending;
                if (initial && ordering.Count != 0 || !initial && ordering.Count == 0)
                    throw new InvalidOperationException("Ordering requires exactly one OrderBy or OrderByDescending followed by zero or more ThenBy or ThenByDescending operations.");

                var (Body, Bindings) = Lambda(call, rows, outer);
                string column = Column(Body, Bindings);
                ordering.Add(column + (name is _orderByDescending or _thenByDescending ? _descendingSql : _ascendingSql));
            }
            else
            {
                throw new InvalidOperationException("The composed source contains an unsupported operation before projection.");
            }
        }

        if (filters.Count != 0)
            sql += _whereSql + string.Join(_andSql, filters);

        return (sql, rows, ordering, distinct);
    }

    /// <summary>
    /// Compiles explicit filtered or deliberate full-table UPDATE and DELETE operations.
    /// </summary>
    /// <param name="chain">The complete mutation and execution chain.</param>
    private void Mutation(List<InvocationExpressionSyntax> chain)
    {
        bool update = Name(chain.Last()) == _execute && chain.Count >= 3 && Name(chain[chain.Count - 2]) == _update;
        bool delete = Name(chain.Last()) == _delete;
        if (!update && !delete)
            throw new InvalidOperationException("A mapped mutation requires Update(...).ExecuteNonQuery() or Delete().");

        var source = NewSource(chain[0]);
        List<(INamedTypeSymbol Table, string Alias)> rows = [source];
        Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> outer = new(SymbolEqualityComparer.Default);
        List<string> filters = [];
        bool allRows = false;
        foreach (InvocationExpressionSyntax call in chain.Skip(1).Take(chain.Count - (update ? 3 : 2)))
        {
            if (Name(call) == _allRows && !allRows)
            {
                allRows = true;
            }
            else if (Name(call) == _where)
            {
                var (Body, Bindings) = Lambda(call, rows, outer);
                filters.Add(Expression(Body, Bindings));
            }
            else
            {
                throw new InvalidOperationException("Mutations support only explicit Where filters or a single AllRows declaration.");
            }
        }

        if (filters.Count == 0 && !allRows)
            throw new InvalidOperationException("An unfiltered mutation requires explicit AllRows intent.");

        string table = SqliteQueryCompiler.Quote(SqliteQueryCompiler.Mapping(source.Table, _tableAttribute)) + _asSql + SqliteQueryCompiler.Quote(source.Alias);
        if (update)
        {
            var (Body, Bindings) = Lambda(chain[chain.Count - 2], rows, outer);
            var values = Assignments(Body, source.Table, Bindings);
            _plan.Sql = _updateSql + table + _setSql + string.Join(_separator, values.Select(value => value.Column + _equalSql + value.Value));
        }
        else
        {
            _plan.Sql = _deleteSql + table;
        }

        if (filters.Count != 0)
            _plan.Sql += _whereSql + string.Join(_andSql, filters);
    }

    /// <summary>
    /// Compiles insertions carrying explicitly computed excluded/current-row conflict updates.
    /// </summary>
    /// <param name="chain">The complete insertion and native execution chain.</param>
    private void Insert(List<InvocationExpressionSyntax> chain)
    {
        if (chain.Count != 4 || Name(chain[1]) != _conflict || Name(chain[2]) != _update || Name(chain.Last()) is not (_execute or _identity or _resolve))
            throw new InvalidOperationException("Computed insertions require OnConflict(columns), Update((current,incoming) => ...), and a supported native terminal.");

        INamedTypeSymbol table = TableType(chain[0]);
        Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> empty = new(SymbolEqualityComparer.Default);
        if (chain[0].ArgumentList.Arguments.Count != 1)
            throw new InvalidOperationException("An insertion requires one explicit mapped initializer.");

        var inserted = Assignments(chain[0].ArgumentList.Arguments[0].Expression, table, empty);
        var current = (Table: table, Alias: SqliteQueryCompiler.Mapping(table, _tableAttribute));
        var incoming = (Table: table, Alias: _excluded);
        var (Body, Bindings) = Lambda(chain[1], [current], empty);
        List<ExpressionSyntax> keys = Body is AnonymousObjectCreationExpressionSyntax anonymous ? [.. anonymous.Initializers.Select(value => value.Expression)] : [Body];
        List<string> columns = [.. keys.Select(key => ColumnName(key, Bindings))];
        if (columns.Count == 0 || columns.Distinct(StringComparer.Ordinal).Count() != columns.Count || columns.Any(column => inserted.All(value => value.Column != column)))
            throw new InvalidOperationException("Computed conflict handling requires unique explicitly inserted mapped conflict columns.");

        var assignments = Lambda(chain[2], [current, incoming], empty);
        var updates = Assignments(assignments.Body, table, assignments.Bindings);
        string physical = SqliteQueryCompiler.Quote(SqliteQueryCompiler.Mapping(table, _tableAttribute));
        _plan.Sql = _insertSql + physical + '(' + string.Join(_separator, inserted.Select(value => value.Column)) + _valuesSql + string.Join(_separator, inserted.Select(value => value.Value)) + ')' + _conflictSql + string.Join(_separator, columns) + _conflictUpdateSql + string.Join(_separator, updates.Select(value => value.Column + _equalSql + value.Value));
        if (_plan.Mode == QueryMode.LastInsertId)
            _plan.Sql += _identitySql;

        if (_plan.Mode == QueryMode.ResolvedId)
        {
            var identity = Lambda(chain.Last(), [current], empty);
            string identityColumn = ColumnName(identity.Body, identity.Bindings);
            if (_semantic.GetTypeInfo(identity.Body).Type?.SpecialType != SpecialType.System_Int64 || _plan.Result.SpecialType != SpecialType.System_Int64 || keys.Any(key => IsNullable(_semantic.GetTypeInfo(key).Type)))
                throw new InvalidOperationException("ResolveId requires an Int64 identity and non-nullable complete conflict keys.");

            _plan.Sql += _resolveSql + identityColumn + _fromSql + physical + _whereSql + string.Join(_andSql, columns.Select(column => column + _equalSql + inserted.Single(value => value.Column == column).Value));
        }
    }

    /// <summary>
    /// Resolves mapped initializer assignments while rejecting hidden computations or unmapped members.
    /// </summary>
    /// <param name="expression">The explicit mapped object creation.</param>
    /// <param name="table">The expected target table type.</param>
    /// <param name="bindings">Available current/excluded row bindings.</param>
    /// <returns>The unique quoted target columns and translated values.</returns>
    private List<(string Column, string Value)> Assignments(ExpressionSyntax expression, INamedTypeSymbol table, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings)
    {
        ObjectCreationExpressionSyntax creation = Creation(expression);
        if (!SymbolEqualityComparer.Default.Equals(_semantic.GetTypeInfo(creation).Type, table))
            throw new InvalidOperationException("Mutation initializers must construct the exact mapped target model.");

        List<(string Column, string Value)> values = [];
        foreach (ExpressionSyntax item in creation.Initializer!.Expressions)
        {
            if (item is not AssignmentExpressionSyntax assignment || assignment.Kind() != SyntaxKind.SimpleAssignmentExpression || _semantic.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property || !SymbolEqualityComparer.Default.Equals(property.ContainingType, table))
                throw new InvalidOperationException("Mutation initializers require simple assignments to mapped target properties.");

            values.Add((SqliteQueryCompiler.Quote(SqliteQueryCompiler.Mapping(property, _columnAttribute)), Expression(assignment.Right, bindings)));
        }

        if (values.Count == 0 || values.Select(value => value.Column).Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new InvalidOperationException("Mutation initializers require a nonempty list of unique mapped columns.");

        return values;
    }

    /// <summary>
    /// Translates supported scalar operations, correlated existence/counts and exact bound values.
    /// </summary>
    /// <param name="expression">The authored supported SQL expression.</param>
    /// <param name="bindings">Available row parameters including correlated outer sources.</param>
    /// <returns>The exact parameterized native SQLite expression.</returns>
    private string Expression(ExpressionSyntax expression, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings)
    {
        if (expression is ParenthesizedExpressionSyntax parentheses)
            return '(' + Expression(parentheses.Expression, bindings) + ')';

        if (expression is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            return Expression(suppressed.Operand, bindings);

        if (expression is MemberAccessExpressionSyntax member && member.Expression is IdentifierNameSyntax row && _semantic.GetSymbolInfo(row).Symbol is ISymbol symbol && bindings.ContainsKey(symbol))
            return Column(expression, bindings);

        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
            return '(' + _notSql + Expression(unary.Operand, bindings) + ')';

        if (expression is BinaryExpressionSyntax binary)
        {
            if (binary.IsKind(SyntaxKind.CoalesceExpression))
                return _coalesceSql + Expression(binary.Left, bindings) + _separator + Expression(binary.Right, bindings) + ')';

            bool arithmetic = binary.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression;
            if (arithmetic && SqliteQueryCompiler.Storage(_semantic.GetTypeInfo(binary).Type) is not (_integerStorage or _realStorage))
                throw new InvalidOperationException("Arithmetic expressions require supported numeric operands; string concatenation has no SQL arithmetic fallback.");

            bool nullable = IsNullable(_semantic.GetTypeInfo(binary.Left).Type) || IsNullable(_semantic.GetTypeInfo(binary.Right).Type) || IsNull(binary.Left) || IsNull(binary.Right);
            string operation = binary.Kind() switch
            {
                SyntaxKind.LogicalAndExpression => _andSql,
                SyntaxKind.LogicalOrExpression => _orSql,
                SyntaxKind.EqualsExpression => nullable ? _isSql : _equalSql,
                SyntaxKind.NotEqualsExpression => nullable ? _isNotSql : _notEqualSql,
                SyntaxKind.GreaterThanExpression => _greaterSql,
                SyntaxKind.GreaterThanOrEqualExpression => _greaterEqualSql,
                SyntaxKind.LessThanExpression => _lessSql,
                SyntaxKind.LessThanOrEqualExpression => _lessEqualSql,
                SyntaxKind.AddExpression => _plusSql,
                SyntaxKind.SubtractExpression => _minusSql,
                SyntaxKind.MultiplyExpression => _multiplySql,
                SyntaxKind.DivideExpression => _divideSql,
                SyntaxKind.ModuloExpression => _moduloSql,
                _ => throw new InvalidOperationException("This SQL expression operator is not supported.")
            };
            return '(' + Expression(binary.Left, bindings) + operation + Expression(binary.Right, bindings) + ')';
        }

        if (expression is InvocationExpressionSyntax call && _semantic.GetSymbolInfo(call).Symbol is IMethodSymbol method && method.ContainingType.ToDisplayString() == _intrinsics)
        {
            if (method.Name is _count or _exists)
            {
                if (call.ArgumentList.Arguments.Count != 1)
                    throw new InvalidOperationException("Correlated Count and Exists require one explicit predicate.");

                var source = NewSource(call);
                var (Body, Bindings) = Lambda(call, [source], bindings);
                string query = _selectSql + (method.Name == _count ? _countSql : _oneSql) + _fromSql + TableSource(source) + _whereSql + ScopedExpression(Body, Bindings, false);
                return method.Name == _exists ? _existsSql + query + ')' : '(' + query + ')';
            }

            if (method.Name == _substring && call.ArgumentList.Arguments.Count == 3)
                return _substringSql + Expression(call.ArgumentList.Arguments[0].Expression, bindings) + _separator + SubstringIndex(call.ArgumentList.Arguments[1].Expression, bindings) + _separator + SubstringIndex(call.ArgumentList.Arguments[2].Expression, bindings) + ')';

            if (method.Name == _sum && call.ArgumentList.Arguments.Count == 1)
            {
                if (!_aggregateProjection)
                    throw new InvalidOperationException("SUM is supported only in a projection and cannot contain another aggregate.");

                return _sumSql + ScopedExpression(call.ArgumentList.Arguments[0].Expression, bindings, false) + ')';
            }

            if (method.Name == _like && call.ArgumentList.Arguments.Count == 2)
                return '(' + Expression(call.ArgumentList.Arguments[0].Expression, bindings) + _likeSql + Expression(call.ArgumentList.Arguments[1].Expression, bindings) + ')';

            if (method.Name == _binaryGreaterThanOrEqual && call.ArgumentList.Arguments.Count == 2)
                return '(' + Expression(call.ArgumentList.Arguments[0].Expression, bindings) + _greaterEqualSql + Expression(call.ArgumentList.Arguments[1].Expression, bindings) + ')';

            throw new InvalidOperationException("This compile-time SQL intrinsic is not supported in an expression.");
        }

        return Bind(expression);
    }

    /// <summary>
    /// Restricts native aggregation to result projections while separating correlated predicate scopes.
    /// </summary>
    /// <param name="expression">The exact projected SQL expression.</param>
    /// <param name="bindings">The mapped source rows available to the projection.</param>
    /// <param name="allowAggregate">Whether the current projection scope permits one native aggregate.</param>
    /// <returns>The projected native SQL expression.</returns>
    private string ScopedExpression(ExpressionSyntax expression, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings, bool allowAggregate)
    {
        bool previous = _aggregateProjection;
        _aggregateProjection = allowAggregate;
        try
        {
            return Expression(expression, bindings);
        }
        finally
        {
            _aggregateProjection = previous;
        }
    }

    /// <summary>
    /// Retains literal substring offsets so SQLite can match existing expression indexes exactly.
    /// </summary>
    /// <param name="expression">The one-based offset or requested length.</param>
    /// <param name="bindings">Available mapped row bindings.</param>
    /// <returns>A compiler-verified integer literal, or the supported bound SQL expression.</returns>
    private string SubstringIndex(ExpressionSyntax expression, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings)
    {
        Optional<object?> constant = _semantic.GetConstantValue(expression);
        return constant.HasValue && constant.Value is int or long ? Convert.ToString(constant.Value, CultureInfo.InvariantCulture)! : Expression(expression, bindings);
    }

    /// <summary>
    /// Validates one direct mapped column and qualifies it with its exact checked source alias.
    /// </summary>
    /// <param name="expression">The direct mapped property reference.</param>
    /// <param name="bindings">Available row parameter bindings.</param>
    /// <returns>The exact qualified SQL column.</returns>
    private string Column(ExpressionSyntax expression, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings)
    {
        var (source, property) = Property(expression, bindings);
        return SqliteQueryCompiler.Quote(source.Alias) + '.' + SqliteQueryCompiler.Quote(SqliteQueryCompiler.Mapping(property, _columnAttribute));
    }

    /// <summary>
    /// Resolves a mapped target column without a source qualifier for conflict and assignment syntax.
    /// </summary>
    /// <param name="expression">The direct mapped property reference.</param>
    /// <param name="bindings">Available row parameter bindings.</param>
    /// <returns>The quoted physical column name.</returns>
    private string ColumnName(ExpressionSyntax expression, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings)
        => SqliteQueryCompiler.Quote(SqliteQueryCompiler.Mapping(Property(expression, bindings).Property, _columnAttribute));

    /// <summary>
    /// Resolves direct semantic row members without allowing captured or unbound objects.
    /// </summary>
    /// <param name="expression">The required direct source-column syntax.</param>
    /// <param name="bindings">The checked row parameter bindings.</param>
    /// <returns>The mapped source and property symbols.</returns>
    private ((INamedTypeSymbol Table, string Alias) Source, IPropertySymbol Property) Property(ExpressionSyntax expression, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings)
    {
        if (expression is not MemberAccessExpressionSyntax member || member.Expression is not IdentifierNameSyntax row || _semantic.GetSymbolInfo(row).Symbol is not ISymbol symbol || !bindings.TryGetValue(symbol, out var source) || _semantic.GetSymbolInfo(member).Symbol is not IPropertySymbol property || !SymbolEqualityComparer.Default.Equals(property.ContainingType, source.Table))
            throw new InvalidOperationException("A column reference must be a mapped property of a current row parameter.");

        return (source, property);
    }

    /// <summary>
    /// Binds source-method arguments or compiler constants without interpreting runtime expressions.
    /// </summary>
    /// <param name="expression">The supported parameter or constant syntax.</param>
    /// <returns>The deduplicated generated parameter placeholder.</returns>
    private string Bind(ExpressionSyntax expression)
    {
        string key;
        string source;
        ITypeSymbol? type = _semantic.GetTypeInfo(expression).Type;
        if (_semantic.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, _method))
        {
            key = parameter.Name;
            source = '@' + parameter.Name;
        }
        else if (_semantic.GetConstantValue(expression) is { HasValue: true } constant)
        {
            source = constant.Value is null ? SyntaxFacts.GetText(SyntaxKind.NullKeyword) : Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(constant.Value, true, false);
            key = source + ':' + type?.ToDisplayString();
        }
        else
        {
            throw new InvalidOperationException("Query values must be method parameters or compile-time constants; unsupported expressions have no runtime fallback.");
        }

        if (!_parameters.TryGetValue(key, out int index))
        {
            index = _plan.Parameters.Count;
            _parameters.Add(key, index);
            _plan.Parameters.Add((source, SqliteQueryCompiler.Storage(type)));
        }

        return _parameterPrefix + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolves all parameters of one source-ordered expression lambda into semantic row bindings.
    /// </summary>
    /// <param name="call">The checked fluent operation or correlated intrinsic.</param>
    /// <param name="rows">Mapped sources in authored join order.</param>
    /// <param name="outer">Outer row bindings retained for correlated predicates.</param>
    /// <returns>The expression body and its complete checked row binding scope.</returns>
    private (ExpressionSyntax Body, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> Bindings) Lambda(InvocationExpressionSyntax call, IReadOnlyList<(INamedTypeSymbol Table, string Alias)> rows, Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> outer)
    {
        if (call.ArgumentList.Arguments.Count != 1 || call.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda || lambda.Body is not ExpressionSyntax body)
            throw new InvalidOperationException("A composed operation requires one expression lambda.");

        List<ParameterSyntax> parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToList(),
            _ => throw new InvalidOperationException("The lambda parameter shape is not supported.")
        };
        if (parameters.Count != rows.Count)
            throw new InvalidOperationException("The lambda must explicitly name every mapped source in join order.");

        Dictionary<ISymbol, (INamedTypeSymbol Table, string Alias)> bindings = new(outer, SymbolEqualityComparer.Default);
        for (int index = 0; index < parameters.Count; index++)
        {
            IParameterSymbol parameter = _semantic.GetDeclaredSymbol(parameters[index]) ?? throw new InvalidOperationException("A row lambda parameter could not be resolved.");
            if (!SymbolEqualityComparer.Default.Equals(parameter.Type, rows[index].Table))
                throw new InvalidOperationException("A row lambda parameter does not match its mapped source position.");

            bindings.Add(parameter, rows[index]);
        }

        return (body, bindings);
    }

    /// <summary>
    /// Allocates a stable, compiler-owned alias for one explicitly mapped source.
    /// </summary>
    /// <param name="call">The generic source, join or correlated intrinsic invocation.</param>
    /// <returns>The exact mapped table and unique generated alias.</returns>
    private (INamedTypeSymbol Table, string Alias) NewSource(InvocationExpressionSyntax call)
        => (TableType(call), _aliasPrefix + (_nextAlias++).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Resolves one generic source model from the checked invocation symbol.
    /// </summary>
    /// <param name="call">The generic source declaration.</param>
    /// <returns>The explicit mapped source type.</returns>
    private INamedTypeSymbol TableType(InvocationExpressionSyntax call)
    {
        if (_semantic.GetSymbolInfo(call).Symbol is not IMethodSymbol symbol || symbol.TypeArguments.Length != 1 || symbol.TypeArguments[0] is not INamedTypeSymbol table)
            throw new InvalidOperationException("Every source operation requires exactly one explicit mapped table type.");

        SqliteQueryCompiler.Mapping(table, _tableAttribute);
        return table;
    }

    /// <summary>
    /// Formats one mapped FROM or JOIN source with its compiler-owned alias.
    /// </summary>
    /// <param name="source">The exact table model and allocated alias.</param>
    /// <returns>The quoted physical source and alias.</returns>
    private static string TableSource((INamedTypeSymbol Table, string Alias) source)
        => SqliteQueryCompiler.Quote(SqliteQueryCompiler.Mapping(source.Table, _tableAttribute)) + _asSql + SqliteQueryCompiler.Quote(source.Alias);

    /// <summary>
    /// Requires an explicit nonempty object initializer without constructor work.
    /// </summary>
    /// <param name="expression">The authored projection or mutation initializer.</param>
    /// <returns>The checked explicit creation expression.</returns>
    private static ObjectCreationExpressionSyntax Creation(ExpressionSyntax expression)
        => expression is ObjectCreationExpressionSyntax creation && creation.Initializer is not null && creation.ArgumentList?.Arguments.Count is not > 0 ? creation : throw new InvalidOperationException("A projection or mutation requires an explicit object initializer without constructor arguments.");

    /// <summary>
    /// Extracts a checked nested fluent projection without executing definition code.
    /// </summary>
    /// <param name="expression">The nested projection passed to UNION.</param>
    /// <returns>The source-to-projection operation chain.</returns>
    private List<InvocationExpressionSyntax> Chain(ExpressionSyntax expression)
    {
        List<InvocationExpressionSyntax> calls = [];
        while (expression is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax access)
        {
            if (_semantic.GetSymbolInfo(call).Symbol is not IMethodSymbol symbol || symbol.ContainingNamespace.ToDisplayString() != _namespace)
                throw new InvalidOperationException("Nested query branches must use only the checked compile-time SQL vocabulary.");

            calls.Insert(0, call);
            expression = access.Expression;
        }

        return calls;
    }

    /// <summary>
    /// Identifies a compiler-resolved null constant for SQL null-safe comparisons.
    /// </summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <returns>Whether the exact expression is a constant null.</returns>
    private bool IsNull(ExpressionSyntax expression)
        => _semantic.GetConstantValue(expression) is { HasValue: true, Value: null };

    /// <summary>
    /// Preserves nullable equality semantics for annotated references and wrapped values.
    /// </summary>
    /// <param name="type">The exact compiler-resolved expression type.</param>
    /// <returns>Whether the expression can carry null.</returns>
    private static bool IsNullable(ITypeSymbol? type)
        => type?.NullableAnnotation == NullableAnnotation.Annotated || type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>
    /// Reads the semantic call chain's authored operation name.
    /// </summary>
    /// <param name="call">The already checked member invocation.</param>
    /// <returns>The exact member identifier.</returns>
    private static string Name(InvocationExpressionSyntax call)
        => ((MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText;
}
