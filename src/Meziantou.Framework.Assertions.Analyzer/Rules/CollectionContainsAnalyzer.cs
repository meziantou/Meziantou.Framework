using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionContainsAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseCollectionContainsDescriptor = new(
        id: RuleIdentifiers.UseCollectionContainsDiagnosticId,
        title: "Use Assert.Contains instead of Assert.True(collection.Contains(...))",
        messageFormat: "Use Assert.Contains(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseCollectionDoesNotContainDescriptor = new(
        id: RuleIdentifiers.UseCollectionDoesNotContainDiagnosticId,
        title: "Use Assert.DoesNotContain instead of Assert.False(collection.Contains(...))",
        messageFormat: "Use Assert.DoesNotContain(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseCollectionContainsDescriptor, UseCollectionDoesNotContainDescriptor];

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

        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetCollectionContainsMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out var match) &&
            !TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetDictionaryContainsKeyMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
        {
            return;
        }

        var descriptor = conditionExpectedToBeFalse ? UseCollectionDoesNotContainDescriptor : UseCollectionContainsDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.InnerInvocation.Syntax.GetLocation()));
    }
}
