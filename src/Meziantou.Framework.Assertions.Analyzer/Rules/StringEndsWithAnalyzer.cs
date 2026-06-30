using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringEndsWithAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseStringEndsWithDescriptor = new(
        id: RuleIdentifiers.UseStringEndsWithDiagnosticId,
        title: "Use Assert.EndsWith instead of Assert.True(string.EndsWith(...))",
        messageFormat: "Use Assert.EndsWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseStringDoesNotEndWithDescriptor = new(
        id: RuleIdentifiers.UseStringDoesNotEndWithDiagnosticId,
        title: "Use Assert.DoesNotEndWith instead of Assert.False(string.EndsWith(...))",
        messageFormat: "Use Assert.DoesNotEndWith(expected, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseStringEndsWithDescriptor, UseStringDoesNotEndWithDescriptor];

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

        if (innerInvocation.TargetMethod.Name != "EndsWith")
            return;

        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetStringOperationMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out var match))
            return;

        var descriptor = conditionExpectedToBeFalse ? UseStringDoesNotEndWithDescriptor : UseStringEndsWithDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.InnerInvocation.Syntax.GetLocation()));
    }
}
