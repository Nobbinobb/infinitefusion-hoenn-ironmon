using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ironmon.Sqlite.QueryGenerator;

/// <summary>
/// Translates a deliberately restricted, semantically checked C# query syntax during compilation.
/// </summary>
internal sealed class SqliteQueryCompiler
{
    private const string _dslNamespace = "Ironmon.Sqlite.Querying";
    private const string _queryType = "SqlQuery";
    private const string _tableAttribute = _dslNamespace + ".SqlTableAttribute";
    private const string _columnAttribute = _dslNamespace + ".SqlColumnAttribute";
    private const string _from = "From";
    private const string _where = "Where";
    private const string _orderBy = "OrderBy";
    private const string _orderByDescending = "OrderByDescending";
    private const string _thenBy = "ThenBy";
    private const string _thenByDescending = "ThenByDescending";
    private const string _select = "Select";
    private const string _insert = "Insert";
    private const string _first = "FirstOrDefault";
    private const string _enumerable = "AsEnumerable";
    private const string _execute = "ExecuteNonQuery";
    private const string _identity = "LastInsertId";
    private const string _resolveId = "ResolveId";
    private const string _conflict = "OnConflict";
    private const string _update = "UpdateFromInserted";
    private const string _nothing = "DoNothing";
    private const string _delete = "Delete";
    private const string _connection = "connection";
    private const string _transaction = "transaction";
    private const string _selectSql = "SELECT ";
    private const string _fromSql = " FROM ";
    private const string _whereSql = " WHERE ";
    private const string _orderBySql = " ORDER BY ";
    private const string _ascendingSql = " ASC";
    private const string _descendingSql = " DESC";
    private const string _insertSql = "INSERT INTO ";
    private const string _valuesSql = ") VALUES(";
    private const string _conflictSql = " ON CONFLICT(";
    private const string _updateSql = ") DO UPDATE SET ";
    private const string _nothingSql = ") DO NOTHING";
    private const string _anyConflictNothingSql = " ON CONFLICT DO NOTHING";
    private const string _deleteSql = "DELETE FROM ";
    private const string _excludedSql = "=excluded.";
    private const string _identitySql = "; SELECT last_insert_rowid()";
    private const string _resolveSql = "; SELECT ";
    private const string _limitSql = " LIMIT 1";
    private const string _and = " AND ";
    private const string _or = " OR ";
    private const string _equal = " = ";
    private const string _notEqual = " <> ";
    private const string _is = " IS ";
    private const string _isNot = " IS NOT ";
    private const string _greater = " > ";
    private const string _greaterEqual = " >= ";
    private const string _less = " < ";
    private const string _lessEqual = " <= ";
    private const string _separator = ",";
    private const string _quote = "\"";
    private const string _escapedQuote = "\"\"";
    private const string _parameterPrefix = "@p";
    private const string _nullable = "Nullable";
    private const string _integerStorage = "Integer";
    private const string _textStorage = "Text";
    private const string _blobStorage = "Blob";
    private const string _realStorage = "Real";
    private const string _readerPrefix = "reader.";
    private const string _int32Reader = "GetInt32";
    private const string _int64Reader = "GetInt64";
    private const string _booleanReader = "GetBoolean";
    private const string _stringReader = "GetString";
    private const string _doubleReader = "GetDouble";
    private const string _valueReader = "GetValue";
    private const string _blobReaderPrefix = "(byte[])";
    private const string _nullableReaderPrefix = "reader.IsDBNull(";
    private const string _nullableReaderMiddle = ") ? null : ";
    private static readonly SymbolDisplayFormat _typeFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
    private readonly IMethodSymbol _method;
    private readonly SemanticModel _semantic;
    private readonly QueryPlan _plan;
    private readonly INamedTypeSymbol _table;
    private readonly Dictionary<string, int> _parameterIndexes = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a compiler for one checked operation and its mapped source or target table.
    /// </summary>
    /// <param name="method">The original static definition method.</param>
    /// <param name="semantic">The semantic model for the definition's syntax tree.</param>
    /// <param name="plan">The plan receiving final SQL, bindings and readers.</param>
    /// <param name="table">The mapped physical source or target model.</param>
    private SqliteQueryCompiler(IMethodSymbol method, SemanticModel semantic, QueryPlan plan, INamedTypeSymbol table)
    {
        _method = method;
        _semantic = semantic;
        _plan = plan;
        _table = table;
    }

