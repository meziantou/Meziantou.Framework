using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionAnyWithoutPredicateAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseNotEmptyDescriptor = new(
        id: RuleIdentifiers.UseNotEmptyForAnyDiagnosticId,
        title: "Use Assert.NotEmpty instead of Assert.True(collection.Any())",
        messageFormat: "Use Assert.NotEmpty(actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseEmptyDescriptor = new(
        id: RuleIdentifiers.UseEmptyForAnyDiagnosticId,
        title: "Use Assert.Empty instead of Assert.False(collection.Any())",
        messageFormat: "Use Assert.Empty(actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseNotEmptyDescriptor, UseEmptyDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !TrueFalseConditionMethodSelectionAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(
                context => Analyze(context, assertType, symbols.Value),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, TrueFalseConditionMethodSelectionAnalyzerCommon.Symbols symbols)
    {
        var assertInvocation = (IInvocationOperation)context.Operation;
        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetAssertInnerInvocation(assertInvocation, assertType, out var innerInvocation, out var conditionExpectedToBeFalse))
            return;

        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetCollectionAnyWithoutPredicateMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out var match))
            return;

        var descriptor = conditionExpectedToBeFalse ? UseEmptyDescriptor : UseNotEmptyDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.InnerInvocation.Syntax.GetLocation()));
    }
}
