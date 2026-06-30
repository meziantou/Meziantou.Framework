using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringStartsWithAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseStringStartsWithDescriptor = new(
        id: RuleIdentifiers.UseStringStartsWithDiagnosticId,
        title: "Use Assert.StartsWith instead of Assert.True(string.StartsWith(...))",
        messageFormat: "Use Assert.StartsWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringDoesNotStartWithDescriptor = new(
        id: RuleIdentifiers.UseStringDoesNotStartWithDiagnosticId,
        title: "Use Assert.DoesNotStartWith instead of Assert.False(string.StartsWith(...))",
        messageFormat: "Use Assert.DoesNotStartWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseStringStartsWithDescriptor, UseStringDoesNotStartWithDescriptor];

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

        if (innerInvocation.TargetMethod.Name != "StartsWith")
            return;

        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetStringOperationMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out var match))
            return;

        var descriptor = conditionExpectedToBeFalse ? UseStringDoesNotStartWithDescriptor : UseStringStartsWithDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.InnerInvocation.Syntax.GetLocation()));
    }
}
