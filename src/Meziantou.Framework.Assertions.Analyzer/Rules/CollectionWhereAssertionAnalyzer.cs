using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionWhereAssertionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseContainsForWhereDescriptor = new(
        id: RuleIdentifiers.UseContainsForWhereDiagnosticId,
        title: "Use Assert.Contains instead of Assert.NotEmpty(collection.Where(...))",
        messageFormat: "Use Assert.Contains(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseDoesNotContainForWhereDescriptor = new(
        id: RuleIdentifiers.UseDoesNotContainForWhereDiagnosticId,
        title: "Use Assert.DoesNotContain instead of Assert.Empty(collection.Where(...))",
        messageFormat: "Use Assert.DoesNotContain(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseSingleWithPredicateDescriptor = new(
        id: RuleIdentifiers.UseSingleWithPredicateDiagnosticId,
        title: "Use Assert.Single with a predicate instead of Assert.Single(collection.Where(...))",
        messageFormat: "Use Assert.Single(actual, predicate) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseContainsForWhereDescriptor, UseDoesNotContainForWhereDescriptor, UseSingleWithPredicateDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !CollectionWhereAssertionAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(
                context => Analyze(context, assertType, symbols.Value),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, CollectionWhereAssertionAnalyzerCommon.Symbols symbols)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!CollectionWhereAssertionAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, symbols, out var match))
            return;

        var descriptor = match.AssertionMethodName switch
        {
            "Contains" => UseContainsForWhereDescriptor,
            "DoesNotContain" => UseDoesNotContainForWhereDescriptor,
            _ => UseSingleWithPredicateDescriptor,
        };

        context.ReportDiagnostic(Diagnostic.Create(descriptor, invocationOperation.Syntax.GetLocation()));
    }
}
