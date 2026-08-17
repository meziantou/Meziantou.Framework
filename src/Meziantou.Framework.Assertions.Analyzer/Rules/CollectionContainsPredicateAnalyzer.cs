using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionContainsPredicateAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseContainsWithExpectedValueDescriptor = new(
        id: RuleIdentifiers.UseContainsWithExpectedValueDiagnosticId,
        title: "Use Assert.Contains with the expected value instead of an equality predicate",
        messageFormat: "Use Assert.Contains(expected, actual) so the expected value is reported",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseDoesNotContainWithExpectedValueDescriptor = new(
        id: RuleIdentifiers.UseDoesNotContainWithExpectedValueDiagnosticId,
        title: "Use Assert.DoesNotContain with the expected value instead of an equality predicate",
        messageFormat: "Use Assert.DoesNotContain(expected, actual) so the expected value is reported",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseContainsWithExpectedValueDescriptor, UseDoesNotContainWithExpectedValueDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!CollectionContainsPredicateAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, out var match))
            return;

        var descriptor = match.AssertionMethodName == "Contains" ? UseContainsWithExpectedValueDescriptor : UseDoesNotContainWithExpectedValueDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, invocationOperation.Syntax.GetLocation()));
    }
}
