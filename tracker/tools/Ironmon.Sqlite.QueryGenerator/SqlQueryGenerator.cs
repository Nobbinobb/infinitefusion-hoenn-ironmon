using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ironmon.Sqlite.QueryGenerator;

/// <summary>
/// Compiles the supported typed query definitions into direct Microsoft.Data.Sqlite methods.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SqlQueryGenerator : IIncrementalGenerator
{
    private const string _attribute = "Ironmon.Sqlite.Querying.SqlQueriesAttribute";
    private const string _definitions = "Definitions";
    private const string _diagnosticId = "ISQL001";
    private const string _category = "QueryGeneration";
    private const string _hintSuffix = ".SqlQueries.g.cs";
    private const string _format = "{0}";
    private static readonly DiagnosticDescriptor _unsupported = new(_diagnosticId, "Unsupported SQLite query definition", _format, _category, DiagnosticSeverity.Error, true);

    /// <summary>
    /// Registers semantic discovery of explicitly attributed query classes.
    /// </summary>
    /// <param name="context">The compiler's incremental source-generation registration context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(_attribute, static (node, _) => node is ClassDeclarationSyntax, static (attribute, _) => (INamedTypeSymbol)attribute.TargetSymbol);
        context.RegisterSourceOutput(targets.Combine(context.CompilationProvider), static (output, input) => Generate(output, input.Left, input.Right));
    }

    /// <summary>
    /// Emits a class only when all of its definitions are supported and valid.
    /// </summary>
    /// <param name="context">Receives generated source or compiler errors.</param>
    /// <param name="target">The attributed outer partial query class.</param>
    /// <param name="compilation">The current semantic compilation, including mapped query models.</param>
    private static void Generate(SourceProductionContext context, INamedTypeSymbol target, Compilation compilation)
    {
        Location location = target.Locations.First();
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!target.IsStatic || target.ContainingType is not null || target.TypeParameters.Length != 0 || target.ContainingNamespace.IsGlobalNamespace)
                throw new InvalidOperationException("A query container must be a non-generic, top-level static partial class in a namespace.");

            if (target.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(context.CancellationToken) is not ClassDeclarationSyntax declaration || !declaration.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword))))
                throw new InvalidOperationException("The attributed query container must be partial.");

            INamedTypeSymbol definitions = target.GetTypeMembers(_definitions).SingleOrDefault() ?? throw new InvalidOperationException("A query container needs one nested static Definitions class.");
            if (!definitions.IsStatic || definitions.TypeParameters.Length != 0)
                throw new InvalidOperationException("Definitions must be a non-generic static class.");

            var plans = definitions.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary).Select(method => SqliteQueryCompiler.Compile(method, compilation, context.CancellationToken)).ToArray();
            if (plans.Length == 0)
                throw new InvalidOperationException("Definitions must contain at least one query method.");

            if (plans.Select(plan => plan.Method.Name).Distinct(StringComparer.Ordinal).Count() != plans.Length || plans.Any(plan => target.GetMembers(plan.Method.Name).Length != 0))
                throw new InvalidOperationException("Each generated query needs a unique method name that is not already declared on the outer class.");

            string hint = target.ToDisplayString().Replace('.', '_') + _hintSuffix;
            context.AddSource(hint, SourceText.From(SqliteProviderEmitter.Emit(target, plans), Encoding.UTF8));
        }
        catch (InvalidOperationException error)
        {
            context.ReportDiagnostic(Diagnostic.Create(_unsupported, location, error.Message));
        }
    }
}
