using System.Reflection;
using Ironmon.Sqlite.QueryGenerator;
using Ironmon.Sqlite.Querying;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Verifies build-time failure for new query shapes that the prototype cannot faithfully translate.
/// </summary>
public sealed class SqlQueryGeneratorTests
{
    private const string _fixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UnsupportedPredicate.cs";
    private const string _deleteFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UnfilteredDelete.cs";
    private const string _resolveFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.ResolveWithoutConflict.cs";
    private const string _nullableResolveFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.ResolveNullableKey.cs";
    private const string _anyConflictResolveFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.ResolveAnyConflict.cs";
    private const string _anyConflictUpdateFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UpdateAnyConflict.cs";
    private const string _thenByFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.ThenByWithoutOrder.cs";
    private const string _thenByDescendingFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.ThenByDescendingWithoutOrder.cs";
    private const string _repeatedOrderFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.RepeatedOrder.cs";
    private const string _filterAfterOrderFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.FilterAfterOrder.cs";
    private const string _computedOrderFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.ComputedOrder.cs";
    private const string _multipleColumnOrderFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.MultipleColumnOrder.cs";
    private const string _unfilteredUpdateFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UnfilteredUpdate.cs";
    private const string _untargetedComputedConflictFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UntargetedComputedConflict.cs";
    private const string _distinctCountFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.DistinctCount.cs";
    private const string _unmatchedUnionPropertiesFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UnmatchedUnionProperties.cs";
    private const string _nestedRightUnionFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.NestedRightUnion.cs";
    private const string _unsupportedAdvancedPredicateFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UnsupportedAdvancedPredicate.cs";
    private const string _stringArithmeticFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.StringArithmetic.cs";
    private const string _aggregatePredicateFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.AggregatePredicate.cs";
    private const string _assemblyName = "SqlGeneratorFixture";
    private const string _platformAssemblies = "TRUSTED_PLATFORM_ASSEMBLIES";
    private const string _diagnosticId = "ISQL001";
    private const string _factoryFixture = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.NonQueryFactoryRequired.cs";
    private const string _factoryDiagnosticId = "ISQL003";

    /// <summary>
    /// Rejects otherwise valid unsupported syntax and emits no callable query implementation.
    /// </summary>
    /// <param name="fixture">The well-typed definition that must be rejected by the generator.</param>
    /// <param name="message">The expected diagnostic explanation.</param>
    [Theory]
    [InlineData(_fixture, "Predicates currently support comparisons")]
    [InlineData(_deleteFixture, "Delete requires From, at least one explicit Where filter")]
    [InlineData(_resolveFixture, "ResolveId requires OnConflict")]
    [InlineData(_nullableResolveFixture, "ResolveId requires non-nullable conflict columns")]
    [InlineData(_anyConflictResolveFixture, "OnConflict without columns supports only DoNothing")]
    [InlineData(_anyConflictUpdateFixture, "OnConflict without columns supports only DoNothing")]
    [InlineData(_thenByFixture, "Ordering requires exactly one OrderBy")]
    [InlineData(_thenByDescendingFixture, "Ordering requires exactly one OrderBy")]
    [InlineData(_repeatedOrderFixture, "Ordering requires exactly one OrderBy")]
    [InlineData(_filterAfterOrderFixture, "Where filters must precede the ordering chain")]
    [InlineData(_computedOrderFixture, "A column reference must be a mapped property")]
    [InlineData(_multipleColumnOrderFixture, "A column reference must be a mapped property")]
    [InlineData(_unfilteredUpdateFixture, "An unfiltered mutation requires explicit AllRows intent")]
    [InlineData(_untargetedComputedConflictFixture, "A composed operation requires one expression lambda")]
    [InlineData(_distinctCountFixture, "Count supports filtered and joined sources without Distinct")]
    [InlineData(_unmatchedUnionPropertiesFixture, "UNION branches require the same exact result type and property order")]
    [InlineData(_nestedRightUnionFixture, "Compose UNION branches left to right")]
    [InlineData(_unsupportedAdvancedPredicateFixture, "unsupported expressions have no runtime fallback")]
    [InlineData(_stringArithmeticFixture, "Arithmetic expressions require supported numeric operands")]
    [InlineData(_aggregatePredicateFixture, "SUM is supported only in a projection")]
    public void UnsupportedWellTypedQueryProducesBuildErrorWithoutFallbackCode(string fixture, string message)
    {
        using Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(fixture) ?? throw new InvalidDataException("The unsupported-query fixture is missing.");
        using StreamReader reader = new(input);
        SyntaxTree syntax = CSharpSyntaxTree.ParseText(reader.ReadToEnd());
        string platforms = (string?)AppContext.GetData(_platformAssemblies) ?? throw new InvalidOperationException("The test runtime did not expose platform references.");
        MetadataReference[] references = [.. platforms.Split(Path.PathSeparator).Append(typeof(Sql).Assembly.Location).Append(typeof(SqliteConnection).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path))];
        CSharpCompilation compilation = CSharpCompilation.Create(_assemblyName, [syntax], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SqlQueryGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        Diagnostic failure = Assert.Single(diagnostics, diagnostic => diagnostic.Id == _diagnosticId);
        Assert.Equal(DiagnosticSeverity.Error, failure.Severity);
        Assert.Contains(message, failure.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    /// <summary>
    /// Rejects optional reusable factories for scalar reads instead of bypassing their result contract.
    /// </summary>
    [Fact]
    public void ReusableCommandFactoryRequiresANonQueryOperation()
    {
        using Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(_factoryFixture) ?? throw new InvalidDataException("The factory-rejection fixture is missing.");
        using StreamReader reader = new(input);
        SyntaxTree syntax = CSharpSyntaxTree.ParseText(reader.ReadToEnd());
        string platforms = (string?)AppContext.GetData(_platformAssemblies) ?? throw new InvalidOperationException("The test runtime did not expose platform references.");
        MetadataReference[] references = [.. platforms.Split(Path.PathSeparator).Append(typeof(Sql).Assembly.Location).Append(typeof(SqliteConnection).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path))];
        CSharpCompilation compilation = CSharpCompilation.Create(_assemblyName, [syntax], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SqlCommandFactoryGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        Diagnostic failure = Assert.Single(diagnostics, diagnostic => diagnostic.Id == _factoryDiagnosticId);
        Assert.Equal(DiagnosticSeverity.Error, failure.Severity);
        Assert.Contains("Reusable command factories support only non-query definitions", failure.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }
}
