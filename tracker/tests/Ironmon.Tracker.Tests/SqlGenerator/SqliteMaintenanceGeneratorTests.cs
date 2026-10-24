using Ironmon.Sqlite.QueryGenerator;
using Ironmon.Sqlite.Definitions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using System.Reflection;

namespace Ironmon.Tracker.Tests.SqlGenerator;

/// <summary>
/// Verifies native maintenance declarations fail closed when scripts or typed contracts are unsupported.
/// </summary>
public sealed class SqliteMaintenanceGeneratorTests
{
    private const string _missing = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.MissingMaintenanceScript.cs";
    private const string _unknown = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.UnknownMaintenanceOperation.cs";
    private const string _invalid = "Ironmon.Tracker.Tests.SqlGenerator.Fixtures.InvalidMaintenanceSignature.cs";
    private const string _assembly = "MaintenanceGeneratorFixture";
    private const string _platformAssemblies = "TRUSTED_PLATFORM_ASSEMBLIES";
    private const string _diagnostic = "ISQL002";

    /// <summary>
    /// Rejects unsupported maintenance declarations without emitting an execution method or runtime fallback.
    /// </summary>
    /// <param name="fixture">The independent malformed declaration fixture.</param>
    /// <param name="message">The expected fail-closed diagnostic explanation.</param>
    [Theory]
    [InlineData(_missing, "missing from compiler AdditionalFiles")]
    [InlineData(_unknown, "Unknown native SQLite maintenance operation")]
    [InlineData(_invalid, "incompatible parameter types")]
    public void InvalidMaintenanceDeclarationProducesNoImplementation(string fixture, string message)
    {
        using Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(fixture)!;
        using StreamReader reader = new(input);
        SyntaxTree syntax = CSharpSyntaxTree.ParseText(reader.ReadToEnd());
        string platforms = (string?)AppContext.GetData(_platformAssemblies) ?? throw new InvalidOperationException("The test runtime did not expose platform references.");
        MetadataReference[] references = [.. platforms.Split(Path.PathSeparator).Append(typeof(Sql).Assembly.Location).Append(typeof(SqliteConnection).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path))];
        CSharpCompilation compilation = CSharpCompilation.Create(_assembly, [syntax], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SqliteMaintenanceGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        Diagnostic failure = Assert.Single(diagnostics, diagnostic => diagnostic.Id == _diagnostic);
        Assert.Equal(DiagnosticSeverity.Error, failure.Severity);
        Assert.Contains(message, failure.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }
}