    /// <summary>
    /// Compiles one complete method body or rejects it before any executable method is generated.
    /// </summary>
    /// <param name="method">The statically declared query definition.</param>
    /// <param name="compilation">The complete consumer compilation used to resolve symbols and constants.</param>
    /// <param name="cancellationToken">Cancels generation when the compiler abandons the build.</param>
    /// <returns>The validated provider execution plan.</returns>
    internal static QueryPlan Compile(IMethodSymbol method, Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!method.IsStatic || method.IsGenericMethod || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsOptional || parameter.IsParams || parameter.Name is _connection or _transaction))
            throw new InvalidOperationException("Query definitions must be static, non-generic methods with required value parameters; connection and transaction are reserved generated arguments.");

        if (method.ReturnType is not INamedTypeSymbol result || result.Name != _queryType || result.ContainingNamespace.ToDisplayString() != _dslNamespace || result.TypeArguments.Length != 1)
            throw new InvalidOperationException("A query definition must return SqlQuery<TResult>.");

        MethodDeclarationSyntax syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax(cancellationToken);
        ExpressionSyntax expression = syntax.ExpressionBody?.Expression ?? (syntax.Body?.Statements.Count == 1 && syntax.Body.Statements[0] is ReturnStatementSyntax statement ? statement.Expression : null) ?? throw new InvalidOperationException("A query definition must consist of one complete return expression.");
        SemanticModel semantic = compilation.GetSemanticModel(syntax.SyntaxTree);
        List<InvocationExpressionSyntax> chain = [];
        while (expression is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax access)
        {
            IMethodSymbol symbol = semantic.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol ?? throw new InvalidOperationException("Every query operation must resolve to a supported typed definition method.");
            if (symbol.ContainingNamespace.ToDisplayString() != _dslNamespace)
                throw new InvalidOperationException("Only operations from the compile-time SQLite query syntax are supported.");

            chain.Insert(0, invocation);
            expression = access.Expression;
        }

        if (chain.Count < 2 || semantic.GetSymbolInfo(chain[0], cancellationToken).Symbol is not IMethodSymbol root || root.TypeArguments.Length != 1 || root.TypeArguments[0] is not INamedTypeSymbol table)
            throw new InvalidOperationException("A query must start with Sql.From<TTable>() or Sql.Insert(new TTable { ... }).");

        if (AdvancedQueryCompiler.RequiresAdvanced(chain, semantic))
            return AdvancedQueryCompiler.Compile(method, result.TypeArguments[0], semantic, chain);

        QueryMode mode = Name(chain[chain.Count - 1]) switch
        {
            _first => QueryMode.FirstOrDefault,
            _enumerable => QueryMode.Enumerable,
            _execute or _delete => QueryMode.NonQuery,
            _identity => QueryMode.LastInsertId,
            _resolveId => QueryMode.ResolvedId,
            _ => throw new InvalidOperationException("The query terminal is not supported by this prototype.")
        };

        QueryPlan plan = new(method, result.TypeArguments[0], mode);
        SqliteQueryCompiler compiler = new(method, semantic, plan, table);
        if (root.Name == _from && mode is QueryMode.FirstOrDefault or QueryMode.Enumerable)
        {
            compiler.Select(chain);
        }
        else if (root.Name == _from && Name(chain[chain.Count - 1]) == _delete)
        {
            compiler.Delete(chain);
        }
        else if (root.Name == _insert && mode is QueryMode.NonQuery or QueryMode.LastInsertId or QueryMode.ResolvedId)
        {
            compiler.Insert(chain);
        }
        else
        {
            throw new InvalidOperationException("The source operation and terminal do not form a supported query.");
        }

        if (method.Parameters.Any(parameter => !compiler._parameterIndexes.ContainsKey(parameter.Name)))
            throw new InvalidOperationException("Every query argument must be bound by the query; unused arguments are rejected.");

        return plan;
    }

    /// <summary>
    /// Translates filters, an optional ordered column chain and an explicit result projection.
    /// </summary>
    /// <param name="chain">The semantically resolved selection call chain.</param>
    private void Select(List<InvocationExpressionSyntax> chain)
    {
        if (chain.Count < 3 || Name(chain[chain.Count - 2]) != _select)
            throw new InvalidOperationException("Selections support From, Where filters, an optional OrderBy and ThenBy chain, Select, and one supported terminal.");

        var (Parameter, Body) = Lambda(chain[chain.Count - 2]);
        if (Body is not ObjectCreationExpressionSyntax creation || creation.ArgumentList?.Arguments.Count > 0 || creation.Initializer is null)
            throw new InvalidOperationException("Select needs an explicit result object initializer assigning mapped columns.");

        _plan.RowType = TypeName(_semantic.GetTypeInfo(creation).Type ?? throw new InvalidOperationException("The projected result type could not be resolved."));
        List<string> columns = [];
        foreach (ExpressionSyntax initializer in creation.Initializer.Expressions)
        {
            if (initializer is not AssignmentExpressionSyntax assignment || assignment.Kind() != SyntaxKind.SimpleAssignmentExpression || assignment.Left is not IdentifierNameSyntax property)
                throw new InvalidOperationException("Result projections support only simple property assignments.");

            IPropertySymbol column = Column(assignment.Right, Parameter);
            columns.Add(Quote(Mapping(column, _columnAttribute)));
            _plan.Projection.Add((property.Identifier.ValueText, Reader(column.Type, columns.Count - 1)));
        }

        if (columns.Count == 0 || _plan.Projection.Select(value => value.Property).Distinct(StringComparer.Ordinal).Count() != columns.Count)
            throw new InvalidOperationException("A projection needs at least one unique result property.");

        List<string> filters = [];
        List<string> ordering = [];
        foreach (InvocationExpressionSyntax call in chain.Skip(1).Take(chain.Count - 3))
        {
            string operation = Name(call);
            if (operation == _where)
            {
                if (ordering.Count != 0)
                    throw new InvalidOperationException("Where filters must precede the ordering chain.");

                var predicate = Lambda(call);
                filters.Add(Predicate(predicate.Body, predicate.Parameter));
            }
            else if (operation is _orderBy or _orderByDescending or _thenBy or _thenByDescending)
            {
                bool initial = operation is _orderBy or _orderByDescending;
                if (initial && ordering.Count != 0 || !initial && ordering.Count == 0)
                    throw new InvalidOperationException("Ordering requires exactly one OrderBy or OrderByDescending followed by zero or more ThenBy or ThenByDescending operations.");

                var selector = Lambda(call);
                IPropertySymbol column = Column(selector.Body, selector.Parameter);
                string direction = operation is _orderByDescending or _thenByDescending ? _descendingSql : _ascendingSql;
                ordering.Add(Quote(Mapping(column, _columnAttribute)) + direction);
            }
            else
            {
                throw new InvalidOperationException("Selections support only Where filters and a mapped ordering chain before Select.");
            }
        }

        _plan.Sql = _selectSql + string.Join(_separator, columns) + _fromSql + Quote(Mapping(_table, _tableAttribute));
        if (filters.Count != 0)
            _plan.Sql += _whereSql + string.Join(_and, filters);

        if (ordering.Count != 0)
            _plan.Sql += _orderBySql + string.Join(_separator, ordering);

        if (_plan.Mode == QueryMode.FirstOrDefault)
            _plan.Sql += _limitSql;
    }

    /// <summary>
    /// Translates insertion assignments and explicitly declared native conflict handling.
    /// </summary>
    /// <param name="chain">The semantically resolved insertion call chain.</param>
    private void Insert(List<InvocationExpressionSyntax> chain)
    {
        bool conflict = chain.Count == 4 && Name(chain[1]) == _conflict && Name(chain[2]) is _update or _nothing && _plan.Mode is QueryMode.NonQuery or QueryMode.ResolvedId;
        if (chain.Count != 2 && !conflict || _plan.Mode == QueryMode.ResolvedId && !conflict)
            throw new InvalidOperationException("Insert supports LastInsertId or ExecuteNonQuery, optionally with conflict handling; ResolveId requires OnConflict and UpdateFromInserted or DoNothing.");

        if (chain[0].ArgumentList.Arguments.Count != 1 || chain[0].ArgumentList.Arguments[0].Expression is not ObjectCreationExpressionSyntax creation || creation.Initializer is null || creation.ArgumentList?.Arguments.Count > 0)
            throw new InvalidOperationException("Insert needs an explicit mapped object initializer.");

        List<string> columns = [];
        List<string> values = [];
        foreach (ExpressionSyntax initializer in creation.Initializer.Expressions)
        {
            if (initializer is not AssignmentExpressionSyntax assignment || assignment.Kind() != SyntaxKind.SimpleAssignmentExpression || _semantic.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property || !SymbolEqualityComparer.Default.Equals(property.ContainingType, _table))
                throw new InvalidOperationException("Insert supports only simple assignments to mapped target properties.");

            columns.Add(Quote(Mapping(property, _columnAttribute)));
            values.Add(Bind(assignment.Right));
        }

        if (columns.Count == 0 || columns.Distinct(StringComparer.Ordinal).Count() != columns.Count)
            throw new InvalidOperationException("An insertion needs at least one unique mapped column.");

        _plan.Sql = _insertSql + Quote(Mapping(_table, _tableAttribute)) + '(' + string.Join(_separator, columns) + _valuesSql + string.Join(_separator, values) + ')';
        if (conflict)
        {
            bool anyConflict = chain[1].ArgumentList.Arguments.Count == 0;
            if (anyConflict && (Name(chain[2]) != _nothing || _plan.Mode != QueryMode.NonQuery))
                throw new InvalidOperationException("OnConflict without columns supports only DoNothing followed by ExecuteNonQuery; updates and identity resolution require an explicit conflict target.");

            string[] keys = anyConflict ? [] : ColumnList(chain[1]);
            string[] updates = Name(chain[2]) == _update ? ColumnList(chain[2]) : [];
            if (keys.Concat(updates).Any(column => !columns.Contains(column)))
                throw new InvalidOperationException("Upsert keys and excluded update columns must be explicitly inserted.");

            _plan.Sql += anyConflict ? _anyConflictNothingSql : _conflictSql + string.Join(_separator, keys) + (updates.Length == 0 ? _nothingSql : _updateSql + string.Join(_separator, updates.Select(column => column + _excludedSql + column)));
            if (_plan.Mode == QueryMode.ResolvedId)
                ResolveIdentity(chain[chain.Count - 1], chain[1], keys, columns, values);
        }

        if (_plan.Mode == QueryMode.LastInsertId)
            _plan.Sql += _identitySql;
    }

    /// <summary>
    /// Resolves the inserted or existing identity using the complete non-nullable conflict key.
    /// </summary>
    /// <param name="terminal">The exact mapped identity selector.</param>
    /// <param name="conflict">The declared unique conflict target.</param>
    /// <param name="keys">The validated quoted target columns.</param>
    /// <param name="columns">The explicitly inserted quoted columns.</param>
    /// <param name="values">The corresponding already-bound insertion placeholders.</param>
    private void ResolveIdentity(InvocationExpressionSyntax terminal, InvocationExpressionSyntax conflict, string[] keys, List<string> columns, List<string> values)
    {
        var (Parameter, Body) = Lambda(terminal);
        IPropertySymbol identity = Column(Body, Parameter);
        if (identity.Type.SpecialType != SpecialType.System_Int64 || _plan.Result.SpecialType != SpecialType.System_Int64)
            throw new InvalidOperationException("ResolveId requires a mapped non-nullable Int64 identity and an Int64 result.");

        if (ColumnProperties(conflict).Any(column => IsNullable(column.Type)))
            throw new InvalidOperationException("ResolveId requires non-nullable conflict columns so the complete key identifies one row.");

        string predicates = string.Join(_and, keys.Select(key => key + _equal + values[columns.IndexOf(key)]));
        _plan.Sql += _resolveSql + Quote(Mapping(identity, _columnAttribute)) + _fromSql + Quote(Mapping(_table, _tableAttribute)) + _whereSql + predicates;
    }

    /// <summary>
    /// Translates a deletion only when at least one explicit supported filter is present.
    /// </summary>
    /// <param name="chain">The mapped source, filters and deletion terminal.</param>
    private void Delete(List<InvocationExpressionSyntax> chain)
    {
        if (chain.Count < 3 || chain.Skip(1).Take(chain.Count - 2).Any(call => Name(call) != _where))
            throw new InvalidOperationException("Delete requires From, at least one explicit Where filter, and Delete.");

        List<string> filters = [];
        foreach (InvocationExpressionSyntax call in chain.Skip(1).Take(chain.Count - 2))
        {
            var (Parameter, Body) = Lambda(call);
            filters.Add(Predicate(Body, Parameter));
        }

        _plan.Sql = _deleteSql + Quote(Mapping(_table, _tableAttribute)) + _whereSql + string.Join(_and, filters);
    }

    /// <summary>
    /// Translates supported binary comparisons and preserves Boolean grouping explicitly.
    /// </summary>
    /// <param name="expression">The original predicate expression.</param>
    /// <param name="parameter">The lambda's mapped row parameter name.</param>
    /// <returns>The fully parameterized SQLite predicate.</returns>
    private string Predicate(ExpressionSyntax expression, string parameter)
    {
        if (expression is ParenthesizedExpressionSyntax parentheses)
            return '(' + Predicate(parentheses.Expression, parameter) + ')';

        if (expression is not BinaryExpressionSyntax binary)
            throw new InvalidOperationException("Predicates currently support comparisons joined with && or ||.");

        bool boolean = binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression);
        bool nullable = IsNullable(_semantic.GetTypeInfo(binary.Left).Type) || IsNullable(_semantic.GetTypeInfo(binary.Right).Type) || IsNull(binary.Left) || IsNull(binary.Right);
        string operation = binary.Kind() switch
        {
            SyntaxKind.LogicalAndExpression => _and,
            SyntaxKind.LogicalOrExpression => _or,
            SyntaxKind.EqualsExpression => nullable ? _is : _equal,
            SyntaxKind.NotEqualsExpression => nullable ? _isNot : _notEqual,
            SyntaxKind.GreaterThanExpression => _greater,
            SyntaxKind.GreaterThanOrEqualExpression => _greaterEqual,
            SyntaxKind.LessThanExpression => _less,
            SyntaxKind.LessThanOrEqualExpression => _lessEqual,
            _ => throw new InvalidOperationException("This predicate operator is not supported by the prototype.")
        };

        string left = boolean ? Predicate(binary.Left, parameter) : Operand(binary.Left, parameter);
        string right = boolean ? Predicate(binary.Right, parameter) : Operand(binary.Right, parameter);
        return '(' + left + operation + right + ')';
    }

    /// <summary>
    /// Resolves a mapped row column or an exact parameterized input value.
    /// </summary>
    /// <param name="expression">The supported operand expression.</param>
    /// <param name="parameter">The mapped row lambda parameter name.</param>
    /// <returns>A quoted column identifier or generated parameter placeholder.</returns>
    private string Operand(ExpressionSyntax expression, string parameter)
        => expression is MemberAccessExpressionSyntax member && member.Expression is IdentifierNameSyntax row && row.Identifier.ValueText == parameter ? Quote(Mapping(Column(expression, parameter), _columnAttribute)) : Bind(expression);

    /// <summary>
    /// Resolves a semantic source property and rejects arbitrary computed projection expressions.
    /// </summary>
    /// <param name="expression">The expected row property reference.</param>
    /// <param name="parameter">The lambda's mapped row parameter name.</param>
    /// <returns>The mapped property belonging to this operation's table.</returns>
    private IPropertySymbol Column(ExpressionSyntax expression, string parameter)
    {
        if (expression is not MemberAccessExpressionSyntax member || member.Expression is not IdentifierNameSyntax row || row.Identifier.ValueText != parameter || _semantic.GetSymbolInfo(member).Symbol is not IPropertySymbol property || !SymbolEqualityComparer.Default.Equals(property.ContainingType, _table))
            throw new InvalidOperationException("A column reference must be a mapped property of the current row parameter.");

        return property;
    }

    /// <summary>
    /// Resolves a single column or anonymous column list used by native conflict handling.
    /// </summary>
    /// <param name="call">The conflict target or excluded-value update operation.</param>
    /// <returns>Unique, quoted mapped columns in authored order.</returns>
    private string[] ColumnList(InvocationExpressionSyntax call)
    {
        string[] columns = ColumnProperties(call).Select(property => Quote(Mapping(property, _columnAttribute))).ToArray();
        if (columns.Length == 0 || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new InvalidOperationException("Conflict targets and updates need a nonempty list of unique mapped columns.");

        return columns;
    }

    /// <summary>
    /// Resolves mapped properties from a single-column or anonymous-column selector.
    /// </summary>
    /// <param name="call">The operation containing the mapped column selector.</param>
    /// <returns>The selected property symbols in authored order.</returns>
    private IPropertySymbol[] ColumnProperties(InvocationExpressionSyntax call)
    {
        var (Parameter, Body) = Lambda(call);
        IEnumerable<ExpressionSyntax> expressions = Body is AnonymousObjectCreationExpressionSyntax anonymous ? anonymous.Initializers.Select(value => value.Expression) : [Body];
        return expressions.Select(expression => Column(expression, Parameter)).ToArray();
    }

    /// <summary>
    /// Binds only original method arguments and compile-time constants, with exact provider storage types.
    /// </summary>
    /// <param name="expression">The argument reference or compiler-resolved constant.</param>
    /// <returns>The deduplicated parameter placeholder.</returns>
    private string Bind(ExpressionSyntax expression)
    {
        ISymbol? symbol = _semantic.GetSymbolInfo(expression).Symbol;
        string key;
        string source;
        ITypeSymbol? type = _semantic.GetTypeInfo(expression).Type;
        if (symbol is IParameterSymbol parameter && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, _method))
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
            throw new InvalidOperationException("Query values must be method parameters or compile-time constants. Compute other values before calling the generated method.");
        }

        if (!_parameterIndexes.TryGetValue(key, out int index))
        {
            index = _plan.Parameters.Count;
            _parameterIndexes.Add(key, index);
            _plan.Parameters.Add((source, Storage(type)));
        }

        return _parameterPrefix + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Extracts the only supported expression-lambda argument without executing user code.
    /// </summary>
    /// <param name="call">The operation containing the one-row lambda.</param>
    /// <returns>The lambda's original row parameter and expression body.</returns>
    private static (string Parameter, ExpressionSyntax Body) Lambda(InvocationExpressionSyntax call)
    {
        if (call.ArgumentList.Arguments.Count != 1 || call.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda || lambda.Body is not ExpressionSyntax body)
            throw new InvalidOperationException("This prototype requires a single expression lambda with one unparenthesized row parameter.");

        return (lambda.Parameter.Identifier.ValueText, body);
    }

    /// <summary>
    /// Reads a compiler-resolved table or column mapping, including named constant attribute arguments.
    /// </summary>
    /// <param name="symbol">The mapped table type or column property.</param>
    /// <param name="attribute">The exact mapping attribute metadata name.</param>
    /// <returns>The validated physical SQLite identifier.</returns>
    internal static string Mapping(ISymbol symbol, string attribute)
    {
        AttributeData? mapping = symbol.GetAttributes().SingleOrDefault(value => value.AttributeClass?.ToDisplayString() == attribute);
        if (mapping?.ConstructorArguments.Length != 1 || mapping.ConstructorArguments[0].Value is not string name || string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0)
            throw new InvalidOperationException("Every referenced table and column needs one valid explicit physical-name mapping.");

        return name;
    }

    /// <summary>
    /// Selects the provider storage type while preserving native SQLite storage classes for object inputs.
    /// </summary>
    /// <param name="type">The parameter's exact compiler-resolved type, including nullable wrappers.</param>
    /// <returns>The explicit SQLite storage type, or null for an uncoerced native ANY input.</returns>
    internal static string? Storage(ITypeSymbol? type)
    {
        type = Unwrap(type);
        if (type is null || type.SpecialType == SpecialType.System_Object)
            return null;

        if (type is IArrayTypeSymbol array && array.ElementType.SpecialType == SpecialType.System_Byte && array.Rank == 1)
            return _blobStorage;

        return type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Boolean => _integerStorage,
            SpecialType.System_String => _textStorage,
            SpecialType.System_Double => _realStorage,
            _ => throw new InvalidOperationException("This parameter type is not supported. Extend the generator's exact binding rules before using it.")
        };
    }

    /// <summary>
    /// Builds a direct ordinal-based reader that preserves integer widths, binary bytes and nulls.
    /// </summary>
    /// <param name="type">The mapped column's compiler-resolved type.</param>
    /// <param name="ordinal">The zero-based projected SQLite column ordinal.</param>
    /// <returns>The generated C# value-reading expression.</returns>
    internal static string Reader(ITypeSymbol type, int ordinal)
    {
        ITypeSymbol unwrapped = Unwrap(type)!;
        string prefix = string.Empty;
        string getter;
        if (unwrapped is IArrayTypeSymbol array && array.ElementType.SpecialType == SpecialType.System_Byte && array.Rank == 1)
        {
            prefix = _blobReaderPrefix;
            getter = _valueReader;
        }
        else
        {
            getter = unwrapped.SpecialType switch
            {
                SpecialType.System_Int32 => _int32Reader,
                SpecialType.System_Int64 => _int64Reader,
                SpecialType.System_Boolean => _booleanReader,
                SpecialType.System_String => _stringReader,
                SpecialType.System_Double => _doubleReader,
                SpecialType.System_Object => _valueReader,
                _ => throw new InvalidOperationException("This result column type is not supported by the prototype's exact reader rules.")
            };
        }

        string index = ordinal.ToString(CultureInfo.InvariantCulture);
        string read = prefix + _readerPrefix + getter + '(' + index + ')';
        return IsNullable(type) ? _nullableReaderPrefix + index + _nullableReaderMiddle + read : read;
    }

    /// <summary>
    /// Detects constant null operands when choosing SQLite null-safe equality.
    /// </summary>
    /// <param name="expression">The operand to inspect semantically.</param>
    /// <returns>Whether the expression is a compiler-resolved null constant.</returns>
    private bool IsNull(ExpressionSyntax expression)
        => _semantic.GetConstantValue(expression) is { HasValue: true, Value: null };

    /// <summary>
    /// Detects nullable reference annotations and Nullable value wrappers.
    /// </summary>
    /// <param name="type">The compiler-resolved input or column type.</param>
    /// <returns>Whether a null guard or null-safe equality is required.</returns>
    private static bool IsNullable(ITypeSymbol? type)
        => type?.NullableAnnotation == NullableAnnotation.Annotated || type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>
    /// Resolves the storage type inside a Nullable value wrapper.
    /// </summary>
    /// <param name="type">The original compiler-resolved type.</param>
    /// <returns>The underlying value type, or the original reference or non-nullable value type.</returns>
    private static ITypeSymbol? Unwrap(ITypeSymbol? type)
        => type is INamedTypeSymbol named && named.Name == _nullable && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0] : type;

    /// <summary>
    /// Quotes identifiers without interpreting their contents as SQL syntax.
    /// </summary>
    /// <param name="name">The compiler-resolved physical identifier.</param>
    /// <returns>The escaped SQLite identifier.</returns>
    internal static string Quote(string name)
        => _quote + name.Replace(_quote, _escapedQuote) + _quote;

    /// <summary>
    /// Reads the already validated fluent method's syntax name.
    /// </summary>
    /// <param name="call">The member-access invocation in the current chain.</param>
    /// <returns>The invoked operation name.</returns>
    private static string Name(InvocationExpressionSyntax call)
        => ((MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText;

    /// <summary>
    /// Formats a consumer type without relying on its using directives or losing nullability.
    /// </summary>
    /// <param name="type">The exact generated signature or result type.</param>
    /// <returns>The fully qualified nullable-aware C# type name.</returns>
    internal static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(_typeFormat);
}
